#if !UNITY_6000_0_OR_NEWER
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public partial class ScreenSpaceReflectionURP
{
    public class ScreenSpaceReflectionPass : ScriptableRenderPass
    {
        private readonly Material ssrMaterial;
        public Resolution resolution;
        public MipmapsMode mipmapsMode;
        public bool isMotionValid; // URP SceneView doesn't update motion vectors unless in play mode.
        private RTHandle sourceHandle;
        private RTHandle reflectHandle;
        private RTHandle historyHandle;

        public ScreenSpaceReflection ssrVolume;

        public ScreenSpaceReflectionPass(Resolution resolution, MipmapsMode mipmapsMode, Material material)
        {
            this.resolution = resolution;
            this.mipmapsMode = mipmapsMode;
            ssrMaterial = material;
        }

        public void Dispose()
        {
            sourceHandle?.Release();
            reflectHandle?.Release();
            historyHandle?.Release();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.useMipMap = false;

            if (ssrVolume.algorithm == ScreenSpaceReflection.Algorithm.PBRAccumulation)
                SetupPBRAccumulationTextures(cmd, desc);
            else
                SetupApproximationTextures(desc);

            ConfigureTarget(sourceHandle, sourceHandle);
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            if (sourceHandle != null)
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(sourceHandle.name));
            if (reflectHandle != null)
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(reflectHandle.name));
            if (historyHandle != null)
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(historyHandle.name));
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            sourceHandle = null;
            reflectHandle = null;
            historyHandle = null;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler("Screen Space Reflection")))
            {
                ApplyMaterialSettings(ssrMaterial, ssrVolume, resolution);

                // Blit() may not handle XR rendering correctly.
                if (ssrVolume.algorithm == ScreenSpaceReflection.Algorithm.PBRAccumulation)
                    ExecutePBRAccumulation(cmd, ref renderingData);
                else
                    ExecuteApproximation(cmd, ref renderingData);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        private void SetupPBRAccumulationTextures(CommandBuffer cmd, RenderTextureDescriptor desc)
        {
            RenderTextureDescriptor descHit = desc;
            descHit.width = (int)resolution * (int)(desc.width * 0.25f);
            descHit.height = (int)resolution * (int)(desc.height * 0.25f);
            descHit.colorFormat = RenderTextureFormat.ARGBHalf; // Store "hitUV.xy" + "fresnel.z"
            RenderingUtils.ReAllocateIfNeeded(ref sourceHandle, descHit, FilterMode.Point, TextureWrapMode.Clamp, name: "_ScreenSpaceReflectionHitTexture");
            cmd.SetGlobalTexture("_ScreenSpaceReflectionHitTexture", sourceHandle);

            RenderingUtils.ReAllocateIfNeeded(ref historyHandle, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_ScreenSpaceReflectionHistoryTexture");
            cmd.SetGlobalTexture("_ScreenSpaceReflectionHistoryTexture", historyHandle);
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Motion);

            RenderingUtils.ReAllocateIfNeeded(ref reflectHandle, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_ScreenSpaceReflectionColorTexture");
        }

        private void SetupApproximationTextures(RenderTextureDescriptor desc)
        {
            RenderingUtils.ReAllocateIfNeeded(ref sourceHandle, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_ScreenSpaceReflectionSourceTexture");

            desc.width = (int)resolution * (int)(desc.width * 0.25f);
            desc.height = (int)resolution * (int)(desc.height * 0.25f);
            desc.useMipMap = (mipmapsMode == MipmapsMode.Trilinear);
            FilterMode filterMode = (mipmapsMode == MipmapsMode.Trilinear) ? FilterMode.Trilinear : FilterMode.Point;

            RenderingUtils.ReAllocateIfNeeded(ref reflectHandle, desc, filterMode, TextureWrapMode.Clamp, name: "_ScreenSpaceReflectionColorTexture");
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        private void ExecutePBRAccumulation(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ssrMaterial.SetFloat(ShaderIDs.AccumFactor, ssrVolume.accumFactor.value);

            // Screen Space Hit
            Blitter.BlitCameraTexture(cmd, sourceHandle, sourceHandle, ssrMaterial, pass: 2);
            // Resolve Color
            Blitter.BlitCameraTexture(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle, reflectHandle, ssrMaterial, pass: 3);
            // Blit to Screen (required by denoiser)
            Blitter.BlitCameraTexture(cmd, reflectHandle, renderingData.cameraData.renderer.cameraColorTargetHandle);
            // Temporal Denoise (alpha blend)
            if (isMotionValid && ssrVolume.accumFactor.value != 0.0f)
            {
                Blitter.BlitCameraTexture(cmd, reflectHandle, renderingData.cameraData.renderer.cameraColorTargetHandle, ssrMaterial, pass: 4);

                // We need to Load & Store the history texture, or it will not be stored on some platforms.
                cmd.SetRenderTarget(
                    historyHandle,
                    RenderBufferLoadAction.Load,
                    RenderBufferStoreAction.Store,
                    historyHandle,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.DontCare);
                // Update History
                Blitter.BlitCameraTexture(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle, historyHandle);
            }
        }

        private void ExecuteApproximation(CommandBuffer cmd, ref RenderingData renderingData)
        {
            SetApproximationMipmapsKeyword(ssrMaterial, mipmapsMode);

            // Copy Scene Color
            Blitter.BlitCameraTexture(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle, sourceHandle);
            // Screen Space Reflection
            Blitter.BlitCameraTexture(cmd, sourceHandle, reflectHandle, ssrMaterial, pass: 0);
            // Combine Color
            Blitter.BlitCameraTexture(cmd, reflectHandle, renderingData.cameraData.renderer.cameraColorTargetHandle, ssrMaterial, pass: 1);
        }
    }
}
#endif
