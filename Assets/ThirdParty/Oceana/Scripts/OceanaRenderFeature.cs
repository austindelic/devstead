using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Oceana {
    public class OceanaRenderFeature : ScriptableRendererFeature {
        private const string PlanarReflectionCameraName = "Devstead Planar Reflection Camera";

        [SerializeField]
        private OceanaSettings m_Settings;

        private OceanaScrollPass m_ScrollPass;
        private OceanaSurfacePass m_SurfacePass;
        private OceanaUnderwaterPass m_UnderwaterPass;

        public override void Create() {
            m_ScrollPass = new OceanaScrollPass(RenderPassEvent.BeforeRenderingOpaques);
            m_ScrollPass.FetchSettings(m_Settings);

            m_SurfacePass = new OceanaSurfacePass(RenderPassEvent.AfterRenderingTransparents);
            m_SurfacePass.FetchSettings(m_Settings);

            m_UnderwaterPass = new OceanaUnderwaterPass(RenderPassEvent.BeforeRenderingPostProcessing);
            m_UnderwaterPass.FetchSettings(m_Settings);
        }

        private void OnValidate() {
            m_ScrollPass?.FetchSettings(m_Settings);
            m_SurfacePass?.FetchSettings(m_Settings);
            m_UnderwaterPass?.FetchSettings(m_Settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            if (m_ScrollPass == null || m_SurfacePass == null || m_UnderwaterPass == null) return;
            if (m_Settings == null || IsPlanarReflectionCamera(renderingData.cameraData.camera)) return;

            renderer.EnqueuePass(m_ScrollPass);
            renderer.EnqueuePass(m_SurfacePass);
            renderer.EnqueuePass(m_UnderwaterPass);
        }

        private static bool IsPlanarReflectionCamera(Camera camera) {
            return camera != null && camera.name.StartsWith(PlanarReflectionCameraName, System.StringComparison.Ordinal);
        }

        protected override void Dispose(bool disposing) {
            m_ScrollPass?.Dispose();
            m_SurfacePass?.Dispose();
            m_UnderwaterPass?.Dispose();
        }
    }
}
