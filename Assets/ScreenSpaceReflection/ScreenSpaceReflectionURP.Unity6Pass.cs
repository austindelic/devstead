#if UNITY_6000_0_OR_NEWER
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public partial class ScreenSpaceReflectionURP
{
    public class Unity6ScreenSpaceReflectionPass : ScriptableRenderPass
    {
        private readonly Material ssrMaterial;
        public Resolution resolution;
        public MipmapsMode mipmapsMode;
        public ScreenSpaceReflection ssrVolume;

        private bool hasLoggedMissingGBuffer;

        private class GBufferGlobalsPassData
        {
        }

        public Unity6ScreenSpaceReflectionPass(Resolution resolution, MipmapsMode mipmapsMode, Material material)
        {
            this.resolution = resolution;
            this.mipmapsMode = mipmapsMode;
            ssrMaterial = material;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void Dispose()
        {
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (ssrVolume == null || !ssrVolume.IsActive())
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
                return;

            TextureHandle[] gBuffers = resourceData.gBuffer;
            if (!HasValidGBuffer(gBuffers))
            {
                LogMissingGBufferOnce();
                return;
            }
            hasLoggedMissingGBuffer = false;

            ApplyMaterialSettings(ssrMaterial, ssrVolume, resolution);
            SetGlobalGBufferTextures(renderGraph, gBuffers);
            DisableBackfaceKeyword(ssrMaterial);
            SetApproximationMipmapsKeyword(ssrMaterial, mipmapsMode);

            TextureDesc sourceDesc = CreateSourceTextureDesc(renderGraph, source);
            TextureHandle sourceCopy = renderGraph.CreateTexture(sourceDesc);
            renderGraph.AddBlitPass(source, sourceCopy, Vector2.one, Vector2.zero, passName: "Copy Color Screen Space Reflection");

            TextureDesc reflectionDesc = CreateReflectionTextureDesc(sourceDesc, cameraData);
            TextureHandle reflectionTexture = renderGraph.CreateTexture(reflectionDesc);
            AddMaterialBlitPass(renderGraph, sourceCopy, reflectionTexture, 0, "Screen Space Reflection");
            AddMaterialBlitPass(renderGraph, reflectionTexture, source, 1, "Composite Screen Space Reflection");
        }

        private static bool HasValidGBuffer(TextureHandle[] gBuffers)
        {
            return gBuffers != null
                && gBuffers.Length >= ShaderIDs.GBuffer.Length
                && gBuffers[0].IsValid()
                && gBuffers[1].IsValid()
                && gBuffers[2].IsValid();
        }

        private void LogMissingGBufferOnce()
        {
            if (hasLoggedMissingGBuffer)
                return;

            Debug.LogWarning("Screen Space Reflection URP: Unity 6 RenderGraph path requires URP Deferred rendering with valid GBuffer textures.");
            hasLoggedMissingGBuffer = true;
        }

        private TextureDesc CreateSourceTextureDesc(RenderGraph renderGraph, TextureHandle source)
        {
            TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
            sourceDesc.name = "_ScreenSpaceReflectionSourceTexture";
            sourceDesc.clearBuffer = false;
            sourceDesc.depthBufferBits = DepthBits.None;
            sourceDesc.msaaSamples = MSAASamples.None;
            sourceDesc.useMipMap = false;
            return sourceDesc;
        }

        private TextureDesc CreateReflectionTextureDesc(TextureDesc sourceDesc, UniversalCameraData cameraData)
        {
            TextureDesc reflectionDesc = sourceDesc;
            reflectionDesc.name = "_ScreenSpaceReflectionColorTexture";
            reflectionDesc.width = Mathf.Max(1, (int)resolution * (int)(cameraData.cameraTargetDescriptor.width * 0.25f));
            reflectionDesc.height = Mathf.Max(1, (int)resolution * (int)(cameraData.cameraTargetDescriptor.height * 0.25f));
            reflectionDesc.filterMode = (mipmapsMode == MipmapsMode.Trilinear) ? FilterMode.Trilinear : FilterMode.Point;
            reflectionDesc.useMipMap = (mipmapsMode == MipmapsMode.Trilinear);
            reflectionDesc.autoGenerateMips = (mipmapsMode == MipmapsMode.Trilinear);
            return reflectionDesc;
        }

        private void AddMaterialBlitPass(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, int pass, string passName)
        {
            var blit = new RenderGraphUtils.BlitMaterialParameters(source, destination, ssrMaterial, pass);
            using (var builder = renderGraph.AddBlitPass(blit, passName: passName, returnBuilder: true))
            {
                builder.UseAllGlobalTextures(true);
            }
        }

        private static void SetGlobalGBufferTextures(RenderGraph renderGraph, TextureHandle[] gBuffers)
        {
            using (var builder = renderGraph.AddRasterRenderPass<GBufferGlobalsPassData>("Set Screen Space Reflection GBuffer Globals", out _))
            {
                builder.AllowPassCulling(false);

                for (int i = 0; i < ShaderIDs.GBuffer.Length; i++)
                {
                    builder.SetGlobalTextureAfterPass(gBuffers[i], ShaderIDs.GBuffer[i]);
                }

                builder.SetRenderFunc(static (GBufferGlobalsPassData data, RasterGraphContext context) => { });
            }
        }
    }
}
#endif
