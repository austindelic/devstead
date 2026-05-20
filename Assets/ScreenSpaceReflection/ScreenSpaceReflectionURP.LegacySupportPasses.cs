#if !UNITY_6000_0_OR_NEWER
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;

public partial class ScreenSpaceReflectionURP
{
    public class BackFaceDepthPass : ScriptableRenderPass
    {
        const string profilerTag = "Render Backface Depth";
        private readonly Material ssrMaterial;
        public ScreenSpaceReflection ssrVolume;
        private RTHandle backFaceDepthHandle;

        private RenderStateBlock depthRenderStateBlock = new(RenderStateMask.Nothing);

        public BackFaceDepthPass(Material material)
        {
            ssrMaterial = material;
        }

        public void Dispose()
        {
            backFaceDepthHandle?.Release();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;

            RenderingUtils.ReAllocateIfNeeded(ref backFaceDepthHandle, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_CameraBackDepthTexture");
            cmd.SetGlobalTexture("_CameraBackDepthTexture", backFaceDepthHandle);
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            if (backFaceDepthHandle != null)
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(backFaceDepthHandle.name));
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            backFaceDepthHandle = null;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (ssrVolume.thicknessMode.value == ScreenSpaceReflection.ThicknessMode.ComputeBackface)
                ExecuteBackfaceDepth(context, ref renderingData);
            else
                DisableBackfaceKeyword(ssrMaterial);
        }

        private void ExecuteBackfaceDepth(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler(profilerTag)))
            {
                cmd.SetRenderTarget(
                    backFaceDepthHandle,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.DontCare,
                    backFaceDepthHandle,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store);
                cmd.ClearRenderTarget(clearDepth: true, clearColor: false, Color.clear);

                RendererListDesc rendererListDesc = new(new ShaderTagId("DepthOnly"), renderingData.cullResults, renderingData.cameraData.camera);
                depthRenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                depthRenderStateBlock.mask |= RenderStateMask.Depth;
                depthRenderStateBlock.rasterState = new RasterState(CullMode.Front);
                depthRenderStateBlock.mask |= RenderStateMask.Raster;
                rendererListDesc.stateBlock = depthRenderStateBlock;
                rendererListDesc.sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
                rendererListDesc.renderQueueRange = RenderQueueRange.opaque;
                RendererList rendererList = context.CreateRendererList(rendererListDesc);

                cmd.DrawRendererList(rendererList);

                ssrMaterial.EnableKeyword(BackfaceKeyword);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }

    public class ForwardGBufferPass : ScriptableRenderPass
    {
        const string profilerTag = "Render Forward GBuffer";

        // Depth Priming.
        private RenderStateBlock renderStateBlock = new(RenderStateMask.Nothing);

        public RTHandle gBuffer0;
        public RTHandle gBuffer1;
        public RTHandle gBuffer2;
        public RTHandle depthHandle;
        private RTHandle[] gBuffers;

        public void Dispose()
        {
            gBuffer0?.Release();
            gBuffer1?.Release();
            gBuffer2?.Release();
            depthHandle?.Release();
        }

        // From "URP-Package/Runtime/DeferredLights.cs".
        public GraphicsFormat GetGBufferFormat(int index)
        {
            if (index == 0) // sRGB albedo, materialFlags
                return QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            else if (index == 1) // sRGB specular, occlusion
                return GraphicsFormat.R8G8B8A8_UNorm;
            else if (index == 2) // normal normal normal packedSmoothness
                return GetNormalGBufferFormat();
            else
                return GraphicsFormat.None;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = CreateGBufferDescriptor(renderingData);

            // Albedo.rgb + MaterialFlags.a
            desc.graphicsFormat = GetGBufferFormat(0);
            RenderingUtils.ReAllocateIfNeeded(ref gBuffer0, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GBuffer0");
            cmd.SetGlobalTexture("_GBuffer0", gBuffer0);

            // Specular.rgb + Occlusion.a
            desc.graphicsFormat = GetGBufferFormat(1);
            RenderingUtils.ReAllocateIfNeeded(ref gBuffer1, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GBuffer1");
            cmd.SetGlobalTexture("_GBuffer1", gBuffer1);

            SetupNormalGBuffer(cmd, desc, ref renderingData);
            ConfigureDepthTarget(ref renderingData);
            ConfigureGBufferClear(ref renderingData);
            ConfigureDepthPriming(ref renderingData);
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(Shader.PropertyToID(gBuffer0.name));
            cmd.ReleaseTemporaryRT(Shader.PropertyToID(gBuffer1.name));
            cmd.ReleaseTemporaryRT(Shader.PropertyToID(gBuffer2.name));
            if (depthHandle != null)
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(depthHandle.name));
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            gBuffer0 = null;
            gBuffer1 = null;
            gBuffer2 = null;
            depthHandle = null;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler(profilerTag)))
            {
                RendererListDesc rendererListDesc = new(new ShaderTagId("UniversalGBuffer"), renderingData.cullResults, renderingData.cameraData.camera);
                rendererListDesc.stateBlock = renderStateBlock;
                rendererListDesc.sortingCriteria = sortingCriteria;
                rendererListDesc.renderQueueRange = RenderQueueRange.opaque;
                RendererList rendererList = context.CreateRendererList(rendererListDesc);

                cmd.DrawRendererList(rendererList);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        private static RenderTextureDescriptor CreateGBufferDescriptor(RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0; // Color and depth cannot be combined in RTHandles
            desc.stencilFormat = GraphicsFormat.None;
            desc.msaaSamples = 1; // Do not enable MSAA for GBuffers.
            return desc;
        }

        private static GraphicsFormat GetNormalGBufferFormat()
        {
            // NormalWS range is -1.0 to 1.0, so we need a signed render texture.
#if UNITY_2023_2_OR_NEWER
            if (SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SNorm, GraphicsFormatUsage.Render))
#else
            if (SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SNorm, FormatUsage.Render))
#endif
                return GraphicsFormat.R8G8B8A8_SNorm;
            else
                return GraphicsFormat.R16G16B16A16_SFloat;
        }

