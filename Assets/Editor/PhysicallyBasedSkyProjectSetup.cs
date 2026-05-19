using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Devstead.Editor.Rendering
{
    public static class PhysicallyBasedSkyProjectSetup
    {
        private const string MainScenePath = "Assets/Scenes/MainScene.unity";
        private const string VolumeProfilePath = "Assets/Settings/MainSceneProfile.asset";

        private const string SkyShaderName = "Hidden/Skybox/PhysicallyBasedSky";
        private const string SkyLutShaderName = "Hidden/Sky/PhysicallyBasedSkyPrecomputation";
        private const string FallbackSkyMaterialGuid = "7c6697a3d51c9324c8e9ad3284a1ac04";
        private const string CloudsMaterialGuid = "3748131af20412e478461c6445c73923";

        [MenuItem("Tools/Rendering/Apply Physically Based Sky Setup")]
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
            var changedAssets = false;

            changedAssets |= ConfigureRendererAssets();
            changedAssets |= ConfigureVolumeProfile();
            changedAssets |= ConfigureMainScene();

            if (changedAssets)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log("Physically Based Sky setup complete.");
        }

        private static bool ConfigureRendererAssets()
        {
            var changed = false;
            var rendererDataAssets = FindRendererDataAssets();

            foreach (var rendererData in rendererDataAssets)
            {
                var isMobileRenderer = rendererData.name.IndexOf("Mobile", System.StringComparison.OrdinalIgnoreCase) >= 0;
                var feature = rendererData.rendererFeatures
                    .OfType<PhysicallyBasedSkyURP>()
                    .FirstOrDefault();

                if (feature == null)
                {
                    feature = ScriptableObject.CreateInstance<PhysicallyBasedSkyURP>();
                    feature.name = "Physically Based Sky URP";

                    AssetDatabase.AddObjectToAsset(feature, rendererData);

                    var rendererObject = new SerializedObject(rendererData);
                    var featuresProperty = rendererObject.FindProperty("m_RendererFeatures");
                    var featureMapProperty = rendererObject.FindProperty("m_RendererFeatureMap");

                    featuresProperty.arraySize++;
                    featuresProperty.GetArrayElementAtIndex(featuresProperty.arraySize - 1).objectReferenceValue = feature;

                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                    featureMapProperty.arraySize++;
                    featureMapProperty.GetArrayElementAtIndex(featureMapProperty.arraySize - 1).longValue = localId;

                    rendererObject.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }

                changed |= ConfigureRendererFeature(feature, isMobileRenderer);

                var cloudsFeature = rendererData.rendererFeatures
                    .OfType<VolumetricCloudsURP>()
                    .FirstOrDefault();

                if (cloudsFeature == null)
                {
                    cloudsFeature = ScriptableObject.CreateInstance<VolumetricCloudsURP>();
                    cloudsFeature.name = "VolumetricCloudsURP";

                    AssetDatabase.AddObjectToAsset(cloudsFeature, rendererData);

                    var rendererObject = new SerializedObject(rendererData);
                    var featuresProperty = rendererObject.FindProperty("m_RendererFeatures");
                    var featureMapProperty = rendererObject.FindProperty("m_RendererFeatureMap");

                    featuresProperty.arraySize++;
                    featuresProperty.GetArrayElementAtIndex(featuresProperty.arraySize - 1).objectReferenceValue = cloudsFeature;

                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(cloudsFeature, out _, out long localId);
                    featureMapProperty.arraySize++;
                    featureMapProperty.GetArrayElementAtIndex(featureMapProperty.arraySize - 1).longValue = localId;

                    rendererObject.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }

                changed |= ConfigureRendererFeature(cloudsFeature, isMobileRenderer);
                changed |= SynchronizeRendererFeatureMap(rendererData);
            }

            return changed;
        }

        private static IEnumerable<UniversalRendererData> FindRendererDataAssets()
        {
            return AssetDatabase.FindAssets(string.Empty, new[] { "Assets/Settings" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<UniversalRendererData>)
                .Where(rendererData => rendererData != null);
        }

        private static bool ConfigureRendererFeature(PhysicallyBasedSkyURP feature, bool isMobileRenderer)
        {
            var changed = false;
            var featureObject = new SerializedObject(feature);

            changed |= SetObjectReference(featureObject, "m_Shader", Shader.Find(SkyShaderName));
            changed |= SetObjectReference(featureObject, "m_LutShader", Shader.Find(SkyLutShaderName));
            changed |= SetObjectReference(featureObject, "m_FallbackSkyMaterial", LoadFallbackSkyMaterial());
            changed |= SetObjectReference(featureObject, "m_VolumetricCloudsMaterial", LoadCloudsMaterial());
            changed |= SetEnum(featureObject, "m_Precomputation", isMobileRenderer
                ? (int)PhysicallyBasedSkyURP.PrecomputationQualityMode.Low
                : (int)PhysicallyBasedSkyURP.PrecomputationQualityMode.High);

            if (!feature.isActive)
            {
                feature.SetActive(true);
                EditorUtility.SetDirty(feature);
                changed = true;
            }

            if (changed)
            {
                featureObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(feature);
            }

            return changed;
        }

        private static bool ConfigureRendererFeature(VolumetricCloudsURP feature, bool isMobileRenderer)
        {
            var changed = false;
            var featureObject = new SerializedObject(feature);

            changed |= SetObjectReference(featureObject, "material", LoadCloudsMaterial());
            changed |= SetBool(featureObject, "renderingDebugger", false);
            changed |= SetBool(featureObject, "reflectionProbe", !isMobileRenderer);
            changed |= SetFloat(featureObject, "resolutionScale", isMobileRenderer ? 0.5f : 0.6f);
            changed |= SetEnum(featureObject, "upscaleMode", (int)VolumetricCloudsURP.CloudsUpscaleMode.Bilinear);
            changed |= SetEnum(featureObject, "preferredRenderMode", (int)VolumetricCloudsURP.CloudsRenderMode.CopyTexture);
            changed |= SetEnum(featureObject, "ambientProbe", (int)VolumetricCloudsURP.CloudsAmbientMode.Dynamic);
            changed |= SetBool(featureObject, "sunAttenuation", false);
            changed |= SetBool(featureObject, "resetOnStart", true);
            changed |= SetBool(featureObject, "outputDepth", false);
            changed |= SetBool(featureObject, "depthTexture", false);

            if (!feature.isActive)
            {
                feature.SetActive(true);
                EditorUtility.SetDirty(feature);
                changed = true;
            }

            if (changed)
            {
                featureObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(feature);
            }

            return changed;
        }

        private static bool SynchronizeRendererFeatureMap(UniversalRendererData rendererData)
        {
            var changed = false;
            var rendererObject = new SerializedObject(rendererData);
            var featuresProperty = rendererObject.FindProperty("m_RendererFeatures");
            var featureMapProperty = rendererObject.FindProperty("m_RendererFeatureMap");

            if (featureMapProperty.arraySize != featuresProperty.arraySize)
            {
                featureMapProperty.arraySize = featuresProperty.arraySize;
                changed = true;
            }

            for (var i = 0; i < featuresProperty.arraySize; i++)
            {
                var feature = featuresProperty.GetArrayElementAtIndex(i).objectReferenceValue;
                if (feature == null)
                {
                    continue;
                }

                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                var mapElement = featureMapProperty.GetArrayElementAtIndex(i);
                if (mapElement.longValue == localId)
                {
                    continue;
                }

                mapElement.longValue = localId;
                changed = true;
            }

            if (changed)
            {
                rendererObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rendererData);
            }

            return changed;
        }

        private static bool ConfigureVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                Debug.LogWarning($"Physically Based Sky setup could not find Volume Profile at {VolumeProfilePath}.");
                return false;
            }

            var changed = false;

            if (!profile.TryGet<VisualEnvironment>(out var visualEnvironment))
            {
                visualEnvironment = profile.Add<VisualEnvironment>(false);
                changed = true;
            }

            changed |= SetActive(visualEnvironment, true);
            changed |= SetParameter(visualEnvironment.skyType, (int)VisualEnvironment.SkyType.PhysicallyBased, true);
            changed |= SetParameter(visualEnvironment.skyAmbientMode, VisualEnvironment.SkyAmbientMode.Dynamic, true);
            changed |= SetParameter(visualEnvironment.renderingSpace, VisualEnvironment.RenderingSpace.World, true);
            changed |= SetParameter(visualEnvironment.centerMode, VisualEnvironment.PlanetMode.Automatic, true);

            if (!profile.TryGet<PhysicallyBasedSky>(out var physicallyBasedSky))
            {
                physicallyBasedSky = profile.Add<PhysicallyBasedSky>(false);
                changed = true;
            }

            changed |= SetActive(physicallyBasedSky, true);
            changed |= SetParameter(physicallyBasedSky.type, PhysicallyBasedSky.PhysicallyBasedSkyModel.EarthAdvanced, true);
            changed |= SetParameter(physicallyBasedSky.atmosphericScattering, true, true);
            changed |= SetParameter(physicallyBasedSky.skyIntensityMode, PhysicallyBasedSky.SkyIntensityMode.Exposure, true);
            changed |= SetParameter(physicallyBasedSky.exposure, 0.0f, true);
            changed |= SetParameter(physicallyBasedSky.updateMode, PhysicallyBasedSky.EnvironmentUpdateMode.OnChanged, true);

            if (!profile.TryGet<VolumetricClouds>(out var volumetricClouds))
            {
                volumetricClouds = profile.Add<VolumetricClouds>(false);
                changed = true;
            }

            volumetricClouds.cloudPreset = VolumetricClouds.CloudPresets.Custom;
            changed |= SetActive(volumetricClouds, true);
            changed |= SetParameter(volumetricClouds.state, true, true);
            changed |= SetParameter(volumetricClouds.localClouds, true, true);
            changed |= SetParameter(volumetricClouds.densityMultiplier, 0.33f, true);
            changed |= SetParameter(volumetricClouds.shapeFactor, 0.87f, true);
            changed |= SetParameter(volumetricClouds.erosionFactor, 0.2f, true);
            changed |= SetParameter(volumetricClouds.erosionScale, 127.0f, true);
            changed |= SetParameter(volumetricClouds.microErosion, true, true);
            changed |= SetParameter(volumetricClouds.microErosionFactor, 0.5f, true);
            changed |= SetParameter(volumetricClouds.microErosionScale, 122.0f, true);
            changed |= SetParameter(volumetricClouds.bottomAltitude, 1200.0f, true);
            changed |= SetParameter(volumetricClouds.altitudeRange, 3200.0f, true);
            changed |= SetParameter(volumetricClouds.altitudeDistortion, 0.0f, true);
            changed |= SetParameter(volumetricClouds.shadowOpacity, 0.75f, true);
            changed |= SetParameter(volumetricClouds.perceptualBlending, 0.0f, true);
            changed |= SetParameter(volumetricClouds.numPrimarySteps, 32, true);
            changed |= SetParameter(volumetricClouds.numLightSteps, 1, true);
            changed |= SetParameter(volumetricClouds.fadeInMode, VolumetricClouds.CloudFadeInMode.Automatic, true);
            changed |= SetParameter(volumetricClouds.fadeInDistance, 500.0f, true);

            if (!profile.TryGet<Fog>(out var fog))
            {
                fog = profile.Add<Fog>(false);
                changed = true;
            }

            changed |= SetActive(fog, true);
            changed |= SetParameter(fog.enabled, false, true);
            changed |= SetParameter(fog.colorMode, Fog.FogColorMode.SkyColor, true);

            if (changed)
            {
                EditorUtility.SetDirty(profile);
                EditorUtility.SetDirty(visualEnvironment);
                EditorUtility.SetDirty(physicallyBasedSky);
                EditorUtility.SetDirty(volumetricClouds);
                EditorUtility.SetDirty(fog);
            }

            return changed;
        }

        private static bool ConfigureMainScene()
        {
            if (SceneManager.GetActiveScene().path != MainScenePath)
            {
                EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            }

            var sceneChanged = false;
            var sun = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude)
                .FirstOrDefault(light => light.type == LightType.Directional);

            if (sun != null)
            {
                if (RenderSettings.sun != sun)
                {
                    RenderSettings.sun = sun;
                    sceneChanged = true;
                }

                if (!Mathf.Approximately(sun.intensity, 3.030782f))
                {
                    sun.intensity = 3.030782f;
                    EditorUtility.SetDirty(sun);
                    sceneChanged = true;
                }
            }

            if (sceneChanged)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            }

            return sceneChanged;
        }

        private static Material LoadFallbackSkyMaterial()
        {
            var path = AssetDatabase.GUIDToAssetPath(FallbackSkyMaterialGuid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        private static Material LoadCloudsMaterial()
        {
            var path = AssetDatabase.GUIDToAssetPath(CloudsMaterialGuid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        private static bool SetObjectReference(SerializedObject serializedObject, string propertyName, Object value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue == value)
            {
                return false;
            }

            property.objectReferenceValue = value;
            return true;
        }

        private static bool SetEnum(SerializedObject serializedObject, string propertyName, int value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.enumValueIndex == value)
            {
                return false;
            }

            property.enumValueIndex = value;
            return true;
        }

        private static bool SetBool(SerializedObject serializedObject, string propertyName, bool value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.boolValue == value)
            {
                return false;
            }

            property.boolValue = value;
            return true;
        }

        private static bool SetFloat(SerializedObject serializedObject, string propertyName, float value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || Mathf.Approximately(property.floatValue, value))
            {
                return false;
            }

            property.floatValue = value;
            return true;
        }

        private static bool SetActive(VolumeComponent component, bool active)
        {
            if (component.active == active)
            {
                return false;
            }

            component.active = active;
            return true;
        }

        private static bool SetParameter<T>(VolumeParameter<T> parameter, T value, bool overrideState)
        {
            var changed = false;

            if (!EqualityComparer<T>.Default.Equals(parameter.value, value))
            {
                parameter.value = value;
                changed = true;
            }

            if (parameter.overrideState != overrideState)
            {
                parameter.overrideState = overrideState;
                changed = true;
            }

            return changed;
        }
    }
}
