using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Devstead.Rendering;
using Oceana;

namespace Devstead.Editor.Rendering
{
    public static partial class SkyAndCloudsProjectSetup
    {
        private const string MenuPath = "Tools/Rendering/Apply Sky and Clouds Setup";
        private const string RenderingSettingsFolder = "Assets/Settings";
        private const string MainScenePath = "Assets/Devstead/Scenes/MainScene.unity";
        private const string VolumeProfilePath = "Assets/Devstead/Rendering/Profiles/MainSceneProfile.asset";
        private const string PcRenderPipelineAssetPath = "Assets/Settings/PC_RPAsset.asset";
        private const string MobileRenderPipelineAssetPath = "Assets/Settings/Mobile_RPAsset.asset";
        private const string MoonTexturePath = "Assets/Devstead/Rendering/Textures/MoonAlbedo.jpg";
        private const string OceanaSettingsPath = "Assets/ThirdParty/Oceana/Settings/OceanaSettings.asset";
        private const string MilkyWayHighQualityCubemapPath = "Assets/Devstead/Rendering/Textures/MilkyWayHighQuality.tif";
        private const string MilkyWayMediumCubemapPath = "Packages/dev.dyrda.milky-way-skybox/Runtime/Textures/eso0932a_mediumQuality.jpg";
        private const string MilkyWayPackagePathFragment = "dev.dyrda.milky-way-skybox";

        private const string SkyShaderName = "Hidden/Skybox/PhysicallyBasedSky";
        private const string SkyLutShaderName = "Hidden/Sky/PhysicallyBasedSkyPrecomputation";
        private const string FallbackSkyMaterialGuid = "7c6697a3d51c9324c8e9ad3284a1ac04";
        private const string CloudsMaterialGuid = "3748131af20412e478461c6445c73923";
        private const string ScreenSpaceReflectionMaterialGuid = "3023044652f9eb940b765b89a445c572";
        private const string ScreenSpaceGlobalIlluminationShaderGuid = "052b080a79c052c4993acc96081d70b1";

        private const float DesktopCloudResolutionScale = 0.6f;
        private const float MobileCloudResolutionScale = 0.5f;
        private const float MainSunIntensity = 3.030782f;
        private const float MainSunColorTemperature = 4300.0f;
        private const float MoonIntensity = 0.15f;
        private const float MoonColorTemperature = 7000.0f;
        private const float SunRotationSpeedDegreesPerSecond = 10.0f;
        private const float MoonTimeOffset = 0.5f;
        private const float NightSkyFullSunAltitude = -0.12f;
        private const float NightSkyHiddenSunAltitude = -0.02f;
        private const float NightSkyEmissionMultiplier = 0.45f;
        private const float SpaceEmissionHorizonFadeStart = 0.01f;
        private const float SpaceEmissionHorizonFadeEnd = 0.22f;
        private const float SpaceEmissionContrast = 1.8f;
        private const float SpaceEmissionSaturation = 0.65f;
        private const float SpaceEmissionTwinkleStrength = 0.06f;
        private const float MainCameraFarClipPlane = 5000.0f;

        private static readonly SkyAndCloudsSetupStep[] SetupSteps =
        {
            new("Renderer Assets", ConfigureRendererAssets),
            new("Volume Profile", ConfigureVolumeProfile),
            new("Render Pipeline Assets", ConfigureRenderPipelineAssets),
            new("Main Scene", ConfigureMainScene),
        };

        [MenuItem(MenuPath)]
        public static void ApplyFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Apply();
        }

        public static void Apply()
        {
            var context = new SkyAndCloudsSetupContext();

            foreach (var step in SetupSteps)
            {
                context.Run(step);
            }

            if (context.ChangedAssets)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log("Sky and Clouds setup complete.");
        }

        private readonly struct SkyAndCloudsSetupStep
        {
            public SkyAndCloudsSetupStep(string name, System.Func<bool> apply)
            {
                Name = name;
                Apply = apply;
            }

            public string Name { get; }
            public System.Func<bool> Apply { get; }
        }

        private sealed class SkyAndCloudsSetupContext
        {
            private readonly List<string> changedStepNames = new();

            public bool ChangedAssets { get; private set; }
            public IReadOnlyList<string> ChangedStepNames => changedStepNames;

            public void Run(SkyAndCloudsSetupStep step)
            {
                if (step.Apply())
                {
                    ChangedAssets = true;
                    changedStepNames.Add(step.Name);
                }
            }
        }
    }
}
