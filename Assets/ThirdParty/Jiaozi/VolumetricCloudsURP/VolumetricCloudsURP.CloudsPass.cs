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
    public partial class VolumetricCloudsPass : ScriptableRenderPass
    {
        private const string rasterPassProfilerTag = "Trace Volumetric Clouds";
        private const string profilerTag = "Volumetric Clouds";
        private readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler(profilerTag);

        public VolumetricClouds cloudsVolume;
        public ColorAdjustments colorAdjustments;
    #if URP_PBSKY
        public VisualEnvironment visualEnvVolume;
    #endif
        public CloudsRenderMode renderMode;
        public float resolutionScale;
        public CloudsUpscaleMode upscaleMode;
        public bool dynamicAmbientProbe;
        public bool resetWindOnStart;
        public bool outputDepth;
        public bool outputToSceneDepth;
        public bool sunAttenuation;
        public bool hasAtmosphericScattering;

        private bool denoiseClouds;

        private RTHandle cloudsColorHandle;
        private RTHandle cloudsDepthHandle;
        private RTHandle accumulateHandle;
        private RTHandle historyHandle;
        private RTHandle cameraTempDepthHandle;

        private readonly Material cloudsMaterial;

        private readonly bool fastCopy = (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;

        private Texture2D customLutPresetMap;
        private readonly Color[] customLutColorArray = new Color[customLutMapResolution];

        public const float earthRad = 6378100.0f;
        public const float windNormalizationFactor = 100000.0f; // NOISE_TEXTURE_NORMALIZATION_FACTOR in "VolumetricCloudsUtilities.hlsl"
        public const int customLutMapResolution = 64;

        // Wind offsets
        private bool prevIsPlaying;
        private float prevTotalTime = -1.0f;
        private float verticalShapeOffset = 0.0f;
        private float verticalErosionOffset = 0.0f;
        private Vector2 windVector = Vector2.zero;

        private static float square(float x) => x * x;

        private void UpdateMaterialProperties(Camera camera)
        {
        #if URP_PBSKY
            bool isVolumeActive = visualEnvVolume != null && visualEnvVolume.IsActive() && visualEnvVolume.skyType.value != 0;
            if (isVolumeActive)
            {
                if (visualEnvVolume.renderingSpace.value == VisualEnvironment.RenderingSpace.World) { cloudsMaterial.EnableKeyword(localClouds); }
                else { cloudsMaterial.DisableKeyword(localClouds); }
            }
            else
            {
                if (cloudsVolume.localClouds.value) { cloudsMaterial.EnableKeyword(localClouds); }
                else { cloudsMaterial.DisableKeyword(localClouds); }
            }
        #else
            if (cloudsVolume.localClouds.value) { cloudsMaterial.EnableKeyword(localClouds); }
            else { cloudsMaterial.DisableKeyword(localClouds); }
        #endif

            if (cloudsVolume.microErosion.value && cloudsVolume.microErosionFactor.value > 0.0f) { cloudsMaterial.EnableKeyword(microErosion); }
            else { cloudsMaterial.DisableKeyword(microErosion); }

            if (resolutionScale < 1.0f && upscaleMode == CloudsUpscaleMode.Bilateral) { cloudsMaterial.EnableKeyword(lowResClouds); }
            else { cloudsMaterial.DisableKeyword(lowResClouds); }

            if (dynamicAmbientProbe) { cloudsMaterial.EnableKeyword(cloudsAmbientProbe); }
            else { cloudsMaterial.DisableKeyword(cloudsAmbientProbe); }

            if (outputDepth) { cloudsMaterial.EnableKeyword(outputCloudsDepth); }
            else { cloudsMaterial.DisableKeyword(outputCloudsDepth); }

            if (sunAttenuation) { cloudsMaterial.EnableKeyword(physicallyBasedSun); }
            else { cloudsMaterial.DisableKeyword(physicallyBasedSun); }

            if (cloudsVolume.perceptualBlending.value > 0.0f) { cloudsMaterial.EnableKeyword(perceptualBlending); }
            else { cloudsMaterial.DisableKeyword(perceptualBlending); }

            cloudsMaterial.SetFloat(numPrimarySteps, cloudsVolume.numPrimarySteps.value);
            cloudsMaterial.SetFloat(numLightSteps, cloudsVolume.numLightSteps.value);
            cloudsMaterial.SetFloat(maxStepSize, cloudsVolume.altitudeRange.value / 8.0f);

        #if URP_PBSKY
            float4 planetCenterRad = visualEnvVolume.GetPlanetCenterRadius(camera.transform.position);
            float actualEarthRad = isVolumeActive ? planetCenterRad.w : Mathf.Lerp(1.0f, 0.025f, cloudsVolume.earthCurvature.value) * earthRad;
            planetCenterRad = visualEnvVolume.renderingSpace.value == VisualEnvironment.RenderingSpace.World ? planetCenterRad : float4(0.0f, -actualEarthRad, 0.0f, actualEarthRad);

            cloudsMaterial.SetVector(planetCenterRadius, planetCenterRad);
        #else
            float actualEarthRad = Mathf.Lerp(1.0f, 0.025f, cloudsVolume.earthCurvature.value) * earthRad;

            cloudsMaterial.SetVector(planetCenterRadius, float4(0.0f, -actualEarthRad, 0.0f, actualEarthRad));
        #endif

            float bottomAltitude = cloudsVolume.bottomAltitude.value + actualEarthRad;
            float highestAltitude = bottomAltitude + cloudsVolume.altitudeRange.value;
            cloudsMaterial.SetFloat(highestCloudAltitude, highestAltitude);
            cloudsMaterial.SetFloat(lowestCloudAltitude, bottomAltitude);
            cloudsMaterial.SetVector(shapeNoiseOffset, new Vector4(cloudsVolume.shapeOffset.value.x, cloudsVolume.shapeOffset.value.z, 0.0f, 0.0f));
            cloudsMaterial.SetFloat(verticalShapeNoiseOffset, cloudsVolume.shapeOffset.value.y);

            // Wind animation
            float totalTime = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            float deltaTime = totalTime - prevTotalTime;
            if (prevTotalTime == -1.0f)
                deltaTime = 0.0f;

        #if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPaused)
                deltaTime = 0.0f;
        #endif

            // Conversion from km/h to m/s is the 0.277778f factor
            // We apply a minus to see something moving in the right direction
            deltaTime *= -0.277778f;

            float theta = cloudsVolume.globalOrientation.value / 180.0f * Mathf.PI;
            Vector2 windDirection = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));

            if (resetWindOnStart && prevIsPlaying != Application.isPlaying)
            {
                windVector = Vector2.zero;
                verticalShapeOffset = 0.0f;
                verticalErosionOffset = 0.0f;
            }
            else
            {
                windVector += deltaTime * cloudsVolume.globalSpeed.value * windDirection;
                verticalShapeOffset += deltaTime * cloudsVolume.verticalShapeWindSpeed.value;
                verticalErosionOffset += deltaTime * cloudsVolume.erosionSpeedMultiplier.value;
                // Reset the accumulated wind variables periodically to avoid extreme values.
                windVector.x %= windNormalizationFactor;
                windVector.y %= windNormalizationFactor;
                verticalShapeOffset %= windNormalizationFactor;
                verticalErosionOffset %= windNormalizationFactor;
            }

            // Update previous values
            prevTotalTime = totalTime;
            prevIsPlaying = Application.isPlaying;

            // We apply a minus to see something moving in the right direction
            cloudsMaterial.SetVector(globalOrientation, new Vector4(-windDirection.x, -windDirection.y, 0.0f, 0.0f));
            cloudsMaterial.SetVector(globalSpeed, windVector);
            cloudsMaterial.SetFloat(shapeSpeedMultiplier, cloudsVolume.shapeSpeedMultiplier.value);
            cloudsMaterial.SetFloat(erosionSpeedMultiplier, cloudsVolume.erosionSpeedMultiplier.value);
            cloudsMaterial.SetFloat(altitudeDistortion, cloudsVolume.altitudeDistortion.value * 0.25f);
            cloudsMaterial.SetFloat(verticalShapeDisplacement, verticalShapeOffset);
            cloudsMaterial.SetFloat(verticalErosionDisplacement, verticalErosionOffset);

            cloudsMaterial.SetFloat(densityMultiplier, cloudsVolume.densityMultiplier.value * cloudsVolume.densityMultiplier.value * 2.0f);
            cloudsMaterial.SetFloat(powderEffectIntensity, cloudsVolume.powderEffectIntensity.value);
            cloudsMaterial.SetFloat(shapeScale, cloudsVolume.shapeScale.value);
            cloudsMaterial.SetFloat(shapeFactor, cloudsVolume.shapeFactor.value);
            cloudsMaterial.SetFloat(erosionScale, cloudsVolume.erosionScale.value);
            cloudsMaterial.SetFloat(erosionFactor, cloudsVolume.erosionFactor.value);
            cloudsMaterial.SetFloat(erosionOcclusion, cloudsVolume.erosionOcclusion.value);
            cloudsMaterial.SetFloat(microErosionScale, cloudsVolume.microErosionScale.value);
            cloudsMaterial.SetFloat(microErosionFactor, cloudsVolume.microErosionFactor.value);

            bool autoFadeIn = cloudsVolume.fadeInMode.value == VolumetricClouds.CloudFadeInMode.Automatic;
            cloudsMaterial.SetFloat(fadeInStart, autoFadeIn ? Mathf.Max(cloudsVolume.altitudeRange.value * 0.2f, camera.nearClipPlane) : Mathf.Max(cloudsVolume.fadeInStart.value, camera.nearClipPlane));
            cloudsMaterial.SetFloat(fadeInDistance, autoFadeIn ? cloudsVolume.altitudeRange.value * 0.3f : cloudsVolume.fadeInDistance.value);
            cloudsMaterial.SetFloat(multiScattering, 1.0f - cloudsVolume.multiScattering.value * 0.95f);
            cloudsMaterial.SetColor(scatteringTint, Color.white - cloudsVolume.scatteringTint.value * 0.75f);
            cloudsMaterial.SetFloat(ambientProbeDimmer, cloudsVolume.ambientLightProbeDimmer.value);
            cloudsMaterial.SetFloat(sunLightDimmer, cloudsVolume.sunLightDimmer.value);
            cloudsMaterial.SetFloat(earthRadius, actualEarthRad);
            cloudsMaterial.SetFloat(accumulationFactor, cloudsVolume.temporalAccumulationFactor.value);
            cloudsMaterial.SetFloat(improvedTransmittanceBlend, cloudsVolume.perceptualBlending.value);
            Vector3 cameraPosPS = camera.transform.position - new Vector3(0.0f, -actualEarthRad, 0.0f);
            cloudsMaterial.SetFloat(cloudnearPlane, max(GetCloudNearPlane(cameraPosPS, bottomAltitude, highestAltitude), camera.nearClipPlane));

            // Custom cloud map is not supported yet.
            //float lowerCloudRadius = (bottomAltitude + highestAltitude) * 0.5f - actualEarthRad;
            //cloudsMaterial.SetFloat(normalizationFactor, Mathf.Sqrt((earthRad + lowerCloudRadius) * (earthRad + lowerCloudRadius) - earthRad * actualEarthRad));

            float postExposureLinear = colorAdjustments != null && colorAdjustments.active ? Mathf.Pow(2.0f, colorAdjustments.postExposure.value) : 1.0f;
            cloudsMaterial.SetFloat(postExposure, postExposureLinear);

            SetupAmbientProbeIfNeeded(cloudsMaterial);

            PrepareCustomLutData(cloudsVolume);
        }

        private void UpdateClouds(Light mainLight, Camera camera)
        {
            // When using PBSky, we already applied the sun attenuation to "_MainLightColor"
            if (sunAttenuation)
            {
                bool isLinearColorSpace = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Color mainLightColor = Color.black;
                if (mainLight != null)
                    mainLightColor = (isLinearColorSpace ? mainLight.color.linear : mainLight.color.gamma) * (mainLight.useColorTemperature ? Mathf.CorrelatedColorTemperatureToRGB(mainLight.colorTemperature) : Color.white) * mainLight.intensity;

            #if URP_PHYSICAL_LIGHT
                bool isPhysicalLight = mainLight.GetComponent<AdditionalLightData>() != null;

                mainLightColor = isPhysicalLight ? mainLightColor : mainLightColor * PI;
            #else
                mainLightColor *= PI;
            #endif

                // Pass the actual main light color to volumetric clouds shader.
                cloudsMaterial.SetVector(sunColor, mainLightColor);
            }

            // Update preset values
            VolumetricClouds.CloudPresets cloudPreset = cloudsVolume.cloudPreset;
            cloudsVolume.cloudPreset = cloudPreset;

            UpdateMaterialProperties(camera);
            denoiseClouds = cloudsVolume.temporalAccumulationFactor.value >= 0.01f;
        }

        private void PrepareCustomLutData(VolumetricClouds clouds)
        {
            if (customLutPresetMap == null)
            {
                customLutPresetMap = new Texture2D(1, customLutMapResolution, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.None)
                {
                    name = "Custom LUT Curve",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                customLutPresetMap.hideFlags = HideFlags.HideAndDontSave;
            }

            var pixels = customLutColorArray;

            var densityCurve = clouds.densityCurve.value;
            var erosionCurve = clouds.erosionCurve.value;
            var ambientOcclusionCurve = clouds.ambientOcclusionCurve.value;
            Color white = Color.white;
            if (densityCurve == null || densityCurve.length == 0)
            {
                for (int i = 0; i < customLutMapResolution; i++)
                    pixels[i] = white;
            }
            else
            {
                float step = 1.0f / (customLutMapResolution - 1f);

                for (int i = 0; i < customLutMapResolution; i++)
                {
                    float currTime = step * i;
                    float density = (i == 0 || i == customLutMapResolution - 1) ? 0 : Mathf.Clamp(densityCurve.Evaluate(currTime), 0.0f, 1.0f);
                    float erosion = Mathf.Clamp(erosionCurve.Evaluate(currTime), 0.0f, 1.0f);
                    float ambientOcclusion = Mathf.Clamp(1.0f - ambientOcclusionCurve.Evaluate(currTime), 0.0f, 1.0f);
                    pixels[i] = new Color(density, erosion, ambientOcclusion, 1.0f);
                }
            }

            customLutPresetMap.SetPixels(pixels);
            customLutPresetMap.Apply();

            cloudsMaterial.SetTexture(cloudsCurveLut, customLutPresetMap);
        }

        private void SetupAmbientProbeIfNeeded(Material cloudsMaterial)
        {
            if (!dynamicAmbientProbe)
            {
                SphericalHarmonicsL2 ambientProbe = RenderSettings.ambientProbe;

                cloudsMaterial.SetVector(shAr, new Vector4(ambientProbe[0, 3], ambientProbe[0, 1], ambientProbe[0, 2], ambientProbe[0, 0] - ambientProbe[0, 6]));
                cloudsMaterial.SetVector(shAg, new Vector4(ambientProbe[1, 3], ambientProbe[1, 1], ambientProbe[1, 2], ambientProbe[1, 0] - ambientProbe[1, 6]));
                cloudsMaterial.SetVector(shAb, new Vector4(ambientProbe[2, 3], ambientProbe[2, 1], ambientProbe[2, 2], ambientProbe[2, 0] - ambientProbe[2, 6]));
                cloudsMaterial.SetVector(shBr, new Vector4(ambientProbe[0, 4], ambientProbe[0, 5], ambientProbe[0, 6] * 3, ambientProbe[0, 7]));
                cloudsMaterial.SetVector(shBg, new Vector4(ambientProbe[1, 4], ambientProbe[1, 5], ambientProbe[1, 6] * 3, ambientProbe[1, 7]));
                cloudsMaterial.SetVector(shBb, new Vector4(ambientProbe[2, 4], ambientProbe[2, 5], ambientProbe[2, 6] * 3, ambientProbe[2, 7]));
                cloudsMaterial.SetVector(shC, new Vector4(ambientProbe[0, 8], ambientProbe[1, 8], ambientProbe[2, 8], 1));
            }
        }

        private static Vector2 IntersectSphere(float sphereRadius, float cosChi,
                                          float radialDistance, float rcpRadialDistance)
        {
            // r_o = float2(0, r)
            // r_d = float2(sinChi, cosChi)
            // p_s = r_o + t * r_d
            //
            // R^2 = dot(r_o + t * r_d, r_o + t * r_d)
            // R^2 = ((r_o + t * r_d).x)^2 + ((r_o + t * r_d).y)^2
            // R^2 = t^2 + 2 * dot(r_o, r_d) + dot(r_o, r_o)
            //
            // t^2 + 2 * dot(r_o, r_d) + dot(r_o, r_o) - R^2 = 0
            //
            // Solve: t^2 + (2 * b) * t + c = 0, where
            // b = r * cosChi,
            // c = r^2 - R^2.
            //
            // t = (-2 * b + sqrt((2 * b)^2 - 4 * c)) / 2
            // t = -b + sqrt(b^2 - c)
            // t = -b + sqrt((r * cosChi)^2 - (r^2 - R^2))
            // t = -b + r * sqrt((cosChi)^2 - 1 + (R/r)^2)
            // t = -b + r * sqrt(d)
            // t = r * (-cosChi + sqrt(d))
            //
            // Why do we do this? Because it is more numerically robust.

            float d = square(sphereRadius * rcpRadialDistance) - saturate(1 - cosChi * cosChi);

            // Return the value of 'd' for debugging purposes.
            return (d < 0.0f) ? new Vector2(-1.0f, -1.0f) : (radialDistance * new Vector2(-cosChi - sqrt(d),
                                                          -cosChi + sqrt(d)));
        }

        private static float GetCloudNearPlane(Vector3 originPS, float lowerBoundPS, float higherBoundPS)
        {
            float radialDistance = length(originPS);
            float rcpRadialDistance = rcp(radialDistance);
            float cosChi = 1.0f;
            Vector2 tInner = IntersectSphere(lowerBoundPS, cosChi, radialDistance, rcpRadialDistance);
            Vector2 tOuter = IntersectSphere(higherBoundPS, -cosChi, radialDistance, rcpRadialDistance);

            if (tInner.x < 0.0f && tInner.y >= 0.0f) // Below the lower bound
                return tInner.y;
            else // Inside or above the cloud volume
                return max(tOuter.x, 0.0f);
        }

        public VolumetricCloudsPass(Material material, float resolution)
        {
            cloudsMaterial = material;
            resolutionScale = resolution;
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
        private readonly RTHandle[] cloudsRTHandles = new RTHandle[2]; // avoid GC allocation
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor historyDesc = CreateBaseCloudsDescriptor(renderingData.cameraData.cameraTargetDescriptor);
            ReAllocateTexture(ref historyHandle, historyDesc, FilterMode.Point, _VolumetricCloudsHistoryTexture); // lighting.rgb only

            RenderTextureDescriptor accumulateDesc = CreateCloudsDescriptor(renderingData.cameraData.cameraTargetDescriptor, CloudsLightingFormat); // lighting.rgb + transmittance.a
            ReAllocateTexture(ref accumulateHandle, accumulateDesc, FilterMode.Point, _VolumetricCloudsAccumulationTexture);

            RenderTextureDescriptor cloudsColorDesc = CreateScaledCloudsDescriptor(renderingData.cameraData.cameraTargetDescriptor, CloudsLightingFormat);
            ReAllocateTexture(ref cloudsColorHandle, cloudsColorDesc, FilterMode.Bilinear, _VolumetricCloudsLightingTexture);
            cloudsMaterial.SetTexture(volumetricCloudsLightingTexture, cloudsColorHandle);

            RenderTextureDescriptor cloudsDepthDesc = CreateScaledCloudsDescriptor(renderingData.cameraData.cameraTargetDescriptor, CloudsDepthFormat); // average z-depth
            ReAllocateTexture(ref cloudsDepthHandle, cloudsDepthDesc, FilterMode.Point, _VolumetricCloudsDepthTexture);
            ReAllocateTexture(ref cameraTempDepthHandle, cloudsDepthDesc, FilterMode.Point, _CameraTempDepthTexture);

            cmd.SetGlobalTexture(volumetricCloudsColorTexture, cloudsColorHandle);
            cmd.SetGlobalTexture(volumetricCloudsLightingTexture, cloudsColorHandle); // Same as "_VolumetricCloudsColorTexture"
            cmd.SetGlobalTexture(volumetricCloudsDepthTexture, cloudsDepthHandle);

            cloudsMaterial.SetTexture(volumetricCloudsHistoryTexture, historyHandle);
            cloudsMaterial.SetTexture(volumetricCloudsDepthTexture, cloudsDepthHandle);

            ConfigureInput(ScriptableRenderPassInput.Depth);

            if (outputDepth)
            {
                cloudsRTHandles[0] = cloudsColorHandle;
                cloudsRTHandles[1] = cloudsDepthHandle;

                // RT-1: clouds lighting
                // RT-2: clouds depth
                ConfigureTarget(cloudsRTHandles, cloudsColorHandle);
            }
            else
            {
                ConfigureTarget(cloudsColorHandle, cloudsColorHandle);
            }
        }

    #if UNITY_6000_0_OR_NEWER
        [Obsolete]
    #endif
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            LightData lightData = renderingData.lightData;
            Light mainLight = GetMainLight(lightData);

            UpdateClouds(mainLight, renderingData.cameraData.camera);

            cloudsMaterial.SetTexture(cameraDepthTexture, null); // Use global texture

            RTHandle cameraColorHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                //RenderTargetIdentifier[] cloudsHandles = new RenderTargetIdentifier[2];
                //cloudsRTHandles[0] = cloudsColorHandle;
                //cloudsRTHandles[1] = cloudsDepthHandle;
                //cmd.SetRenderTarget(cloudsHandles, cloudsColorHandle);
                // Clouds Rendering
                Blitter.BlitTexture(cmd, cameraColorHandle, m_ScaleBias, cloudsMaterial, pass: 0);

                // Clouds Upscale & Combine
                Blitter.BlitCameraTexture(cmd, cameraColorHandle, cameraColorHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, cloudsMaterial, pass: hasAtmosphericScattering ? 7 : 1);

                if (outputToSceneDepth)
                {
                    // Using reflection to access the "_CameraDepthTexture" in compatibility mode
                    var renderer = renderingData.cameraData.renderer as UniversalRenderer;
                    var cameraDepthHandle = depthTextureFieldInfo.GetValue(renderer) as RTHandle;

                    Blitter.BlitCameraTexture(cmd, cameraDepthHandle, cameraTempDepthHandle);

                    // Handle both R32 and D32 texture format
                    cmd.SetRenderTarget(cameraDepthHandle, cameraDepthHandle);
                    Blitter.BlitTexture(cmd, cameraTempDepthHandle, m_ScaleBias, cloudsMaterial, pass: 6);
                }

                if (denoiseClouds)
                {
                    // Prepare Temporal Reprojection (copy source buffer: colorHandle.rgb + cloudsColorHandle.a)
                    Blitter.BlitCameraTexture(cmd, cameraColorHandle, accumulateHandle, cloudsMaterial, pass: 2);

                    // Temporal Reprojection
                    Blitter.BlitCameraTexture(cmd, accumulateHandle, cameraColorHandle, cloudsMaterial, pass: 3);

                    // Update history texture for temporal reprojection
                    bool canCopy = cameraColorHandle.rt.format == historyHandle.rt.format && cameraColorHandle.rt.antiAliasing == 1 && fastCopy;
                    if (canCopy && renderMode == CloudsRenderMode.CopyTexture) { cmd.CopyTexture(cameraColorHandle, historyHandle); }
                    else { Blitter.BlitCameraTexture(cmd, cameraColorHandle, historyHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, cloudsMaterial, pass: 2); }
                }
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

        // This class stores the data needed by the pass, passed as parameter to the delegate function that executes the pass
        private class PassData
        {
            internal Material cloudsMaterial;
            internal Camera camera;

            internal CloudsUpscaleMode upscaleMode;

            internal float resolutionScale;

            internal bool canCopy;
            internal bool denoiseClouds;
            internal bool dynamicAmbientProbe;
            internal bool outputDepth;
            internal bool outputToSceneDepth;
            internal bool hasAtmosphericScattering;

            internal TextureHandle cameraColorHandle;
            internal TextureHandle activeDepthHandle;
            internal TextureHandle cameraDepthHandle;
            internal TextureHandle cloudsColorHandle;
            internal TextureHandle cloudsDepthHandle;
            internal TextureHandle accumulateHandle;
            internal TextureHandle historyHandle;

            internal TextureHandle cameraTempDepthHandle;
        }

        private class RasterPassData
        {
            internal Material cloudsMaterial;

            internal TextureHandle cameraColorHandle;
            internal TextureHandle cameraDepthHandle;
        }

        // This static method is used to execute the pass and passed as the RenderFunc delegate to the RenderGraph render pass
        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            // Clouds Upscale & Combine
            Blitter.BlitCameraTexture(cmd, data.cloudsColorHandle, data.cameraColorHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, data.cloudsMaterial, pass: data.hasAtmosphericScattering ? 7 : 1);

            if (data.outputToSceneDepth)
            {
                Blitter.BlitCameraTexture(cmd, data.cameraDepthHandle, data.cameraTempDepthHandle);

                // Handle both R32 and D32 texture format
                context.cmd.SetRenderTarget(data.cameraDepthHandle, data.cameraDepthHandle);
                Blitter.BlitTexture(cmd, data.cameraTempDepthHandle, m_ScaleBias, data.cloudsMaterial, pass: 6);
            }

            if (data.denoiseClouds)
            {
                // Prepare Temporal Reprojection (copy source buffer: colorHandle.rgb + cloudsHandle.a)
                Blitter.BlitCameraTexture(cmd, data.cameraColorHandle, data.accumulateHandle, data.cloudsMaterial, pass: 2);

                // Temporal Reprojection
                Blitter.BlitCameraTexture(cmd, data.accumulateHandle, data.cameraColorHandle, data.cloudsMaterial, pass: 3);

                // Update history texture for temporal reprojection
                if (data.canCopy)
                    cmd.CopyTexture(data.cameraColorHandle, data.historyHandle);
                else
                    Blitter.BlitCameraTexture(cmd, data.cameraColorHandle, data.historyHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, data.cloudsMaterial, pass: 2);

                data.cloudsMaterial.SetTexture(volumetricCloudsHistoryTexture, data.historyHandle);
            }

            context.cmd.SetRenderTarget(data.cameraColorHandle, data.activeDepthHandle);
        }

        static void ExecuteRasterPass(RasterPassData data, RasterGraphContext rgContext)
        {
            RasterCommandBuffer cmd = rgContext.cmd;

            data.cloudsMaterial.SetTexture(cameraDepthTexture, data.cameraDepthHandle);
            Blitter.BlitTexture(cmd, data.cameraColorHandle, m_ScaleBias, data.cloudsMaterial, pass: 0);
        }

        // This is where the renderGraph handle can be accessed.
        // Each ScriptableRenderPass can use the RenderGraph handle to add multiple render passes to the render graph
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // UniversalResourceData contains all the texture handles used by the renderer, including the active color and depth textures
            // The active color and depth textures are the main color and depth buffers that the camera renders into
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            // add a raster render pass to the render graph, specifying the name and the data type that will be passed to the ExecuteRasterPass function
            using (var builder = renderGraph.AddRasterRenderPass<RasterPassData>(rasterPassProfilerTag, out var rasterPassData))
            {
                Light mainLight = GetMainLight(lightData);
                UpdateClouds(mainLight, cameraData.camera);

                // Get the active color texture through the frame data, and set it as the source texture for the blit
                rasterPassData.cameraColorHandle = resourceData.activeColorTexture;
                rasterPassData.cameraDepthHandle = resourceData.cameraDepthTexture;

                RenderTextureDescriptor cloudsColorDesc = CreateScaledCloudsDescriptor(cameraData.cameraTargetDescriptor, CloudsLightingFormat); // lighting.rgb + transmittance.a
                ReAllocateTexture(ref cloudsColorHandle, cloudsColorDesc, FilterMode.Bilinear, _VolumetricCloudsLightingTexture);
                cloudsMaterial.SetTexture(volumetricCloudsLightingTexture, cloudsColorHandle);
                TextureHandle cloudsTextureHandle = renderGraph.ImportTexture(cloudsColorHandle);

                //builder.SetGlobalTextureAfterPass(cloudsTextureHandle, volumetricCloudsColorTexture);
                //builder.SetGlobalTextureAfterPass(cloudsTextureHandle, volumetricCloudsLightingTexture); // Same as "_VolumetricCloudsColorTexture"

                if (outputDepth)
                {
                    RenderTextureDescriptor cloudsDepthDesc = CreateScaledCloudsDescriptor(cameraData.cameraTargetDescriptor, CloudsDepthFormat); // average z-depth
                    ReAllocateTexture(ref cloudsDepthHandle, cloudsDepthDesc, FilterMode.Point, _VolumetricCloudsDepthTexture);
                    cloudsMaterial.SetTexture(volumetricCloudsDepthTexture, cloudsDepthHandle);
                    TextureHandle cloudsDepthTextureHandle = renderGraph.ImportTexture(cloudsDepthHandle);
                    //builder.UseTexture(cloudsDepthTextureHandle, AccessFlags.Write);
                    //builder.SetGlobalTextureAfterPass(cloudsDepthTextureHandle, volumetricCloudsDepthTexture);

                    builder.SetRenderAttachment(cloudsDepthTextureHandle, 1);
                }

                // Fill up the passData with the data needed by the pass
                rasterPassData.cloudsMaterial = cloudsMaterial;

                ConfigureInput(ScriptableRenderPassInput.Depth);

                builder.UseTexture(rasterPassData.cameraColorHandle, AccessFlags.ReadWrite);
                builder.UseTexture(rasterPassData.cameraDepthHandle, AccessFlags.Read);

                builder.SetRenderAttachment(cloudsTextureHandle, 0);

                // Sets the render function.
                builder.SetRenderFunc((RasterPassData rasterPassData, RasterGraphContext rgContext) => ExecuteRasterPass(rasterPassData, rgContext));
            }

            // add an unsafe render pass to the render graph, specifying the name and the data type that will be passed to the ExecutePass function
            using (var builder = renderGraph.AddUnsafePass<PassData>(profilerTag, out var passData))
            {
                // Get the active color texture through the frame data, and set it as the source texture for the blit
                passData.cameraColorHandle = resourceData.activeColorTexture;
                passData.activeDepthHandle = resourceData.activeDepthTexture;
                passData.cameraDepthHandle = resourceData.cameraDepthTexture;

                RenderTextureDescriptor desc = CreateCloudsDescriptor(cameraData.cameraTargetDescriptor, CloudsLightingFormat); // lighting.rgb + transmittance.a

                TextureHandle accumulateHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: _VolumetricCloudsAccumulationTexture, false, FilterMode.Point, TextureWrapMode.Clamp);
                TextureHandle historyHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: _VolumetricCloudsHistoryTexture, false, FilterMode.Point, TextureWrapMode.Clamp);

                // Full resolution camera texture descriptor
                RenderTextureDescriptor tempDepthDesc = desc;
                TextureHandle cloudsTextureHandle = renderGraph.ImportTexture(cloudsColorHandle);

                builder.SetGlobalTextureAfterPass(cloudsTextureHandle, volumetricCloudsColorTexture);
                builder.SetGlobalTextureAfterPass(cloudsTextureHandle, volumetricCloudsLightingTexture); // Same as "_VolumetricCloudsColorTexture"

                if (outputDepth)
                {
                    TextureHandle cloudsDepthTextureHandle = renderGraph.ImportTexture(cloudsDepthHandle);
                    passData.cloudsDepthHandle = cloudsDepthTextureHandle;
                    builder.UseTexture(passData.cloudsDepthHandle, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(cloudsDepthTextureHandle, volumetricCloudsDepthTexture);
                }

                if (outputToSceneDepth)
                {
                    tempDepthDesc.colorFormat = CloudsDepthFormat; // average z-depth

                    TextureHandle tempDepthHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, tempDepthDesc, name: _CameraTempDepthTexture, false, FilterMode.Point, TextureWrapMode.Clamp);
                    passData.cameraTempDepthHandle = tempDepthHandle;
                    builder.UseTexture(passData.cameraTempDepthHandle, AccessFlags.Write);
                }

                // Fill up the passData with the data needed by the pass
                passData.cloudsMaterial = cloudsMaterial;
                passData.camera = cameraData.camera;
                passData.upscaleMode = upscaleMode;
                passData.resolutionScale = resolutionScale;
                passData.canCopy = cameraData.cameraTargetDescriptor.colorFormat == CloudsLightingFormat && cameraData.cameraTargetDescriptor.msaaSamples == 1 && fastCopy;
                passData.denoiseClouds = denoiseClouds;
                passData.dynamicAmbientProbe = dynamicAmbientProbe;
                passData.outputDepth = outputDepth;
                passData.outputToSceneDepth = outputToSceneDepth && (cameraData.camera.cameraType == CameraType.Game || cameraData.camera.cameraType == CameraType.SceneView);
                passData.hasAtmosphericScattering = hasAtmosphericScattering;

                passData.cloudsColorHandle = cloudsTextureHandle;
                passData.accumulateHandle = accumulateHandle;
                passData.historyHandle = historyHandle;

                ConfigureInput(ScriptableRenderPassInput.Depth);

                // UnsafePasses don't setup the outputs using UseTextureFragment/UseTextureFragmentDepth, you should specify your writes with UseTexture instead
                builder.UseTexture(passData.cameraColorHandle, AccessFlags.ReadWrite);
                builder.UseTexture(passData.activeDepthHandle, AccessFlags.None);
                builder.UseTexture(passData.cameraDepthHandle, AccessFlags.Read);
                builder.UseTexture(passData.cloudsColorHandle, AccessFlags.Write);
                builder.UseTexture(passData.accumulateHandle, AccessFlags.Write);
                builder.UseTexture(passData.historyHandle, AccessFlags.ReadWrite);

                // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }
        #endregion
    #endif

        #region Shared
        public void Dispose()
        {
            cloudsColorHandle?.Release();
            cloudsDepthHandle?.Release();
            historyHandle?.Release();
            accumulateHandle?.Release();
            cameraTempDepthHandle?.Release();
        }
        #endregion
    }
}
