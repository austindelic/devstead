using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Reflection;

[DisallowMultipleRendererFeature("Screen Space Reflection URP")]
[Tooltip("Add this Renderer Feature to support screen space reflection in URP Volume.")]
public partial class ScreenSpaceReflectionURP : ScriptableRendererFeature
{
    public enum Resolution
    {
        [InspectorName("100%")]
        [Tooltip("Do ray marching at 100% resolution.")]
        Full = 4,

        [InspectorName("75%")]
        [Tooltip("Do ray marching at 75% resolution.")]
        ThreeQuarters = 3,

        [InspectorName("50%")]
        [Tooltip("Do ray marching at 50% resolution.")]
        Half = 2,

        [InspectorName("25%")]
        [Tooltip("Do ray marching at 25% resolution.")]
        Quarter = 1
    }

    public enum MipmapsMode
    {
        [Tooltip("Disable rough reflections in approximation mode.")]
        None = 0,

        [Tooltip("Use trilinear mipmaps to compute rough reflections in approximation mode.")]
        Trilinear = 1
    }

    [Header("Setup")]
    [Tooltip("The post-processing material of screen space reflection.")]
    public Material material;
    [Tooltip("Enable this to execute SSR in Rendering Debugger view. This is disabled by default to avoid affecting the individual lighting previews.")]
    public bool renderingDebugger = false;
    [Header("Performance")]
    [Tooltip("The resolution of screen space ray marching.")]
    public Resolution resolution = Resolution.Full;
    [Header("Approximation")]
    [Tooltip("Controls how URP compute rough reflections in approximation mode.")]
    public MipmapsMode mipmapsMode = MipmapsMode.Trilinear;
    [Header("PBR Accumulation")]
    [Tooltip("Enable this to denoise SSR at anytime in SceneView. This is disabled by default because URP SceneView only updates motion vectors in play mode.")]
    public bool sceneView = false;

    private const string ssrShaderName = "Hidden/Lighting/ScreenSpaceReflection";
#if UNITY_6000_0_OR_NEWER
    private Unity6ScreenSpaceReflectionPass screenSpaceReflectionPass;
#else
    private ScreenSpaceReflectionPass screenSpaceReflectionPass;
    private BackFaceDepthPass backFaceDepthPass;
    private ForwardGBufferPass forwardGBufferPass;
#endif

    // Print message only once when using the rendering debugger.
    private bool isLogPrinted = false;

#if !UNITY_6000_0_OR_NEWER
    // Render GBuffers in Forward path.
    private readonly static FieldInfo renderingModeFieldInfo = typeof(UniversalRenderer).GetField("m_RenderingMode", BindingFlags.NonPublic | BindingFlags.Instance);
    private readonly static FieldInfo normalsTextureFieldInfo = typeof(UniversalRenderer).GetField("m_NormalsTexture", BindingFlags.NonPublic | BindingFlags.Instance);
#endif

    public Material SSRMaterial
    {
        get { return material; }
        set { material = (value.shader == Shader.Find(ssrShaderName)) ? value : material; }
    }
    public bool RenderingDebugger
    {
        get { return renderingDebugger; }
        set { renderingDebugger = value; }
    }

    public Resolution DownSampling
    {
        get { return resolution; }
        set { resolution = value; }
    }

    public MipmapsMode ColorMipmapsMode
    {
        get { return mipmapsMode; }
        set { mipmapsMode = value; }
    }

    public override void Create()
    {
        // Check if the screen space reflection material uses the correct shader.
        if (material != null)
        {
            if (material.shader != Shader.Find(ssrShaderName))
            {
                Debug.LogErrorFormat("Screen Space Reflection URP: Material shader should be {0}.", ssrShaderName);
                return;
            }
        }
        // No material applied.
        else
        {
            //Debug.LogError("Screen Space Reflection URP: Post-processing material is empty.");
            return;
        }

#if UNITY_6000_0_OR_NEWER
        if (screenSpaceReflectionPass == null)
        {
            screenSpaceReflectionPass = new(resolution, mipmapsMode, material);
            screenSpaceReflectionPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }
        else
        {
            screenSpaceReflectionPass.resolution = resolution;
            screenSpaceReflectionPass.mipmapsMode = mipmapsMode;
        }
#else
        if (backFaceDepthPass == null)
        {
            backFaceDepthPass = new(material);
            // Skybox pass doesn't reset the render target in 2023.2.
            // If we change the render target after opaque rendering, it won't draw to the screen.
            backFaceDepthPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        }

        if (screenSpaceReflectionPass == null)
        {
            screenSpaceReflectionPass = new(resolution, mipmapsMode, material);
            screenSpaceReflectionPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents + 1;
        }
        else
        {
            // Update every frame to support runtime changes to these properties.
            screenSpaceReflectionPass.resolution = resolution;
            screenSpaceReflectionPass.mipmapsMode = mipmapsMode;
        }

        if (forwardGBufferPass == null)
        {
            forwardGBufferPass = new();
            forwardGBufferPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents; // Depth Priming
        }
#endif
    }

