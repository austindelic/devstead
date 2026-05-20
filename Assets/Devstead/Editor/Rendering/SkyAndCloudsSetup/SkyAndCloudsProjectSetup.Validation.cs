using System.Collections.Generic;
using System.IO;
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
        private const string ValidationMenuPath = "Tools/Rendering/Validate Sky and Clouds Setup";

        [MenuItem(ValidationMenuPath)]
        public static void ValidateFromMenu()
        {
            ValidateAndLog();
        }

        public static bool ValidateAndLog()
        {
            var report = ValidateSetup();
            report.LogSummary("Sky and Clouds setup validation");
            return !report.HasErrors;
        }

        public static SkyAndCloudsValidationReport ValidateSetup()
        {
            var report = new SkyAndCloudsValidationReport();

            ValidateRequiredAssets(report);
            ValidateRendererAssets(report);
            ValidateVolumeProfile(report);
            ValidateMainScene(report);
            ValidateSerializedMissingScripts(report);

            return report;
        }

        private static void ValidateRequiredAssets(SkyAndCloudsValidationReport report)
        {
            if (!AssetDatabase.IsValidFolder(RenderingSettingsFolder))
            {
                report.Error($"Required rendering settings folder is missing: {RenderingSettingsFolder}");
            }

            RequireAsset<SceneAsset>(report, MainScenePath, "main scene");
            RequireAsset<VolumeProfile>(report, VolumeProfilePath, "main scene volume profile");
            RequireAsset<UniversalRenderPipelineAsset>(report, PcRenderPipelineAssetPath, "PC render pipeline asset");
            RequireAsset<UniversalRenderPipelineAsset>(report, MobileRenderPipelineAssetPath, "mobile render pipeline asset");
            RequireAsset<Texture2D>(report, MoonTexturePath, "moon albedo texture");
            RequireAsset<OceanaSettings>(report, OceanaSettingsPath, "Oceana settings");
            RequireGuidAsset<Material>(report, FallbackSkyMaterialGuid, "fallback sky material");
            RequireGuidAsset<Material>(report, CloudsMaterialGuid, "volumetric clouds material");

            if (Shader.Find(SkyShaderName) == null)
            {
                report.Error($"Required shader is missing: {SkyShaderName}");
            }

            if (Shader.Find(SkyLutShaderName) == null)
            {
                report.Error($"Required shader is missing: {SkyLutShaderName}");
            }

            if (LoadMilkyWayCubemap() == null)
            {
                report.Warning("Milky Way cubemap is missing. Install dev.dyrda.milky-way-skybox or provide the high-quality Devstead cubemap.");
            }

            if (System.Type.GetType("ScreenSpaceReflectionURP, ScreenSpaceReflection.Runtime") != null)
            {
                RequireGuidAsset<Material>(report, ScreenSpaceReflectionMaterialGuid, "screen-space reflection material");
            }

            if (System.Type.GetType("ScreenSpaceGlobalIlluminationURP, SSGIURP") != null)
            {
                RequireGuidAsset<Shader>(report, ScreenSpaceGlobalIlluminationShaderGuid, "screen-space global illumination shader");
            }
        }

        private static void ValidateRendererAssets(SkyAndCloudsValidationReport report)
        {
            var rendererDataAssets = FindRendererDataAssets().ToArray();
            if (rendererDataAssets.Length == 0)
            {
                report.Error($"No Universal Renderer Data assets were found in {RenderingSettingsFolder}.");
                return;
            }

            foreach (var rendererData in rendererDataAssets)
            {
                var isMobileRenderer = rendererData.name.IndexOf("Mobile", System.StringComparison.OrdinalIgnoreCase) >= 0;

                ValidateRendererFeature<PhysicallyBasedSkyURP>(report, rendererData, "Physically Based Sky URP", ValidatePhysicallyBasedSkyFeature);
                ValidateRendererFeature<VolumetricCloudsURP>(report, rendererData, "VolumetricCloudsURP", ValidateVolumetricCloudsFeature);
                ValidateRendererFeature<OceanaRenderFeature>(report, rendererData, "Oceana", ValidateOceanaFeature);

                if (!isMobileRenderer)
                {
                    ValidateOptionalRendererFeature(
                        report,
                        rendererData,
                        "ScreenSpaceGlobalIlluminationURP, SSGIURP",
                        "ScreenSpaceGlobalIlluminationURP");
                    ValidateOptionalRendererFeature(
                        report,
                        rendererData,
                        "ScreenSpaceReflectionURP, ScreenSpaceReflection.Runtime",
                        "ScreenSpaceReflectionURP");
                }

                ValidateRendererFeatureMap(report, rendererData);
            }
        }

        private static void ValidateVolumeProfile(SkyAndCloudsValidationReport report)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                return;
            }

            ValidateVolumeComponent<VisualEnvironment>(report, profile, "Visual Environment");
            ValidateVolumeComponent<PhysicallyBasedSky>(report, profile, "Physically Based Sky");
            ValidateVolumeComponent<VolumetricClouds>(report, profile, "Volumetric Clouds");
            ValidateVolumeComponent<Fog>(report, profile, "Fog");
            ValidateOptionalVolumeComponent(
                report,
                profile,
                "ScreenSpaceReflection, ScreenSpaceReflection.Runtime",
                "ScreenSpaceReflection");
            ValidateOptionalVolumeComponent(
                report,
                profile,
                "ScreenSpaceGlobalIlluminationVolume, SSGIURP",
                "ScreenSpaceGlobalIlluminationVolume");
        }

        private static void ValidateMainScene(SkyAndCloudsValidationReport report)
        {
            var previousActiveScene = SceneManager.GetActiveScene();
            var scene = GetLoadedMainScene();
            var validateRenderSettings = scene.IsValid() && previousActiveScene.path == scene.path;
            var closeSceneAfterValidation = false;

            if (!scene.IsValid())
            {
                try
                {
                    scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
                    closeSceneAfterValidation = true;
                }
                catch (System.Exception exception)
                {
                    report.Error($"Could not load main scene for validation: {exception.Message}");
                    return;
                }
            }

            try
            {
                ValidateSceneReferences(report, scene, validateRenderSettings);
                ValidateMissingScriptsInScene(report, scene);
            }
            finally
            {
                if (closeSceneAfterValidation && scene.IsValid())
                {
                    if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    {
                        SceneManager.SetActiveScene(previousActiveScene);
                    }

                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void ValidateSceneReferences(SkyAndCloudsValidationReport report, Scene scene, bool validateRenderSettings)
        {
            var sun = FindSceneObjects<Light>()
                .FirstOrDefault(light => light.gameObject.scene == scene && light.type == LightType.Directional && light.name == "Sun");
            var moon = FindSceneObjects<Light>()
                .FirstOrDefault(light => light.gameObject.scene == scene && light.type == LightType.Directional && light.name == "Moon");
            var globalVolume = FindSceneObjects<Volume>()
                .FirstOrDefault(volume => volume.gameObject.scene == scene && volume.name == "Global Volume");
            var skyController = FindSceneObjects<SkyController>()
                .FirstOrDefault(controller => controller.gameObject.scene == scene);
            var camera = FindSceneObjects<Camera>()
                .FirstOrDefault(sceneCamera => sceneCamera.gameObject.scene == scene && sceneCamera.CompareTag("MainCamera"));

            if (sun == null)
            {
                report.Error("Main scene is missing a directional Sun light named `Sun`.");
            }

            if (moon == null)
            {
                report.Error("Main scene is missing a directional Moon light named `Moon`.");
            }

            if (globalVolume == null)
            {
                report.Error("Main scene is missing a Global Volume.");
            }
            else
            {
                var expectedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
                if (globalVolume.profile != expectedProfile)
                {
                    report.Warning($"Global Volume does not reference {VolumeProfilePath}.");
                }
            }

            if (skyController == null)
            {
                report.Error("Main scene is missing a SkyController.");
            }
            else
            {
                if (skyController.Sun == null)
                {
                    report.Error("SkyController has no Sun reference.");
                }
                else if (sun != null && skyController.Sun != sun)
                {
                    report.Error("SkyController Sun reference does not point to the scene Sun light.");
                }

                if (skyController.Moon == null)
                {
                    report.Error("SkyController has no Moon reference.");
                }
                else if (moon != null && skyController.Moon != moon)
                {
                    report.Error("SkyController Moon reference does not point to the scene Moon light.");
                }

                if (skyController.SkyVolume == null)
                {
                    report.Error("SkyController has no sky volume reference.");
                }
                else if (globalVolume != null && skyController.SkyVolume != globalVolume)
                {
                    report.Error("SkyController sky volume reference does not point to the scene Global Volume.");
                }
            }

            if (camera == null)
            {
                report.Error("Main scene is missing a MainCamera-tagged camera.");
            }
            else
            {
                if (camera.GetComponent<CharacterController>() == null)
                {
                    report.Error("Main Camera is missing a CharacterController.");
                }

                if (camera.GetComponent<FirstPersonCameraController>() == null)
                {
                    report.Error("Main Camera is missing a FirstPersonCameraController.");
                }

                if (camera.GetComponent<UniversalAdditionalCameraData>() == null)
                {
                    report.Error("Main Camera is missing UniversalAdditionalCameraData.");
                }
            }

            if (sun != null && validateRenderSettings && RenderSettings.sun != sun)
            {
                report.Warning("RenderSettings.sun does not point to the scene Sun light.");
            }
        }

        private static void ValidateSerializedMissingScripts(SkyAndCloudsValidationReport report)
        {
            ValidateSerializedMissingScripts(report, "t:Prefab");
            ValidateSerializedMissingScripts(report, "t:Scene");
        }

        private static void ValidateSerializedMissingScripts(SkyAndCloudsValidationReport report, string filter)
        {
            foreach (var path in AssetDatabase.FindAssets(filter, new[] { "Assets/Devstead" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), path);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                string contents;
                try
                {
                    contents = File.ReadAllText(fullPath);
                }
                catch (System.Exception exception)
                {
                    report.Warning($"Could not scan {path} for missing scripts: {exception.Message}");
                    continue;
                }

                if (contents.Contains("m_Script: {fileID: 0", System.StringComparison.Ordinal))
                {
                    report.Error($"Serialized missing script reference found in {path}.");
                }
            }
        }

        private static void ValidateMissingScriptsInScene(SkyAndCloudsValidationReport report, Scene scene)
        {
            foreach (var gameObject in EnumerateSceneGameObjects(scene))
            {
                var components = gameObject.GetComponents<Component>();
                for (var i = 0; i < components.Length; i++)
                {
                    if (components[i] == null)
                    {
                        report.Error($"Missing script on scene object `{GetScenePath(gameObject)}`.");
                    }
                }
            }
        }

        private static void ValidateRendererFeature<T>(
            SkyAndCloudsValidationReport report,
            UniversalRendererData rendererData,
            string displayName,
            System.Action<SkyAndCloudsValidationReport, UniversalRendererData, T> validate)
            where T : ScriptableRendererFeature
        {
            var feature = rendererData.rendererFeatures.OfType<T>().FirstOrDefault();
            if (feature == null)
            {
                report.Error($"{rendererData.name} is missing renderer feature {displayName}.");
                return;
            }

            if (!feature.isActive)
            {
                report.Warning($"{rendererData.name} renderer feature {displayName} is inactive.");
            }

            validate(report, rendererData, feature);
        }

        private static void ValidateOptionalRendererFeature(
            SkyAndCloudsValidationReport report,
            UniversalRendererData rendererData,
            string assemblyQualifiedTypeName,
            string displayName)
        {
            var featureType = System.Type.GetType(assemblyQualifiedTypeName);
            if (featureType == null)
            {
                report.Warning($"Optional renderer feature type {displayName} is unavailable.");
                return;
            }

            var feature = rendererData.rendererFeatures.FirstOrDefault(rendererFeature =>
                rendererFeature != null && rendererFeature.GetType() == featureType);
            if (feature == null)
            {
                report.Warning($"{rendererData.name} is missing optional renderer feature {displayName}.");
                return;
            }

            if (!feature.isActive)
            {
                report.Warning($"{rendererData.name} optional renderer feature {displayName} is inactive.");
            }

            var featureObject = new SerializedObject(feature);
            if (displayName == "ScreenSpaceGlobalIlluminationURP")
            {
                ValidateObjectReference(report, featureObject, "m_Shader", rendererData.name, "screen-space global illumination shader");
            }
            else if (displayName == "ScreenSpaceReflectionURP")
            {
                ValidateObjectReference(report, featureObject, "material", rendererData.name, "screen-space reflection material");
            }
        }

        private static void ValidatePhysicallyBasedSkyFeature(
            SkyAndCloudsValidationReport report,
            UniversalRendererData rendererData,
            PhysicallyBasedSkyURP feature)
        {
            var featureObject = new SerializedObject(feature);
            ValidateObjectReference(report, featureObject, "m_Shader", rendererData.name, "sky shader");
            ValidateObjectReference(report, featureObject, "m_LutShader", rendererData.name, "sky LUT shader");
            ValidateObjectReference(report, featureObject, "m_FallbackSkyMaterial", rendererData.name, "fallback sky material");
            ValidateObjectReference(report, featureObject, "m_VolumetricCloudsMaterial", rendererData.name, "volumetric clouds material");
            ValidateObjectReference(report, featureObject, "m_MoonSurfaceTexture", rendererData.name, "moon surface texture");
        }

        private static void ValidateVolumetricCloudsFeature(
            SkyAndCloudsValidationReport report,
            UniversalRendererData rendererData,
            VolumetricCloudsURP feature)
        {
            var featureObject = new SerializedObject(feature);
            ValidateObjectReference(report, featureObject, "material", rendererData.name, "volumetric clouds material");
        }

        private static void ValidateOceanaFeature(
            SkyAndCloudsValidationReport report,
            UniversalRendererData rendererData,
            OceanaRenderFeature feature)
        {
            var featureObject = new SerializedObject(feature);
            ValidateObjectReference(report, featureObject, "m_Settings", rendererData.name, "Oceana settings");
        }

        private static void ValidateRendererFeatureMap(SkyAndCloudsValidationReport report, UniversalRendererData rendererData)
        {
            var rendererObject = new SerializedObject(rendererData);
            var featuresProperty = rendererObject.FindProperty("m_RendererFeatures");
            var featureMapProperty = rendererObject.FindProperty("m_RendererFeatureMap");

            if (featuresProperty == null || featureMapProperty == null)
            {
                report.Warning($"{rendererData.name} renderer feature serialized fields could not be inspected.");
                return;
            }

            if (featureMapProperty.arraySize != featuresProperty.arraySize)
            {
                report.Error($"{rendererData.name} renderer feature map size does not match renderer feature count.");
                return;
            }

            for (var i = 0; i < featuresProperty.arraySize; i++)
            {
                var feature = featuresProperty.GetArrayElementAtIndex(i).objectReferenceValue;
                if (feature == null)
                {
                    report.Error($"{rendererData.name} has a null renderer feature slot at index {i}.");
                    continue;
                }

                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                if (featureMapProperty.GetArrayElementAtIndex(i).longValue != localId)
                {
                    report.Error($"{rendererData.name} renderer feature map entry {i} does not match its feature local ID.");
                }
            }
        }

        private static void ValidateVolumeComponent<T>(SkyAndCloudsValidationReport report, VolumeProfile profile, string displayName)
            where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var component))
            {
                report.Error($"{VolumeProfilePath} is missing volume component {displayName}.");
                return;
            }

            if (!component.active)
            {
                report.Warning($"{VolumeProfilePath} volume component {displayName} is inactive.");
            }
        }

        private static void ValidateOptionalVolumeComponent(
            SkyAndCloudsValidationReport report,
            VolumeProfile profile,
            string assemblyQualifiedTypeName,
            string displayName)
        {
            var componentType = System.Type.GetType(assemblyQualifiedTypeName);
            if (componentType == null)
            {
                report.Warning($"Optional volume component type {displayName} is unavailable.");
                return;
            }

            var component = profile.components.FirstOrDefault(volumeComponent =>
                volumeComponent != null && volumeComponent.GetType() == componentType);
            if (component == null)
            {
                report.Warning($"{VolumeProfilePath} is missing optional volume component {displayName}.");
                return;
            }

            if (!component.active)
            {
                report.Warning($"{VolumeProfilePath} optional volume component {displayName} is inactive.");
            }
        }

        private static void ValidateObjectReference(
            SkyAndCloudsValidationReport report,
            SerializedObject serializedObject,
            string propertyName,
            string ownerName,
            string displayName)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                report.Warning($"{ownerName} could not inspect serialized property {propertyName} for {displayName}.");
                return;
            }

            if (property.objectReferenceValue == null)
            {
                report.Error($"{ownerName} has no {displayName} assigned.");
            }
        }

        private static void RequireAsset<T>(SkyAndCloudsValidationReport report, string path, string displayName)
            where T : UnityEngine.Object
        {
            if (AssetDatabase.LoadAssetAtPath<T>(path) == null)
            {
                report.Error($"Required {displayName} is missing at {path}.");
            }
        }

        private static void RequireGuidAsset<T>(SkyAndCloudsValidationReport report, string guid, string displayName)
            where T : UnityEngine.Object
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<T>(path) == null)
            {
                report.Error($"Required {displayName} with GUID {guid} is missing.");
            }
        }

        private static Scene GetLoadedMainScene()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.path == MainScenePath)
                {
                    return scene;
                }
            }

            return default;
        }

        private static IEnumerable<GameObject> EnumerateSceneGameObjects(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var stack = new Stack<Transform>();
                stack.Push(root.transform);

                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    yield return current.gameObject;

                    for (var i = current.childCount - 1; i >= 0; i--)
                    {
                        stack.Push(current.GetChild(i));
                    }
                }
            }
        }

        private static T[] FindSceneObjects<T>()
            where T : Object
        {
#if UNITY_6000_0_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include);
#else
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif
        }

        private static string GetScenePath(GameObject gameObject)
        {
            var names = new Stack<string>();
            var current = gameObject.transform;

            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }
    }

    public sealed class SkyAndCloudsValidationReport
    {
        private readonly List<SkyAndCloudsValidationIssue> issues = new();

        public IReadOnlyList<SkyAndCloudsValidationIssue> Issues => issues;
        public bool HasErrors => ErrorCount > 0;
        public int ErrorCount { get; private set; }
        public int WarningCount { get; private set; }

        public void Error(string message)
        {
            issues.Add(new SkyAndCloudsValidationIssue(SkyAndCloudsValidationSeverity.Error, message));
            ErrorCount++;
        }

        public void Warning(string message)
        {
            issues.Add(new SkyAndCloudsValidationIssue(SkyAndCloudsValidationSeverity.Warning, message));
            WarningCount++;
        }

        public void LogSummary(string title)
        {
            foreach (var issue in issues)
            {
                if (issue.Severity == SkyAndCloudsValidationSeverity.Error)
                {
                    Debug.LogError(issue.Message);
                }
                else
                {
                    Debug.LogWarning(issue.Message);
                }
            }

            var summary = $"{title}: {ErrorCount} error(s), {WarningCount} warning(s).";
            if (ErrorCount > 0)
            {
                Debug.LogError(summary);
            }
            else if (WarningCount > 0)
            {
                Debug.LogWarning(summary);
            }
            else
            {
                Debug.Log(summary);
            }
        }
    }

    public readonly struct SkyAndCloudsValidationIssue
    {
        public SkyAndCloudsValidationIssue(SkyAndCloudsValidationSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public SkyAndCloudsValidationSeverity Severity { get; }
        public string Message { get; }
    }

    public enum SkyAndCloudsValidationSeverity
    {
        Warning,
        Error,
    }
}
