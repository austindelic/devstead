using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Oceana {
    public class OceanaScrollPass : ScriptableRenderPass {
        private const int k_KernelID = 0;
        private const int k_GroupX = 32;
        private const int k_GroupY = 32;

        private ComputeShader m_Shader;
        private Texture2DArray m_ScrollArray;
        private GraphicsFormat m_ScrollFormat;
        private Vector4[] m_ScrollArrayST;
        private int m_ScrollCount;
        private int m_Resolution;
        private int m_MipLevel;
        private RTHandle m_ScrollArrayHandle;
        private RTHandle m_ScrollMapHandle;

        private OceanaSettings m_Settings;

        public OceanaScrollPass(RenderPassEvent injection) {
            renderPassEvent = injection;
        }

        public void FetchSettings(OceanaSettings settings) {
            if (m_Settings != null) m_Settings.OnUpdate -= FetchProperties;

            if (settings != null) {
                m_Settings = settings;
                m_Settings.OnUpdate += FetchProperties;
                FetchProperties();
            }
            else {
                m_Settings = null;
                m_Shader = null;
                m_ScrollArray = null;
                m_ScrollArrayST = null;
                m_ScrollCount = 0;
                ReleaseScrollArrayHandle();
            }
        }

        private void FetchProperties() {
            ReleaseScrollArrayHandle();

            m_Shader = m_Settings.ScrollShader;
            m_ScrollArray = m_Settings.ScrollArray;
            m_ScrollFormat = m_Settings.ScrollFormat;
            m_ScrollArrayST = m_Settings.ScrollArrayST;

            m_Resolution = (int)m_Settings.ScrollResolution;
            m_ScrollCount = m_ScrollArray == null ? 0 : m_ScrollArray.depth;
            m_MipLevel = m_ScrollArray == null ? 0 : Mathf.Clamp(m_ScrollArray.width / m_Resolution, 1, m_ScrollArray.mipmapCount) - 1;

            if (m_ScrollArray != null) {
                m_ScrollArrayHandle = RTHandles.Alloc(m_ScrollArray);
            }
        }

        public class ScrollGlobalData : ContextItem {
            public TextureHandle scrollMap;

            public override void Reset() {
                scrollMap = TextureHandle.nullHandle;
            }
        }

        private class ScrollPassData {
            internal TextureHandle scrollFetch;
            internal TextureHandle scrollOutput;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            if (!CanRender()) {
                return;
            }

            using (IComputeRenderGraphBuilder builder = renderGraph.AddComputePass(passName, out ScrollPassData passData)) {
                passData.scrollFetch = renderGraph.ImportTexture(m_ScrollArrayHandle);

                RenderTextureDescriptor scrollMapDesc = new RenderTextureDescriptor(m_Resolution, m_Resolution) {
                    useMipMap = true,
                    autoGenerateMips = false,
                    graphicsFormat = m_ScrollFormat,
                    sRGB = false,
                    enableRandomWrite = true
                };
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_ScrollMapHandle, in scrollMapDesc, FilterMode.Bilinear, TextureWrapMode.Repeat);
                passData.scrollOutput = renderGraph.ImportTexture(m_ScrollMapHandle);

                ScrollGlobalData mapData = frameData.GetOrCreate<ScrollGlobalData>();
                mapData.scrollMap = passData.scrollOutput;

                builder.UseTexture(passData.scrollFetch, AccessFlags.Read);
                builder.UseTexture(passData.scrollOutput, AccessFlags.Write);

                builder.SetRenderFunc((ScrollPassData data, ComputeGraphContext context) => Execute(data, context));
            }
        }

        private void Execute(ScrollPassData data, ComputeGraphContext context) {
            context.cmd.SetComputeVectorArrayParam(m_Shader, OceanaShaderIds.ScrollArrayST, m_ScrollArrayST);
            context.cmd.SetComputeIntParam(m_Shader, OceanaShaderIds.ScrollCount, m_ScrollCount);
            context.cmd.SetComputeFloatParam(m_Shader, OceanaShaderIds.Time, Time.time);

            context.cmd.SetComputeTextureParam(m_Shader, k_KernelID, OceanaShaderIds.ScrollArray, data.scrollFetch);

            context.cmd.SetComputeIntParam(m_Shader, OceanaShaderIds.ScrollResolution, m_Resolution);
            context.cmd.SetComputeIntParam(m_Shader, OceanaShaderIds.MipLevel, m_MipLevel);

            context.cmd.SetComputeTextureParam(m_Shader, k_KernelID, OceanaShaderIds.ScrollMap, data.scrollOutput);
            context.cmd.DispatchCompute(m_Shader, k_KernelID, m_Resolution / k_GroupX, m_Resolution / k_GroupY, 1);

            m_ScrollMapHandle.rt.GenerateMips();
        }

        private bool CanRender() {
            return m_Shader != null
                && m_ScrollArray != null
                && m_ScrollArrayHandle != null
                && m_ScrollArrayST != null
                && m_ScrollArrayST.Length >= m_ScrollCount
                && m_ScrollCount > 0
                && m_Resolution > 0;
        }

        public void Dispose() {
            if (m_Settings != null) m_Settings.OnUpdate -= FetchProperties;
            ReleaseScrollArrayHandle();
            m_ScrollMapHandle?.Release();
            m_ScrollMapHandle = null;
        }

        private void ReleaseScrollArrayHandle() {
            m_ScrollArrayHandle?.Release();
            m_ScrollArrayHandle = null;
        }
    }
}
