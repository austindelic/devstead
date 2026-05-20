using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public partial class VolumetricCloudsURP
{
    public partial class VolumetricCloudsPass
    {
        private static readonly int numPrimarySteps = Shader.PropertyToID("_NumPrimarySteps");
        private static readonly int numLightSteps = Shader.PropertyToID("_NumLightSteps");
        private static readonly int maxStepSize = Shader.PropertyToID("_MaxStepSize");
        private static readonly int highestCloudAltitude = Shader.PropertyToID("_HighestCloudAltitude");
        private static readonly int lowestCloudAltitude = Shader.PropertyToID("_LowestCloudAltitude");
        private static readonly int shapeNoiseOffset = Shader.PropertyToID("_ShapeNoiseOffset");
        private static readonly int verticalShapeNoiseOffset = Shader.PropertyToID("_VerticalShapeNoiseOffset");
        private static readonly int globalOrientation = Shader.PropertyToID("_WindDirection");
        private static readonly int globalSpeed = Shader.PropertyToID("_WindVector");
        private static readonly int verticalShapeDisplacement = Shader.PropertyToID("_VerticalShapeWindDisplacement");
        private static readonly int verticalErosionDisplacement = Shader.PropertyToID("_VerticalErosionWindDisplacement");
        private static readonly int shapeSpeedMultiplier = Shader.PropertyToID("_MediumWindSpeed");
        private static readonly int erosionSpeedMultiplier = Shader.PropertyToID("_SmallWindSpeed");
        private static readonly int altitudeDistortion = Shader.PropertyToID("_AltitudeDistortion");
        private static readonly int densityMultiplier = Shader.PropertyToID("_DensityMultiplier");
        private static readonly int powderEffectIntensity = Shader.PropertyToID("_PowderEffectIntensity");
        private static readonly int shapeScale = Shader.PropertyToID("_ShapeScale");
        private static readonly int shapeFactor = Shader.PropertyToID("_ShapeFactor");
        private static readonly int erosionScale = Shader.PropertyToID("_ErosionScale");
        private static readonly int erosionFactor = Shader.PropertyToID("_ErosionFactor");
        private static readonly int erosionOcclusion = Shader.PropertyToID("_ErosionOcclusion");
        private static readonly int microErosionScale = Shader.PropertyToID("_MicroErosionScale");
        private static readonly int microErosionFactor = Shader.PropertyToID("_MicroErosionFactor");
        private static readonly int fadeInStart = Shader.PropertyToID("_FadeInStart");
        private static readonly int fadeInDistance = Shader.PropertyToID("_FadeInDistance");
        private static readonly int multiScattering = Shader.PropertyToID("_MultiScattering");
        private static readonly int scatteringTint = Shader.PropertyToID("_ScatteringTint");
        private static readonly int ambientProbeDimmer = Shader.PropertyToID("_AmbientProbeDimmer");
        private static readonly int sunLightDimmer = Shader.PropertyToID("_SunLightDimmer");
        private static readonly int earthRadius = Shader.PropertyToID("_EarthRadius");
        private static readonly int accumulationFactor = Shader.PropertyToID("_AccumulationFactor");
        private static readonly int improvedTransmittanceBlend = Shader.PropertyToID("_ImprovedTransmittanceBlend");
        //private static readonly int normalizationFactor = Shader.PropertyToID("_NormalizationFactor");
        private static readonly int cloudsCurveLut = Shader.PropertyToID("_CloudCurveTexture");
        private static readonly int cloudnearPlane = Shader.PropertyToID("_CloudNearPlane");
        private static readonly int sunColor = Shader.PropertyToID("_SunColor");
        private static readonly int planetCenterRadius = Shader.PropertyToID("_PlanetCenterRadius");
        private static readonly int postExposure = Shader.PropertyToID("_PostExposure");

        private static readonly int cameraDepthTexture = Shader.PropertyToID(_CameraDepthTexture);
        private static readonly int volumetricCloudsColorTexture = Shader.PropertyToID(_VolumetricCloudsColorTexture);
        private static readonly int volumetricCloudsHistoryTexture = Shader.PropertyToID(_VolumetricCloudsHistoryTexture);
        private static readonly int volumetricCloudsDepthTexture = Shader.PropertyToID(_VolumetricCloudsDepthTexture);
        private static readonly int volumetricCloudsLightingTexture = Shader.PropertyToID(_VolumetricCloudsLightingTexture); // Same as "_VolumetricCloudsColorTexture"

        // unity_SH is not available when performing full screen blit pass
        private static readonly int shAr = Shader.PropertyToID("clouds_SHAr");
        private static readonly int shAg = Shader.PropertyToID("clouds_SHAg");
        private static readonly int shAb = Shader.PropertyToID("clouds_SHAb");
        private static readonly int shBr = Shader.PropertyToID("clouds_SHBr");
        private static readonly int shBg = Shader.PropertyToID("clouds_SHBg");
        private static readonly int shBb = Shader.PropertyToID("clouds_SHBb");
        private static readonly int shC = Shader.PropertyToID("clouds_SHC");

        private const string localClouds = "_LOCAL_VOLUMETRIC_CLOUDS";
        private const string microErosion = "_CLOUDS_MICRO_EROSION";
        private const string lowResClouds = "_LOW_RESOLUTION_CLOUDS";
        private const string cloudsAmbientProbe = "_CLOUDS_AMBIENT_PROBE";
        private const string outputCloudsDepth = "_OUTPUT_CLOUDS_DEPTH";
        private const string physicallyBasedSun = "_PHYSICALLY_BASED_SUN";
        private const string perceptualBlending = "_PERCEPTUAL_BLENDING";

        private const string _CameraDepthTexture = "_CameraDepthTexture";
        private const string _VolumetricCloudsColorTexture = "_VolumetricCloudsColorTexture";
        private const string _VolumetricCloudsHistoryTexture = "_VolumetricCloudsHistoryTexture";
        private const string _VolumetricCloudsAccumulationTexture = "_VolumetricCloudsAccumulationTexture";
        private const string _VolumetricCloudsDepthTexture = "_VolumetricCloudsDepthTexture";
        private const string _VolumetricCloudsLightingTexture = "_VolumetricCloudsLightingTexture"; // Same as "_VolumetricCloudsColorTexture"
        private const string _CameraTempDepthTexture = "_CameraTempDepthTexture";

        private static readonly Vector4 m_ScaleBias = new Vector4(1.0f, 1.0f, 0.0f, 0.0f);

        private readonly static FieldInfo depthTextureFieldInfo = typeof(UniversalRenderer).GetField("m_DepthTexture", BindingFlags.NonPublic | BindingFlags.Instance);
    }

    public partial class VolumetricCloudsAmbientPass
    {
        private const string _VolumetricCloudsAmbientProbe = "_VolumetricCloudsAmbientProbe";

        private const string STEREO_INSTANCING_ON = "STEREO_INSTANCING_ON";

        private static readonly int worldSpaceCameraPos = Shader.PropertyToID("_WorldSpaceCameraPos");
        private static readonly int disableSunDisk = Shader.PropertyToID("_DisableSunDisk");
        //private static readonly int unity_MatrixVP = Shader.PropertyToID("unity_MatrixVP");
        private static readonly int unity_MatrixInvVP = Shader.PropertyToID("unity_MatrixInvVP");
        private static readonly int scaledScreenParams = Shader.PropertyToID("_ScaledScreenParams");
        private static readonly int screenSize = Shader.PropertyToID("_ScreenSize");

        private static readonly int volumetricCloudsAmbientProbe = Shader.PropertyToID(_VolumetricCloudsAmbientProbe);
    }

    public partial class VolumetricCloudsShadowsPass
    {
        private static readonly int shadowCookieResolution = Shader.PropertyToID("_ShadowCookieResolution");
        private static readonly int shadowIntensity = Shader.PropertyToID("_ShadowIntensity");
        private static readonly int shadowOpacityFallback = Shader.PropertyToID("_ShadowOpacityFallback");
        private static readonly int cloudShadowSunOrigin = Shader.PropertyToID("_CloudShadowSunOrigin");
        private static readonly int cloudShadowSunRight = Shader.PropertyToID("_CloudShadowSunRight");
        private static readonly int cloudShadowSunUp = Shader.PropertyToID("_CloudShadowSunUp");
        private static readonly int cloudShadowSunForward = Shader.PropertyToID("_CloudShadowSunForward");
        private static readonly int cameraPositionPS = Shader.PropertyToID("_CameraPositionPS");
        private static readonly int volumetricCloudsShadowOriginToggle = Shader.PropertyToID("_VolumetricCloudsShadowOriginToggle");
        private static readonly int volumetricCloudsShadowScale = Shader.PropertyToID("_VolumetricCloudsShadowScale");
        //private static readonly int shadowPlaneOffset = Shader.PropertyToID("_ShadowPlaneOffset");

        private const string _VolumetricCloudsShadowTexture = "_VolumetricCloudsShadowTexture";
        private const string _VolumetricCloudsShadowTempTexture = "_VolumetricCloudsShadowTempTexture";

        private const string _LIGHT_COOKIES = "_LIGHT_COOKIES";
        private const string STEREO_INSTANCING_ON = "STEREO_INSTANCING_ON";

        private static readonly int mainLightTexture = Shader.PropertyToID("_MainLightCookieTexture");
        private static readonly int mainLightWorldToLight = Shader.PropertyToID("_MainLightWorldToLight");
        private static readonly int mainLightCookieTextureFormat = Shader.PropertyToID("_MainLightCookieTextureFormat");
    }
}
