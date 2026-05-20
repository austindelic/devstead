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

/// <summary>
/// A renderer feature that adds volumetric clouds support to the URP volume.
/// </summary>
[DisallowMultipleRendererFeature("Volumetric Clouds URP")]
[Tooltip("Add this Renderer Feature to support volumetric clouds in URP Volume.")]
[HelpURL("https://github.com/jiaozi158/UnityVolumetricCloudsURP/tree/main")]
public partial class VolumetricCloudsURP : ScriptableRendererFeature
{
    [Header("Setup")]
    [Tooltip("The material of volumetric clouds shader.")]
    [SerializeField] private Material material;
    [Tooltip("Enable this to render volumetric clouds in Rendering Debugger view. \nThis is disabled by default to avoid affecting the individual lighting previews.")]
    [SerializeField] private bool renderingDebugger = false;

    [Header("Performance")]
    [Tooltip("Specifies if URP renders volumetric clouds in both real-time and baked reflection probes. \nVolumetric clouds in real-time reflection probes may reduce performance.")]
    [SerializeField] private bool reflectionProbe = false;
    [Range(0.25f, 1.0f), Tooltip("The resolution scale for volumetric clouds rendering.")]
    [SerializeField] private float resolutionScale = 0.5f;
    [Tooltip("Select the method to use for upscaling volumetric clouds.")]
    [SerializeField] private CloudsUpscaleMode upscaleMode = CloudsUpscaleMode.Bilinear;
    [Tooltip("Specifies the preferred texture render mode for volumetric clouds. \nThe Copy Texture mode should be more performant.")]
    [SerializeField] private CloudsRenderMode preferredRenderMode = CloudsRenderMode.CopyTexture;

    [Header("Lighting")]
    [Tooltip("Specifies the volumetric clouds ambient probe update frequency.")]
    [SerializeField] private CloudsAmbientMode ambientProbe = CloudsAmbientMode.Dynamic;
    [Tooltip("Specifies if URP calculates physically based sun attenuation for volumetric clouds.")]
    [SerializeField] private bool sunAttenuation = false;

    [Header("Wind")]
    [Tooltip("Enable to reset the wind offsets to their initial states when start playing.")]
    [SerializeField] private bool resetOnStart = true;

    [Header("Depth")]
    [Tooltip("Specifies if URP outputs volumetric clouds average depth to a global shader texture named \"_VolumetricCloudsDepthTexture\".")]
    [SerializeField] private bool outputDepth = true;

    [Header("Experimental"), Tooltip("Specifies if URP also outputs volumetric clouds average depth to \"_CameraDepthTexture\".")]
    [SerializeField] private bool depthTexture = false;

    private const string shaderName = "Hidden/Sky/VolumetricClouds";
    private const string VOLUMETRIC_CLOUDS = "VOLUMETRIC_CLOUDS";
    private const string VISUAL_ENVIRONMENT_DYNAMIC_SKY = "VISUAL_ENVIRONMENT_DYNAMIC_SKY";
    private VolumetricCloudsPass volumetricCloudsPass;
    private VolumetricCloudsAmbientPass volumetricCloudsAmbientPass;
    private VolumetricCloudsShadowsPass volumetricCloudsShadowsPass;

    // Print message only once.
    private bool isLogPrinted = false;
    private bool isCookiePrinted = false;

    /// <summary>
    /// Gets or sets the material of volumetric clouds shader.
    /// </summary>
    /// <value>
    /// The material of volumetric clouds shader.
    /// </value>
    public Material CloudsMaterial
    {
        get { return material; }
        set { material = (value.shader == Shader.Find(shaderName)) ? value : material; }
    }

    /// <summary>
    /// Gets or sets a value indicating whether to render volumetric clouds in Rendering Debugger view.
    /// </summary>
    /// <value>
    /// <c>true</c> if rendering volumetric clouds in Rendering Debugger view; otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// This is disabled by default to avoid affecting the individual lighting previews.
    /// </remarks>
    public bool RenderingDebugger
    {
        get { return renderingDebugger; }
        set { renderingDebugger = value; }
    }

