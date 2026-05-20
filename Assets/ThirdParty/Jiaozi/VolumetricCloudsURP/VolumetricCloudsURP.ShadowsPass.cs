using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using Unity.Mathematics;
using static Unity.Mathematics.math;

#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

public partial class VolumetricCloudsURP
{
    public partial class VolumetricCloudsShadowsPass : ScriptableRenderPass
    {
        private const string profilerTag = "Volumetric Clouds Shadows";
        private readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler(profilerTag);

        public VolumetricClouds cloudsVolume;
    #if URP_PBSKY
        public VisualEnvironment visualEnvVolume;
    #endif
        private readonly Material cloudsMaterial;

        private RTHandle shadowTextureHandle;
        private RTHandle intermediateShadowTextureHandle;

        private readonly Vector3[] frustumCorners = new Vector3[4];

        private Light targetLight;

        private static readonly Matrix4x4 s_DirLightProj = Matrix4x4.Ortho(-0.5f, 0.5f, -0.5f, 0.5f, -0.5f, 0.5f);

        public VolumetricCloudsShadowsPass(Material material)
        {
            cloudsMaterial = material;
        }

    #if !UNITY_6000_0_OR_NEWER
        #region Non Render Graph Pass
        private Light GetMainLight(LightData lightData)
        {
            int shadowLightIndex = lightData.mainLightIndex;
            if (shadowLightIndex != -1)
            {
                VisibleLight shadowLight = lightData.visibleLights[shadowLightIndex];
                Light light = shadowLight.light;
                if ((light.shadows != LightShadows.None || RenderSettings.sun != null && !RenderSettings.sun.isActiveAndEnabled) && shadowLight.lightType == LightType.Directional)
                    return light;
            }

            return RenderSettings.sun;
        }

    #if UNITY_6000_0_OR_NEWER
        [Obsolete]
    #endif
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            int shadowResolution = (int)cloudsVolume.shadowResolution.value;
            GraphicsFormat cookieFormat = GetShadowCookieFormat();
            RenderTextureDescriptor desc = CreateShadowCookieDescriptor(renderingData.cameraData.cameraTargetDescriptor, shadowResolution, cookieFormat);
            ReAllocateShadowTexture(ref shadowTextureHandle, desc, _VolumetricCloudsShadowTexture);
            ReAllocateShadowTexture(ref intermediateShadowTextureHandle, desc, _VolumetricCloudsShadowTempTexture);

            ConfigureTarget(shadowTextureHandle, shadowTextureHandle);
        }

    #if UNITY_6000_0_OR_NEWER
        [Obsolete]
    #endif
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (cloudsVolume == null || cloudsMaterial == null)
                return;

            CameraData cameraData = renderingData.cameraData;
            Camera camera = cameraData.camera;
            if (camera == null)
                return;

            LightData lightData = renderingData.lightData;

            bool isStereoEnabled = camera.stereoEnabled;

            // Get and update the main light
            Light light = GetMainLight(lightData);
            if (targetLight != light)
            {
                ResetShadowCookie();
                targetLight = light;
            }