    protected override void Dispose(bool disposing)
    {
        if (screenSpaceReflectionPass != null)
            screenSpaceReflectionPass.Dispose();
#if !UNITY_6000_0_OR_NEWER
        if (backFaceDepthPass != null)
            backFaceDepthPass.Dispose();
        if (forwardGBufferPass != null)
            forwardGBufferPass.Dispose();
#endif
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null)
        {
            Debug.LogErrorFormat("Screen Space Reflection URP: Post-processing material is empty.");
            return;
        }

#if UNITY_6000_0_OR_NEWER
        var stack = VolumeManager.instance == null ? null : VolumeManager.instance.stack;
        ScreenSpaceReflection ssrVolume = stack == null ? null : stack.GetComponent<ScreenSpaceReflection>();
        bool isActive = ssrVolume != null && ssrVolume.IsActive();
        bool isDebugger = DebugManager.instance != null && DebugManager.instance.isAnyDebugUIActive;

        if (renderingData.cameraData.camera.cameraType != CameraType.Preview && isActive && (!isDebugger || renderingDebugger))
        {
            screenSpaceReflectionPass.resolution = resolution;
            screenSpaceReflectionPass.mipmapsMode = mipmapsMode;
            screenSpaceReflectionPass.ssrVolume = ssrVolume;
            renderer.EnqueuePass(screenSpaceReflectionPass);
            isLogPrinted = false;
        }
        else if (isDebugger && isLogPrinted == false)
        {
            Debug.Log("Screen Space Reflection URP: Disable effect to avoid affecting rendering debugging.");
            isLogPrinted = true;
        }
#else
        var renderingMode = (RenderingMode)renderingModeFieldInfo.GetValue(renderer as UniversalRenderer);
        bool isUsingDeferred = (renderingMode != RenderingMode.Forward) && (renderingMode != RenderingMode.ForwardPlus); // URP may have Deferred+ in the future.

        // URP forces Forward path on OpenGL platforms.
        bool isOpenGL = (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3) || (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore); // GLES 2 is removed.

        var stack = VolumeManager.instance == null ? null : VolumeManager.instance.stack;
        ScreenSpaceReflection ssrVolume = stack == null ? null : stack.GetComponent<ScreenSpaceReflection>();
        bool isActive = ssrVolume != null && ssrVolume.IsActive();
        bool isDebugger = DebugManager.instance != null && DebugManager.instance.isAnyDebugUIActive;

        bool isMotionValid = true;
#if UNITY_EDITOR
        // Motion Vectors of URP SceneView don't get updated each frame when not entering play mode. (Might be fixed when supporting scene view anti-aliasing)
        // Change the method to multi-frame accumulation (offline mode) if SceneView is not in play mode.
        isMotionValid = sceneView || UnityEditor.EditorApplication.isPlaying || renderingData.cameraData.camera.cameraType != CameraType.SceneView;
#endif

        if (renderingData.cameraData.camera.cameraType != CameraType.Preview && isActive && (!isDebugger || renderingDebugger))
        {
            if (!isUsingDeferred || isOpenGL) { renderer.EnqueuePass(forwardGBufferPass); }
            backFaceDepthPass.ssrVolume = ssrVolume;
            renderer.EnqueuePass(backFaceDepthPass);
            screenSpaceReflectionPass.isMotionValid = isMotionValid;
#if UNITY_2023_2_OR_NEWER
            // [PBR Accumulation] Looks like there's a bug with the queue of URP's final blit pass when enabling FXAA in 2023.2 (alpha & beta).
            // We will move the queue of SSR pass forward in that case.
            // The next step is to integrate with SRP render graph, and probably there will be more injection points available in URP, which makes PBR Accumulation more useful.
            screenSpaceReflectionPass.renderPassEvent = ssrVolume.algorithm == ScreenSpaceReflection.Algorithm.PBRAccumulation ? (ssrVolume.accumFactor.value == 0.0f ? RenderPassEvent.BeforeRenderingPostProcessing : (renderingData.cameraData.camera.cameraType != CameraType.SceneView && renderingData.cameraData.camera.GetComponent<UniversalAdditionalCameraData>().antialiasing == AntialiasingMode.FastApproximateAntialiasing) ? RenderPassEvent.AfterRenderingPostProcessing - 1 : RenderPassEvent.AfterRenderingPostProcessing) : RenderPassEvent.BeforeRenderingTransparents;
#else
            screenSpaceReflectionPass.renderPassEvent = ssrVolume.algorithm == ScreenSpaceReflection.Algorithm.PBRAccumulation ? (ssrVolume.accumFactor.value == 0.0f ? RenderPassEvent.BeforeRenderingPostProcessing : RenderPassEvent.AfterRenderingPostProcessing) : RenderPassEvent.BeforeRenderingTransparents;
#endif
            screenSpaceReflectionPass.ssrVolume = ssrVolume;
            renderer.EnqueuePass(screenSpaceReflectionPass);
            isLogPrinted = false;
        }
        else if (isDebugger && isLogPrinted == false)
        {
            Debug.Log("Screen Space Reflection URP: Disable effect to avoid affecting rendering debugging.");
            isLogPrinted = true;
        }
#endif
    }

}