    /// <summary>
    /// Gets or sets the resolution scale for volumetric clouds rendering.
    /// </summary>
    /// <value>
    /// The resolution scale for volumetric clouds rendering, ranging from 0.25 to 1.0.
    /// </value>
    public float ResolutionScale
    {
        get { return resolutionScale; }
        set { resolutionScale = Mathf.Clamp(value, 0.25f, 1.0f); }
    }

    /// <summary>
    /// Gets or sets the preferred texture render mode for volumetric clouds.
    /// </summary>
    /// <value>
    /// The preferred texture render mode for volumetric clouds, either CopyTexture or BlitTexture.
    /// </value>
    /// <remarks>
    /// The CopyTexture mode should be more performant.
    /// </remarks>
    public CloudsRenderMode PreferredRenderMode
    {
        get { return preferredRenderMode; }
        set { preferredRenderMode = value; }
    }

    /// <summary>
    /// Gets or sets the ambient probe update frequency for volumetric clouds.
    /// </summary>
    /// <value>
    /// The ambient probe update frequency for volumetric clouds, either Static or Dynamic.
    /// </value>
    public CloudsAmbientMode AmbientUpdateMode
    {
        get { return ambientProbe; }
        set { ambientProbe = value; }
    }

    /// <summary>
    /// Gets or sets the method used for upscaling volumetric clouds.
    /// </summary>
    /// <value>
    /// The method to use for upscaling volumetric clouds.
    /// </value>
    public CloudsUpscaleMode UpscaleMode
    {
        get { return upscaleMode; }
        set { upscaleMode = value; }
    }

    /// <summary>
    /// Gets or sets a value indicating whether to reset wind offsets for volumetric clouds when entering playmode.
    /// </summary>
    /// <value>
    /// <c>true</c> if resetting wind offsets when entering playmode; otherwise, <c>false</c>.
    /// </value>
    public bool ResetWindOnStart
    {
        get { return resetOnStart; }
        set { resetOnStart = value; }
    }

    /// <summary>
    /// Gets or sets a value indicating whether URP calculates physically based sun attenuation for volumetric clouds.
    /// </summary>
    /// <value>
    /// <c>true</c> if URP calculates physically based sun attenuation for volumetric clouds; otherwise, <c>false</c>.
    /// </value>
    public bool SunAttenuation
    {
        get { return sunAttenuation; }
        set { sunAttenuation = value; }
    }

    /// <summary>
    /// Gets or sets a value indicating whether URP outputs volumetric clouds average depth to a global shader texture named "_VolumetricCloudsDepthTexture".
    /// </summary>
    /// <value>
    /// <c>true</c> if URP outputs volumetric clouds average depth; otherwise, <c>false</c>.
    /// </value>
    public bool OutputCloudsDepth
    {
        get { return outputDepth; }
        set { outputDepth = value; }
    }

    /// <summary>
    /// Gets or sets a value indicating whether URP also outputs volumetric clouds average depth to "_CameraDepthTexture".
    /// </summary>
    public bool OutputToSceneDepth
    {
        get { return depthTexture; }
        set { depthTexture = value; }
    }

    public enum CloudsRenderMode
    {
        [Tooltip("Always use Blit() to copy render textures.")]
        BlitTexture = 0,

        [Tooltip("Use CopyTexture() to copy render textures when supported.")]
        CopyTexture = 1
    }

    public enum CloudsAmbientMode
    {
        [Tooltip("Use URP default static ambient probe for volumetric clouds rendering.")]
        Static,

        [Tooltip("Use a fast dynamic ambient probe for volumetric clouds rendering.")]
        Dynamic
    }

    public enum CloudsUpscaleMode
    {
        [Tooltip("Use simple but fast filtering for volumetric clouds upscale.")]
        Bilinear,

        [Tooltip("Use more computationally expensive filtering for volumetric clouds upscale. \nThis blurs the cloud details but reduces the noise that may appear at lower clouds resolutions.")]
        Bilateral
    }