            // Check if we need shadow cookie
            bool hasVolumetricCloudsShadows = targetLight != null && targetLight.isActiveAndEnabled && targetLight.intensity != 0.0f;
            if (!hasVolumetricCloudsShadows)
            {
                ResetShadowCookie();
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                if (isStereoEnabled)
                    cmd.DisableShaderKeyword(STEREO_INSTANCING_ON);

                Matrix4x4 wsToLSMat = targetLight.transform.worldToLocalMatrix;
                Matrix4x4 lsToWSMat = targetLight.transform.localToWorldMatrix;

                float3 cameraPos = camera.transform.position;

                float perspectiveCorrectedShadowDistance = cloudsVolume.shadowDistance.value / cos(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);

                camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), camera.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, frustumCorners);

                // Generate the light space bounds of the camera frustum
                Bounds lightSpaceBounds = new Bounds();
                lightSpaceBounds.SetMinMax(new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), new Vector3(-float.MaxValue, -float.MaxValue, -float.MaxValue));
                lightSpaceBounds.Encapsulate(wsToLSMat.MultiplyPoint(cameraPos));
                for (int cornerIdx = 0; cornerIdx < 4; ++cornerIdx)
                {
                    Vector3 corner = frustumCorners[cornerIdx];
                    float diag = corner.magnitude;
                    corner = (corner / diag) * Mathf.Min(perspectiveCorrectedShadowDistance, diag);
                    Vector3 posLightSpace = wsToLSMat.MultiplyPoint(new float3(corner) + cameraPos);
                    lightSpaceBounds.Encapsulate(posLightSpace);

                    posLightSpace = wsToLSMat.MultiplyPoint(new float3(-corner) + cameraPos);
                    lightSpaceBounds.Encapsulate(posLightSpace);
                }

                // Compute the four corners we need
                float3 c0 = lsToWSMat.MultiplyPoint(lightSpaceBounds.center + new Vector3(-lightSpaceBounds.extents.x, -lightSpaceBounds.extents.y, lightSpaceBounds.extents.z));
                float3 c1 = lsToWSMat.MultiplyPoint(lightSpaceBounds.center + new Vector3(lightSpaceBounds.extents.x, -lightSpaceBounds.extents.y, lightSpaceBounds.extents.z));
                float3 c2 = lsToWSMat.MultiplyPoint(lightSpaceBounds.center + new Vector3(-lightSpaceBounds.extents.x, lightSpaceBounds.extents.y, lightSpaceBounds.extents.z));

            #if URP_PBSKY
                bool isVolumeActive = visualEnvVolume != null && visualEnvVolume.IsActive();

                float fallbackEarthRad = Mathf.Lerp(1.0f, 0.025f, cloudsVolume.earthCurvature.value) * VolumetricCloudsPass.earthRad;
                float4 planetCenterRad = isVolumeActive ? visualEnvVolume.GetPlanetCenterRadius(camera.transform.position) : float4(0.0f, -fallbackEarthRad, 0.0f, fallbackEarthRad);
                float actualEarthRad = isVolumeActive ? planetCenterRad.w : Mathf.Lerp(1.0f, 0.025f, cloudsVolume.earthCurvature.value) * VolumetricCloudsPass.earthRad;
                float3 planetCenterPos = isVolumeActive ? planetCenterRad.xyz : float3(0.0f, -actualEarthRad, 0.0f);
            #else
                float actualEarthRad = Mathf.Lerp(1.0f, 0.025f, cloudsVolume.earthCurvature.value) * VolumetricCloudsPass.earthRad;
                float3 planetCenterPos = float3(0.0f, -actualEarthRad, 0.0f);
            #endif

                float3 dirX = c1 - c0;
                float3 dirY = c2 - c0;

                // The shadow cookie size
                float2 regionSize = float2(length(dirX), length(dirY));

                int shadowResolution = (int)cloudsVolume.shadowResolution.value;

                // Update material properties
                cloudsMaterial.SetFloat(shadowCookieResolution, shadowResolution);
                //cloudsMaterial.SetFloat(shadowPlaneOffset, cloudsVolume.shadowPlaneHeightOffset.value);
                cloudsMaterial.SetFloat(shadowIntensity, cloudsVolume.shadowOpacity.value);
                cloudsMaterial.SetFloat(shadowOpacityFallback, 1.0f - cloudsVolume.shadowOpacityFallback.value);
                cloudsMaterial.SetVector(cloudShadowSunOrigin, float4(c0 - planetCenterPos, 1.0f));
                cloudsMaterial.SetVector(cloudShadowSunRight, float4(dirX, 0.0f));
                cloudsMaterial.SetVector(cloudShadowSunUp, float4(dirY, 0.0f));
                cloudsMaterial.SetVector(cloudShadowSunForward, float4(-targetLight.transform.forward, 0.0f));
                cloudsMaterial.SetVector(cameraPositionPS, float4(cameraPos - planetCenterPos, 0.0f));
                cmd.SetGlobalVector(volumetricCloudsShadowOriginToggle, float4(c0, 0.0f));
                cmd.SetGlobalVector(volumetricCloudsShadowScale, float4(regionSize, 0.0f, 0.0f)); // Used in physically based sky

                // Apply light cookie settings
                targetLight.cookie = null;
                UniversalAdditionalLightData additonal = targetLight.GetComponent<UniversalAdditionalLightData>();
                if (additonal != null)
                {
                    additonal.lightCookieSize = Vector2.one;
                    additonal.lightCookieOffset = Vector2.zero;
                }

                Vector2 uvScale = 1 / regionSize;
                float minHalfValue = Unity.Mathematics.half.MinValue;
                if (Mathf.Abs(uvScale.x) < minHalfValue)
                    uvScale.x = Mathf.Sign(uvScale.x) * minHalfValue;
                if (Mathf.Abs(uvScale.y) < minHalfValue)
                    uvScale.y = Mathf.Sign(uvScale.y) * minHalfValue;

                Matrix4x4 cookieUVTransform = Matrix4x4.Scale(new Vector3(uvScale.x, uvScale.y, 1));
                lsToWSMat.SetColumn(3, float4(cameraPos, 1));
                Matrix4x4 cookieMatrix = s_DirLightProj * cookieUVTransform * lsToWSMat.inverse;

                float cookieFormat = (float)GetLightCookieShaderFormat(shadowTextureHandle.rt.graphicsFormat);

                cmd.SetGlobalTexture(mainLightTexture, shadowTextureHandle);
                cmd.SetGlobalMatrix(mainLightWorldToLight, cookieMatrix);
                cmd.SetGlobalFloat(mainLightCookieTextureFormat, cookieFormat);
                cmd.EnableShaderKeyword(_LIGHT_COOKIES);

                // Render shadow cookie texture
                Blitter.BlitCameraTexture(cmd, shadowTextureHandle, shadowTextureHandle, cloudsMaterial, pass: 4);

                // Given the low number of steps available and the absence of noise in the integration, we try to reduce the artifacts by doing two consecutive 3x3 blur passes.
                Blitter.BlitCameraTexture(cmd, shadowTextureHandle, intermediateShadowTextureHandle, cloudsMaterial, pass: 5);
                Blitter.BlitCameraTexture(cmd, intermediateShadowTextureHandle, shadowTextureHandle, cloudsMaterial, pass: 5);

                if (isStereoEnabled)
                    cmd.EnableShaderKeyword(STEREO_INSTANCING_ON);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
        #endregion
    #endif

    #if UNITY_6000_0_OR_NEWER
        #region Render Graph Pass
        private Light GetMainLight(UniversalLightData lightData)
        {
            int shadowLightIndex = lightData.mainLightIndex;
            if (shadowLightIndex != -1)
            {
                VisibleLight shadowLight = lightData.visibleLights[shadowLightIndex];
                Light light = shadowLight.light;
                if ((light.shadows != LightShadows.None || RenderSettings.sun != null && !RenderSettings.sun.isActiveAndEnabled) && shadowLight.lightType == LightType.Directional)
                    return light;
            }

            return RenderSettings.sun;
        }

        private class PassData
        {
            internal Material cloudsMaterial;

            internal TextureHandle intermediateShadowTexture;
            internal TextureHandle shadowTexture;

            internal Matrix4x4 mainLightWorldToLight;
            internal float mainLightCookieTextureFormat;

            internal Vector4 shadowOriginToggle;
            internal Vector4 shadowScale;

            internal bool isStereoEnabled;
        }

        // This static method is used to execute the pass and passed as the RenderFunc delegate to the RenderGraph render pass
        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            if (data.isStereoEnabled)
                cmd.DisableShaderKeyword(STEREO_INSTANCING_ON);

            // Render shadow cookie texture
            Blitter.BlitCameraTexture(cmd, data.shadowTexture, data.shadowTexture, data.cloudsMaterial, pass: 4);

            // Given the low number of steps available and the absence of noise in the integration, we try to reduce the artifacts by doing two consecutive 3x3 blur passes.
            Blitter.BlitCameraTexture(cmd, data.shadowTexture, data.intermediateShadowTexture, data.cloudsMaterial, pass: 5);
            Blitter.BlitCameraTexture(cmd, data.intermediateShadowTexture, data.shadowTexture, data.cloudsMaterial, pass: 5);

            cmd.SetGlobalVector(volumetricCloudsShadowOriginToggle, data.shadowOriginToggle);
            cmd.SetGlobalVector(volumetricCloudsShadowScale, data.shadowScale); // Used in physically based sky

            cmd.SetGlobalTexture(mainLightTexture, data.shadowTexture);
            cmd.SetGlobalMatrix(mainLightWorldToLight, data.mainLightWorldToLight);
            cmd.SetGlobalFloat(mainLightCookieTextureFormat, data.mainLightCookieTextureFormat);
            cmd.EnableShaderKeyword(_LIGHT_COOKIES);

            if (data.isStereoEnabled)
                cmd.EnableShaderKeyword(STEREO_INSTANCING_ON);
        }

        // This is where the renderGraph handle can be accessed.
        // Each ScriptableRenderPass can use the RenderGraph handle to add multiple render passes to the render graph
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (cloudsVolume == null || cloudsMaterial == null)
                return;

            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            // Get and update the main light
            Light light = GetMainLight(lightData);
            if (targetLight != light)
            {
                ResetShadowCookie();
                targetLight = light;
            }

            // Check if we need shadow cookie
            bool hasVolumetricCloudsShadows = targetLight != null && targetLight.isActiveAndEnabled && targetLight.intensity != 0.0f;
            if (!hasVolumetricCloudsShadows)
            {
                ResetShadowCookie();
                return;
            }

            var camera = cameraData.camera;
            if (camera == null)
                return;

            // add an unsafe render pass to the render graph, specifying the name and the data type that will be passed to the ExecutePass function
            using (var builder = renderGraph.AddUnsafePass<PassData>(profilerTag, out var passData))
            {
                // UniversalResourceData contains all the texture handles used by the renderer, including the active color and depth textures
                // The active color and depth textures are the main color and depth buffers that the camera renders into

                Matrix4x4 wsToLSMat = targetLight.transform.worldToLocalMatrix;
                Matrix4x4 lsToWSMat = targetLight.transform.localToWorldMatrix;

                float3 cameraPos = camera.transform.position;

                float perspectiveCorrectedShadowDistance = cloudsVolume.shadowDistance.value / cos(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);

                camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), camera.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, frustumCorners);

                // Generate the light space bounds of the camera frustum
                Bounds lightSpaceBounds = new Bounds();
                lightSpaceBounds.SetMinMax(new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), new Vector3(-float.MaxValue, -float.MaxValue, -float.MaxValue));
                lightSpaceBounds.Encapsulate(wsToLSMat.MultiplyPoint(cameraPos));
                for (int cornerIdx = 0; cornerIdx < 4; ++cornerIdx)
                {
                    Vector3 corner = frustumCorners[cornerIdx];
                    float diag = corner.magnitude;
                    corner = (corner / diag) * Mathf.Min(perspectiveCorrectedShadowDistance, diag);
                    Vector3 posLightSpace = wsToLSMat.MultiplyPoint(float3(corner) + cameraPos);
                    lightSpaceBounds.Encapsulate(posLightSpace);

                    posLightSpace = wsToLSMat.MultiplyPoint(float3(-corner) + cameraPos);
                    lightSpaceBounds.Encapsulate(posLightSpace);
                }

                // Compute the four corners we need
                float3 c0 = lsToWSMat.MultiplyPoint(lightSpaceBounds.center + new Vector3(-lightSpaceBounds.extents.x, -lightSpaceBounds.extents.y, lightSpaceBounds.extents.z));
                float3 c1 = lsToWSMat.MultiplyPoint(lightSpaceBounds.center + new Vector3(lightSpaceBounds.extents.x, -lightSpaceBounds.extents.y, lightSpaceBounds.extents.z));
                float3 c2 = lsToWSMat.MultiplyPoint(lightSpaceBounds.center + new Vector3(-lightSpaceBounds.extents.x, lightSpaceBounds.extents.y, lightSpaceBounds.extents.z));

            #if URP_PBSKY
                bool isVolumeActive = visualEnvVolume != null && visualEnvVolume.IsActive();

                float fallbackEarthRad = Mathf.Lerp(1.0f, 0.025f, cloudsVolume.earthCurvature.value) * VolumetricCloudsPass.earthRad;
                float4 planetCenterRad = isVolumeActive ? visualEnvVolume.GetPlanetCenterRadius(camera.transform.position) : float4(0.0f, -fallbackEarthRad, 0.0f, fallbackEarthRad);
                float actualEarthRad = isVolumeActive ? planetCenterRad.w : Mathf.Lerp(1.0f, 0.025f, cloudsVolume.earthCurvature.value) * VolumetricCloudsPass.earthRad;
                float3 planetCenterPos = isVolumeActive ? planetCenterRad.xyz : float3(0.0f, -actualEarthRad, 0.0f);
            #else
                float actualEarthRad = Mathf.Lerp(1.0f, 0.025f, cloudsVolume.earthCurvature.value) * VolumetricCloudsPass.earthRad;
                float3 planetCenterPos = float3(0.0f, -actualEarthRad, 0.0f);
            #endif

                float3 dirX = c1 - c0;
                float3 dirY = c2 - c0;

                // The shadow cookie size
                float2 regionSize = float2(length(dirX), length(dirY));

                int shadowResolution = (int)cloudsVolume.shadowResolution.value;
                GraphicsFormat cookieTextureFormat = GetShadowCookieFormat();
                RenderTextureDescriptor desc = CreateShadowCookieDescriptor(cameraData.cameraTargetDescriptor, shadowResolution, cookieTextureFormat);
                ReAllocateShadowTexture(ref shadowTextureHandle, desc, _VolumetricCloudsShadowTexture);
                TextureHandle shadowTexture = renderGraph.ImportTexture(shadowTextureHandle);

                TextureHandle intermediateShadowTexture = renderGraph.CreateTexture(new TextureDesc(shadowResolution, shadowResolution, false, false)
                { colorFormat = cookieTextureFormat, enableRandomWrite = false, name = _VolumetricCloudsShadowTempTexture });

                // Update material properties
                cloudsMaterial.SetFloat(shadowCookieResolution, shadowResolution);
                //cloudsMaterial.SetFloat(shadowPlaneOffset, cloudsVolume.shadowPlaneHeightOffset.value);
                cloudsMaterial.SetFloat(shadowIntensity, cloudsVolume.shadowOpacity.value);
                cloudsMaterial.SetFloat(shadowOpacityFallback, 1.0f - cloudsVolume.shadowOpacityFallback.value);
                cloudsMaterial.SetVector(cloudShadowSunOrigin, float4(c0 - planetCenterPos, 1.0f));
                cloudsMaterial.SetVector(cloudShadowSunRight, float4(dirX, 0.0f));
                cloudsMaterial.SetVector(cloudShadowSunUp, float4(dirY, 0.0f));
                cloudsMaterial.SetVector(cloudShadowSunForward, float4(-targetLight.transform.forward, 0.0f));
                cloudsMaterial.SetVector(cameraPositionPS, float4(cameraPos - planetCenterPos, 0.0f));
                cloudsMaterial.SetVector(volumetricCloudsShadowOriginToggle, float4(c0, 0.0f));

                // Apply light cookie settings
                targetLight.cookie = null;
                UniversalAdditionalLightData additonal = targetLight.GetComponent<UniversalAdditionalLightData>();
                if (additonal != null)
                {
                    additonal.lightCookieSize = Vector2.one;
                    additonal.lightCookieOffset = Vector2.zero;
                }

                // Apply shadow cookie
                Vector2 uvScale = 1 / regionSize;
                float minHalfValue = Unity.Mathematics.half.MinValue;
                if (Mathf.Abs(uvScale.x) < minHalfValue)
                    uvScale.x = Mathf.Sign(uvScale.x) * minHalfValue;
                if (Mathf.Abs(uvScale.y) < minHalfValue)
                    uvScale.y = Mathf.Sign(uvScale.y) * minHalfValue;

                Matrix4x4 cookieUVTransform = Matrix4x4.Scale(new Vector3(uvScale.x, uvScale.y, 1));
                //cookieUVTransform.SetColumn(3, new Vector4(uvScale.x, uvScale.y, 0, 1));
                lsToWSMat.SetColumn(3, float4(cameraPos, 1));
                Matrix4x4 cookieMatrix = s_DirLightProj * cookieUVTransform * lsToWSMat.inverse;

                float cookieFormat = (float)GetLightCookieShaderFormat(cookieTextureFormat);

                // Fill up the passData with the data needed by the pass
                passData.cloudsMaterial = cloudsMaterial;
                passData.shadowTexture = shadowTexture;
                passData.intermediateShadowTexture = intermediateShadowTexture;
                passData.mainLightWorldToLight = cookieMatrix;
                passData.mainLightCookieTextureFormat = cookieFormat;
                passData.shadowOriginToggle = float4(c0, 0.0f);
                passData.shadowScale = float4(regionSize, 0.0f, 0.0f);
                passData.isStereoEnabled = cameraData.camera.stereoEnabled;

                // UnsafePasses don't setup the outputs using UseTextureFragment/UseTextureFragmentDepth, you should specify your writes with UseTexture instead
                builder.UseTexture(passData.shadowTexture, AccessFlags.Write);
                builder.UseTexture(passData.intermediateShadowTexture, AccessFlags.Write); // We always write to it before reading

                // Shader keyword changes (_LIGHT_COOKIES) are considered as global state modifications
                builder.AllowGlobalStateModification(true);

                // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }
        #endregion
    #endif

        #region Shared
        private enum LightCookieShaderFormat
        {
            None = -1,

            RGB = 0,
            Alpha = 1,
            Red = 2
        }

        private LightCookieShaderFormat GetLightCookieShaderFormat(GraphicsFormat cookieFormat)
        {
            // TODO: convert this to use GraphicsFormatUtility
            switch (cookieFormat)
            {
                default:
                    return LightCookieShaderFormat.RGB;
                // A8, A16 GraphicsFormat does not expose yet.
                case (GraphicsFormat)54:
                case (GraphicsFormat)55:
                    return LightCookieShaderFormat.Alpha;
                case GraphicsFormat.R8_SRGB:
                case GraphicsFormat.R8_UNorm:
                case GraphicsFormat.R8_UInt:
                case GraphicsFormat.R8_SNorm:
                case GraphicsFormat.R8_SInt:
                case GraphicsFormat.R16_UNorm:
                case GraphicsFormat.R16_UInt:
                case GraphicsFormat.R16_SNorm:
                case GraphicsFormat.R16_SInt:
                case GraphicsFormat.R16_SFloat:
                case GraphicsFormat.R32_UInt:
                case GraphicsFormat.R32_SInt:
                case GraphicsFormat.R32_SFloat:
                case GraphicsFormat.R_BC4_SNorm:
                case GraphicsFormat.R_BC4_UNorm:
                case GraphicsFormat.R_EAC_SNorm:
                case GraphicsFormat.R_EAC_UNorm:
                    return LightCookieShaderFormat.Red;
            }
        }

        private void ResetShadowCookie()
        {
            if (targetLight != null)
            {
                targetLight.cookie = null;
                UniversalAdditionalLightData additionalData = targetLight.GetComponent<UniversalAdditionalLightData>();
                if (additionalData != null)
                {
                    additionalData.lightCookieSize = Vector2.one;
                    additionalData.lightCookieOffset = Vector2.zero;
                }
            }
        }

        public void Dispose()
        {
            ResetShadowCookie();
            shadowTextureHandle?.Release();
            intermediateShadowTextureHandle?.Release();
        }
        #endregion
    }
}