        private void SetupNormalGBuffer(CommandBuffer cmd, RenderTextureDescriptor desc, ref RenderingData renderingData)
        {
            // If "_CameraNormalsTexture" exists (lacking smoothness info), set the target to it instead of creating a new RT.
            if (normalsTextureFieldInfo.GetValue(renderingData.cameraData.renderer) is not RTHandle normalsTextureHandle || renderingData.cameraData.cameraType == CameraType.SceneView)
            {
                // NormalWS.rgb + Smoothness.a
                desc.graphicsFormat = GetGBufferFormat(2);
                RenderingUtils.ReAllocateIfNeeded(ref gBuffer2, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GBuffer2");
                cmd.SetGlobalTexture("_GBuffer2", gBuffer2);
                gBuffers = new RTHandle[] { gBuffer0, gBuffer1, gBuffer2 };
            }
            else
            {
                cmd.SetGlobalTexture("_GBuffer2", normalsTextureHandle);
                gBuffers = new RTHandle[] { gBuffer0, gBuffer1, normalsTextureHandle };
            }
        }

        private void ConfigureDepthTarget(ref RenderingData renderingData)
        {
            if (renderingData.cameraData.renderer.cameraDepthTargetHandle.isMSAAEnabled)
            {
                RenderTextureDescriptor depthDesc = renderingData.cameraData.cameraTargetDescriptor;
                depthDesc.msaaSamples = 1;
                RenderingUtils.ReAllocateIfNeeded(ref depthHandle, depthDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GBuffersDepthTexture");
                ConfigureTarget(gBuffers, depthHandle);
            }
            else
                ConfigureTarget(gBuffers, renderingData.cameraData.renderer.cameraDepthTargetHandle);
        }

        private void ConfigureGBufferClear(ref RenderingData renderingData)
        {
            // [OpenGL] Reusing the depth buffer seems to cause black glitching artifacts, so clear the existing depth.
            bool isOpenGL = IsOpenGL();
            if (isOpenGL || renderingData.cameraData.renderer.cameraDepthTargetHandle.isMSAAEnabled)
                ConfigureClear(ClearFlag.Color | ClearFlag.Depth, Color.black);
            else
                // We have to also clear previous color so that the "background" will remain empty (black) when moving the camera.
                ConfigureClear(ClearFlag.Color, Color.clear);
        }

        private void ConfigureDepthPriming(ref RenderingData renderingData)
        {
            // Reduce GBuffer overdraw using the depth from opaque pass. (excluding OpenGL platforms)
            if (!IsOpenGL() && (renderingData.cameraData.renderType == CameraRenderType.Base || renderingData.cameraData.clearDepth) && !renderingData.cameraData.renderer.cameraDepthTargetHandle.isMSAAEnabled)
            {
                renderStateBlock.depthState = new DepthState(false, CompareFunction.Equal);
                renderStateBlock.mask |= RenderStateMask.Depth;
            }
            else if (renderStateBlock.depthState.compareFunction == CompareFunction.Equal)
            {
                renderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                renderStateBlock.mask |= RenderStateMask.Depth;
            }
        }

        private static bool IsOpenGL()
        {
            return SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore;
        }
    }
}
#endif