    public override void Create()
    {
        // Check if the volumetric clouds material uses the correct shader.
        if (material != null)
        {
            if (material.shader != Shader.Find(shaderName))
            {
            #if UNITY_EDITOR || DEBUG
                Debug.LogErrorFormat("Volumetric Clouds URP: Material shader is not {0}.", shaderName);
            #endif
                return;
            }
        }
        // No material applied.
        else
        {
        #if UNITY_EDITOR || DEBUG
            Debug.LogError("Volumetric Clouds URP: Material is empty.");
        #endif
            return;
        }

        // Renderer assets can be constructed before Unity has initialized a Volume stack.
        // Treat clouds as inactive for that pass instead of failing renderer creation.
        bool isDebugger = DebugManager.instance != null && DebugManager.instance.isAnyDebugUIActive;
        var stack = VolumeManager.instance == null ? null : VolumeManager.instance.stack;
        VolumetricClouds cloudsVolume = stack == null ? null : stack.GetComponent<VolumetricClouds>();
        bool isVolumeActive = cloudsVolume != null && cloudsVolume.IsActive() && (!isDebugger || renderingDebugger);

        if (!isActive || !isVolumeActive)
            Shader.DisableKeyword(VOLUMETRIC_CLOUDS);
        else
            Shader.EnableKeyword(VOLUMETRIC_CLOUDS);

        if (volumetricCloudsPass == null)
        {
            volumetricCloudsPass = new(material, resolutionScale);
            volumetricCloudsPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents; // Use camera previous matrix to do reprojection
        }
        else
        {
            // Update every frame to support runtime changes to these properties.
            volumetricCloudsPass.resolutionScale = resolutionScale;
            volumetricCloudsPass.upscaleMode = upscaleMode;
            volumetricCloudsPass.dynamicAmbientProbe = ambientProbe == CloudsAmbientMode.Dynamic;
        }

        if (volumetricCloudsAmbientPass == null)
        {
            volumetricCloudsAmbientPass = new(material);
            volumetricCloudsAmbientPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents - 1;
        }

        if (volumetricCloudsShadowsPass == null)
        {
            volumetricCloudsShadowsPass = new(material);
            volumetricCloudsShadowsPass.renderPassEvent = RenderPassEvent.BeforeRendering;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (volumetricCloudsPass != null)
            volumetricCloudsPass.Dispose();
        if (volumetricCloudsAmbientPass != null)
            volumetricCloudsAmbientPass.Dispose();
        if (volumetricCloudsShadowsPass != null)
            volumetricCloudsShadowsPass.Dispose();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null)
        {
        #if UNITY_EDITOR || DEBUG
            Debug.LogErrorFormat("Volumetric Clouds URP: Material is empty.");
        #endif
            return;
        }

    #if UNITY_EDITOR
        bool isEditingPrefab = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null;
        bool isSceneViewFocused = UnityEditor.SceneView.lastActiveSceneView != null && UnityEditor.SceneView.lastActiveSceneView.hasFocus;
        // Disable Volumetric Clouds when entering prefab mode.
        if (isEditingPrefab && isSceneViewFocused)
            return;
    #endif

        var stack = VolumeManager.instance == null ? null : VolumeManager.instance.stack;
        VolumetricClouds cloudsVolume = stack == null ? null : stack.GetComponent<VolumetricClouds>();
        ColorAdjustments colorAdjustments = stack == null ? null : stack.GetComponent<ColorAdjustments>();
        bool isDebugger = DebugManager.instance != null && DebugManager.instance.isAnyDebugUIActive;
        bool isVolumeActive = cloudsVolume != null && cloudsVolume.IsActive() && (!isDebugger || renderingDebugger);

        bool isProbeCamera = renderingData.cameraData.cameraType == CameraType.Reflection && reflectionProbe;

        if (isVolumeActive)
            Shader.EnableKeyword(VOLUMETRIC_CLOUDS);
        else
            Shader.DisableKeyword(VOLUMETRIC_CLOUDS);

        if (isVolumeActive && (renderingData.cameraData.cameraType == CameraType.Game || renderingData.cameraData.cameraType == CameraType.SceneView || isProbeCamera))
        {
        #if URP_PBSKY
            VisualEnvironment visualEnvironment = stack.GetComponent<VisualEnvironment>();

            // Check if the ambient probe is already updating dynamically.
            bool isDynamicPbrSky = visualEnvironment != null && visualEnvironment.IsActive() && visualEnvironment.skyAmbientMode.value == VisualEnvironment.SkyAmbientMode.Dynamic && Shader.IsKeywordEnabled(VISUAL_ENVIRONMENT_DYNAMIC_SKY);
            bool dynamicAmbientProbe = !isDynamicPbrSky && ambientProbe == CloudsAmbientMode.Dynamic;
        #else
            bool dynamicAmbientProbe = ambientProbe == CloudsAmbientMode.Dynamic;
        #endif
            volumetricCloudsPass.cloudsVolume = cloudsVolume;
            volumetricCloudsPass.colorAdjustments = colorAdjustments;
            volumetricCloudsPass.dynamicAmbientProbe = dynamicAmbientProbe;
            volumetricCloudsPass.renderMode = preferredRenderMode;
            volumetricCloudsPass.resetWindOnStart = resetOnStart;
            volumetricCloudsPass.outputDepth = depthTexture || outputDepth; // Implicitly enable clouds depth when we need to output to scene depth
            volumetricCloudsPass.outputToSceneDepth = depthTexture;
            volumetricCloudsPass.sunAttenuation = sunAttenuation;

            volumetricCloudsShadowsPass.cloudsVolume = cloudsVolume;

        #if URP_PBSKY
            PhysicallyBasedSky pbrSky = stack.GetComponent<PhysicallyBasedSky>();
            Fog fog = stack.GetComponent<Fog>();
            volumetricCloudsPass.hasAtmosphericScattering = visualEnvironment != null && visualEnvironment.IsActive() && visualEnvironment.skyType.value == (int)VisualEnvironment.SkyType.PhysicallyBased && pbrSky != null && pbrSky.IsActive() && pbrSky.atmosphericScattering.value;
            volumetricCloudsPass.hasAtmosphericScattering |= fog != null && fog.IsActive();
            volumetricCloudsPass.visualEnvVolume = visualEnvironment;
        #else
            volumetricCloudsPass.hasAtmosphericScattering = false;
        #endif

            renderer.EnqueuePass(volumetricCloudsPass);

            if (cloudsVolume.shadows.value)
            {
                // Check if URP supports "Light Cookies"
                UniversalRenderPipelineAsset asset = UniversalRenderPipeline.asset;
                if (asset.supportsLightCookies)
                {
                    isCookiePrinted = false;
                #if URP_PBSKY
                    volumetricCloudsShadowsPass.visualEnvVolume = visualEnvironment;
                #endif
                    renderer.EnqueuePass(volumetricCloudsShadowsPass);
                }
            #if UNITY_EDITOR || DEBUG
                else
                {
                    // URP may have stripped light cookie varients (in build), so skip the shadow cookie rendering
                    if (!isCookiePrinted) { Debug.LogWarning("Volumetric Clouds URP: Light Cookies are disabled in the active URP asset. The volumetric clouds shadows will not be rendered."); isCookiePrinted = true; }
                }
            #endif
            }

            // No need to render dynamic ambient probe for reflection probes.
            if (dynamicAmbientProbe && !isProbeCamera) { renderer.EnqueuePass(volumetricCloudsAmbientPass); }

            isLogPrinted = false;
        }
    #if UNITY_EDITOR || DEBUG
        else if (isDebugger && !renderingDebugger && !isLogPrinted)
        {
            Debug.Log("Volumetric Clouds URP: Disable effect to avoid affecting rendering debugging.");
            isLogPrinted = true;
        }
    #endif
    }
}
