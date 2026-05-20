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
    public partial class VolumetricCloudsAmbientPass : ScriptableRenderPass
    {
        private const string profilerTag = "Volumetric Clouds Ambient Probe";
        private readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler(profilerTag);

        private readonly Material cloudsMaterial;
        private RTHandle probeColorHandle;

        // Modified from CoreUtils.lookAtList to swap the directions of up and down faces
        private static readonly Matrix4x4 frontView = new Matrix4x4(float4(-1, 0, 0, 0), float4(0, -1, 0, 0), float4(0, 0, -1, 0), float4(0, 0, 0, 1));
        private static readonly Matrix4x4 backView = new Matrix4x4(float4(1, 0, 0, 0), float4(0, -1, 0, 0), float4(0, 0, 1, 0), float4(0, 0, 0, 1));
        private static readonly Matrix4x4 upView = new Matrix4x4(float4(1, 0, 0, 0), float4(0, 0, -1, 0), float4(0, -1, 0, 0), float4(0, 0, 0, 1));
        private static readonly Matrix4x4 downView = new Matrix4x4(float4(1, 0, 0, 0), float4(0, 0, 1, 0), float4(0, 1, 0, 0), float4(0, 0, 0, 1));
        private static readonly Matrix4x4 rightView = new Matrix4x4(float4(0, 0, -1, 0), float4(0, -1, 0, 0), float4(1, 0, 0, 0), float4(0, 0, 0, 1));
        private static readonly Matrix4x4 leftView = new Matrix4x4(float4(0, 0, 1, 0), float4(0, -1, 0, 0), float4(-1, 0, 0, 0), float4(0, 0, 0, 1));

        // Cubemap Order: right, left, up, down, back, front. (+X, -X, +Y, -Y, +Z, -Z)
        private static readonly Matrix4x4[] skyViews = { rightView, leftView, upView, downView, backView, frontView };

    #if UNITY_6000_0_OR_NEWER
        private readonly RendererListHandle[] rendererListHandles = new RendererListHandle[6];
    #endif

        private readonly Matrix4x4[] skyViewMatrices = new Matrix4x4[6];

        private static readonly Matrix4x4 skyProjectionMatrix = Matrix4x4.Perspective(90.0f, 1.0f, 0.1f, 10.0f);
        private static readonly Vector4 skyViewScreenParams = new Vector4(16.0f, 16.0f, 1.0f + rcp(16.0f), 1.0f + rcp(16.0f));
        private static readonly Vector4 skyViewScreenSize = new Vector4(16.0f, 16.0f, rcp(16.0f), rcp(16.0f));

        public VolumetricCloudsAmbientPass(Material material)
        {
            cloudsMaterial = material;
        }

    #if !UNITY_6000_0_OR_NEWER
        #region Non Render Graph Pass
    #if UNITY_6000_0_OR_NEWER
        [Obsolete]
    #endif
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = CreateAmbientProbeDescriptor(renderingData.cameraData.cameraTargetDescriptor, clearDepthStencilFormat: true);
            ReAllocateAmbientProbe(ref probeColorHandle, desc);
            cloudsMaterial.SetTexture(volumetricCloudsAmbientProbe, probeColorHandle);

            ConfigureTarget(probeColorHandle, probeColorHandle);
        }

    #if UNITY_6000_0_OR_NEWER
        [Obsolete]
    #endif
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // UpdateEnvironment() is another way to update ambient lighting but it's really slow.
            //DynamicGI.UpdateEnvironment();

            CommandBuffer cmd = CommandBufferPool.Get();

            Camera camera = renderingData.cameraData.camera;
            var desc = renderingData.cameraData.cameraTargetDescriptor;

            bool isStereoEnabled = camera.stereoEnabled;

            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                if (isStereoEnabled)
                    cmd.DisableShaderKeyword(STEREO_INSTANCING_ON);

                float2 cameraResolution = float2(desc.width, desc.height);
                Vector3 cameraPositionWS = camera.transform.position;
                Vector4 cameraScreenSize = new Vector4(cameraResolution.x, cameraResolution.y, rcp(cameraResolution.x), rcp(cameraResolution.y));
                Vector4 cameraScreenParams = new Vector4(cameraResolution.x, cameraResolution.y, 1.0f + cameraScreenSize.z, 1.0f + cameraScreenSize.w);

                Matrix4x4 skyMatrixP = GL.GetGPUProjectionMatrix(skyProjectionMatrix, true);

                cmd.SetGlobalVector(worldSpaceCameraPos, Vector3.zero);
                cmd.SetGlobalFloat(disableSunDisk, 1.0f);

                cmd.SetGlobalVector(scaledScreenParams, skyViewScreenParams);
                cmd.SetGlobalVector(screenSize, skyViewScreenSize);

                for (int i = 0; i < 6; i++)
                {
                    CoreUtils.SetRenderTarget(cmd, probeColorHandle, ClearFlag.None, 0, (CubemapFace)i);

                    //var lookAt = Matrix4x4.LookAt(Vector3.zero, CoreUtils.lookAtList[i], CoreUtils.upVectorList[i]);
                    //Matrix4x4 viewMatrix = lookAt * Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f)); // Need to scale -1.0 on Z to match what is being done in the camera.wolrdToCameraMatrix API. ...

                    Matrix4x4 viewMatrix = skyViews[i];
                    viewMatrix *= Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f));
                    skyViewMatrices[i] = viewMatrix;

                    Matrix4x4 skyMatrixVP = skyMatrixP * skyViewMatrices[i];

                    // Camera matrices for skybox rendering
                    cmd.SetViewMatrix(skyViewMatrices[i]);
                    //cmd.SetGlobalMatrix(unity_MatrixVP, skyMatrixVP);
                    cmd.SetGlobalMatrix(unity_MatrixInvVP, skyMatrixVP.inverse);

                    // Can we exclude the sun disk in ambient probe?
                    RendererList rendererList = context.CreateSkyboxRendererList(camera, skyProjectionMatrix, skyViewMatrices[i]);
                    cmd.DrawRendererList(rendererList);
                }

                cmd.SetGlobalVector(worldSpaceCameraPos, cameraPositionWS);
                cmd.SetGlobalFloat(disableSunDisk, 0.0f);

                Matrix4x4 matrixVP = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;

                // Camera matrices for objects rendering
                cmd.SetViewMatrix(camera.worldToCameraMatrix);
                //cmd.SetGlobalMatrix(unity_MatrixVP, matrixVP);
                cmd.SetGlobalMatrix(unity_MatrixInvVP, matrixVP.inverse);
                cmd.SetGlobalVector(scaledScreenParams, cameraScreenParams);
                cmd.SetGlobalVector(screenSize, cameraScreenSize);

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
        private class PassData
        {
            internal Material cloudsMaterial;

            internal TextureHandle probeColorHandle;

            internal Vector3 cameraPositionWS;
            internal Vector4 cameraScreenParams;
            internal Vector4 cameraScreenSize;
            internal Matrix4x4 worldToCameraMatrix;
            internal Matrix4x4 projectionMatrix;

            internal RendererListHandle[] rendererListHandles;
            internal Matrix4x4[] skyViewMatrices;
            internal Matrix4x4 skyProjectionMatrix;

            internal bool isStereoEnabled;
        }

        // This static method is used to execute the pass and passed as the RenderFunc delegate to the RenderGraph render pass
        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            if (data.isStereoEnabled)
                cmd.DisableShaderKeyword(STEREO_INSTANCING_ON);

            context.cmd.SetGlobalVector(worldSpaceCameraPos, Vector3.zero);
            context.cmd.SetGlobalFloat(disableSunDisk, 1.0f);

            context.cmd.SetGlobalVector(scaledScreenParams, skyViewScreenParams);
            context.cmd.SetGlobalVector(screenSize, skyViewScreenSize);

            Matrix4x4 skyMatrixP = GL.GetGPUProjectionMatrix(data.skyProjectionMatrix, true);

            for (int i = 0; i < 6; i++)
            {
                CoreUtils.SetRenderTarget(cmd, data.probeColorHandle, ClearFlag.None, 0, (CubemapFace)i);

                Matrix4x4 skyMatrixVP = skyMatrixP * data.skyViewMatrices[i];

                // Camera matrices for skybox rendering
                cmd.SetViewMatrix(data.skyViewMatrices[i]);
                //cmd.SetProjectionMatrix(skyMatrixP);
                //context.cmd.SetGlobalMatrix(unity_MatrixVP, skyMatrixVP);
                context.cmd.SetGlobalMatrix(unity_MatrixInvVP, skyMatrixVP.inverse);

                context.cmd.DrawRendererList(data.rendererListHandles[i]);
            }

            data.cloudsMaterial.SetTexture(volumetricCloudsAmbientProbe, data.probeColorHandle);

            context.cmd.SetGlobalVector(worldSpaceCameraPos, data.cameraPositionWS);
            context.cmd.SetGlobalFloat(disableSunDisk, 0.0f);

            Matrix4x4 matrixVP = GL.GetGPUProjectionMatrix(data.projectionMatrix, true) * data.worldToCameraMatrix;

            // Camera matrices for objects rendering
            cmd.SetViewMatrix(data.worldToCameraMatrix);
            //cmd.SetProjectionMatrix(data.projectionMatrix);
            //context.cmd.SetGlobalMatrix(unity_MatrixVP, matrixVP);
            context.cmd.SetGlobalMatrix(unity_MatrixInvVP, matrixVP.inverse);
            context.cmd.SetGlobalVector(scaledScreenParams, data.cameraScreenParams);
            context.cmd.SetGlobalVector(screenSize, data.cameraScreenSize);

            if (data.isStereoEnabled)
                cmd.EnableShaderKeyword(STEREO_INSTANCING_ON);
        }

        // This is where the renderGraph handle can be accessed.
        // Each ScriptableRenderPass can use the RenderGraph handle to add multiple render passes to the render graph
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // add an unsafe render pass to the render graph, specifying the name and the data type that will be passed to the ExecutePass function
            using (var builder = renderGraph.AddUnsafePass<PassData>(profilerTag, out var passData))
            {
                // UniversalResourceData contains all the texture handles used by the renderer, including the active color and depth textures
                // The active color and depth textures are the main color and depth buffers that the camera renders into
                UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                float2 cameraResolution = float2(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height);
                RenderTextureDescriptor desc = CreateAmbientProbeDescriptor(cameraData.cameraTargetDescriptor, clearDepthStencilFormat: false);
                ReAllocateAmbientProbe(ref probeColorHandle, desc);
                TextureHandle probeColorTextureHandle = renderGraph.ImportTexture(probeColorHandle);
                passData.probeColorHandle = probeColorTextureHandle;
                passData.cloudsMaterial = cloudsMaterial;

                for (int i = 0; i < 6; i++)
                {
                    //var lookAt = Matrix4x4.LookAt(Vector3.zero, CoreUtils.lookAtList[i], CoreUtils.upVectorList[i]);
                    //Matrix4x4 viewMatrix = lookAt * Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f)); // Need to scale -1.0 on Z to match what is being done in the camera.wolrdToCameraMatrix API. ...

                    Matrix4x4 viewMatrix = skyViews[i];
                    viewMatrix *= Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f)); // Need to scale -1.0 on Z to match what is being done in the camera.wolrdToCameraMatrix API. ...
                    skyViewMatrices[i] = viewMatrix;
                    rendererListHandles[i] = renderGraph.CreateSkyboxRendererList(cameraData.camera, skyProjectionMatrix, viewMatrix);
                    builder.UseRendererList(rendererListHandles[i]);
                }

                // Fill up the passData with the data needed by the pass
                passData.rendererListHandles = rendererListHandles;
                passData.skyViewMatrices = skyViewMatrices;
                passData.skyProjectionMatrix = skyProjectionMatrix;
                passData.cloudsMaterial = cloudsMaterial;
                passData.cameraPositionWS = cameraData.camera.transform.position;
                passData.cameraScreenSize = new Vector4(cameraResolution.x, cameraResolution.y, rcp(cameraResolution.x), rcp(cameraResolution.y));
                passData.cameraScreenParams = new Vector4(cameraResolution.x, cameraResolution.y, 1.0f + passData.cameraScreenSize.z, 1.0f + passData.cameraScreenSize.w);
                passData.worldToCameraMatrix = cameraData.camera.worldToCameraMatrix;
                passData.projectionMatrix = cameraData.camera.projectionMatrix;
                passData.isStereoEnabled = cameraData.camera.stereoEnabled;

                // UnsafePasses don't setup the outputs using UseTextureFragment/UseTextureFragmentDepth, you should specify your writes with UseTexture instead
                builder.UseTexture(passData.probeColorHandle, AccessFlags.Write);

                // Global shader property changes are considered as global state modifications
                builder.AllowGlobalStateModification(true);

                // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }
        #endregion
    #endif

        #region Shared
        public void Dispose()
        {
            probeColorHandle?.Release();
        }
        #endregion
    }
}
