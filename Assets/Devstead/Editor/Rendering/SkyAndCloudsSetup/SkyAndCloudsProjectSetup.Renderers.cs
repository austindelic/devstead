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
        private static bool ConfigureRendererAssets()
        {
            var changed = false;
            var rendererDataAssets = FindRendererDataAssets();

            foreach (var rendererData in rendererDataAssets)
            {
                var isMobileRenderer = rendererData.name.IndexOf("Mobile", System.StringComparison.OrdinalIgnoreCase) >= 0;
                var skyFeature = GetOrCreateRendererFeature<PhysicallyBasedSkyURP>(rendererData, "Physically Based Sky URP", ref changed);
                var cloudsFeature = GetOrCreateRendererFeature<VolumetricCloudsURP>(rendererData, "VolumetricCloudsURP", ref changed);
                var oceanaFeature = GetOrCreateRendererFeature<OceanaRenderFeature>(rendererData, "Oceana", ref changed);
                changed |= ConfigureRendererData(rendererData, isMobileRenderer);
                changed |= ConfigureScreenSpaceGlobalIlluminationFeature(rendererData, isMobileRenderer);
                changed |= ConfigureScreenSpaceReflectionFeature(rendererData, isMobileRenderer);
                changed |= ConfigureRendererFeature(skyFeature, isMobileRenderer);
                changed |= ConfigureRendererFeature(cloudsFeature, isMobileRenderer);
                changed |= ConfigureRendererFeature(oceanaFeature);
                changed |= SynchronizeRendererFeatureMap(rendererData);
            }

            return changed;
        }

        private static IEnumerable<UniversalRendererData> FindRendererDataAssets()
        {
            return AssetDatabase.FindAssets(string.Empty, new[] { RenderingSettingsFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<UniversalRendererData>)
                .Where(rendererData => rendererData != null);
        }

        private static T GetOrCreateRendererFeature<T>(UniversalRendererData rendererData, string featureName, ref bool changed)
            where T : ScriptableRendererFeature
        {
            var feature = rendererData.rendererFeatures
                .OfType<T>()
                .FirstOrDefault();

            if (feature != null)
            {
                return feature;
            }

            feature = ScriptableObject.CreateInstance<T>();
            feature.name = featureName;

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

            return feature;
        }

        private static bool ConfigureScreenSpaceGlobalIlluminationFeature(UniversalRendererData rendererData, bool isMobileRenderer)
        {
            if (isMobileRenderer)
            {
                return false;
            }

            var featureType = System.Type.GetType("ScreenSpaceGlobalIlluminationURP, SSGIURP");
            if (featureType == null)
            {
                Debug.LogWarning("Sky and Clouds setup could not find ScreenSpaceGlobalIlluminationURP. Import UnitySSGIURP and re-run setup.");
                return false;
            }

            var changed = false;
            var feature = rendererData.rendererFeatures
                .FirstOrDefault(rendererFeature => rendererFeature != null && rendererFeature.GetType() == featureType);

            if (feature == null)
            {
                feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(featureType);
                feature.name = "ScreenSpaceGlobalIlluminationURP";
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

            var featureObject = new SerializedObject(feature);

            changed |= SetObjectReference(featureObject, "m_Shader", LoadScreenSpaceGlobalIlluminationShader());
            changed |= SetBool(featureObject, "m_RenderingDebugger", false);
            changed |= SetBool(featureObject, "m_ReflectionProbes", false);
            changed |= SetBool(featureObject, "m_HighQualityUpscaling", false);
            changed |= SetBool(featureObject, "m_OverrideAmbientLighting", true);
            changed |= SetBool(featureObject, "m_BackfaceLighting", false);

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

        private static bool ConfigureScreenSpaceReflectionFeature(UniversalRendererData rendererData, bool isMobileRenderer)
        {
            if (isMobileRenderer)
            {
                return false;
            }

            var featureType = System.Type.GetType("ScreenSpaceReflectionURP, ScreenSpaceReflection.Runtime");
            if (featureType == null)
            {
                Debug.LogWarning("Sky and Clouds setup could not find ScreenSpaceReflectionURP. Import UnitySSReflectionURP and re-run setup.");
                return false;
            }

            var changed = false;
            var feature = rendererData.rendererFeatures
                .FirstOrDefault(rendererFeature => rendererFeature != null && rendererFeature.GetType() == featureType);

            if (feature == null)
            {
                feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(featureType);
                feature.name = "ScreenSpaceReflectionURP";
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

            var featureObject = new SerializedObject(feature);

            changed |= SetObjectReference(featureObject, "material", LoadScreenSpaceReflectionMaterial());
            changed |= SetBool(featureObject, "renderingDebugger", false);
            changed |= SetInt(featureObject, "resolution", 4);
            changed |= SetInt(featureObject, "mipmapsMode", 1);
            changed |= SetBool(featureObject, "sceneView", false);

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

        private static bool ConfigureRendererFeature(PhysicallyBasedSkyURP feature, bool isMobileRenderer)
        {
            var changed = false;
            var featureObject = new SerializedObject(feature);

            changed |= SetObjectReference(featureObject, "m_Shader", Shader.Find(SkyShaderName));
            changed |= SetObjectReference(featureObject, "m_LutShader", Shader.Find(SkyLutShaderName));
            changed |= SetObjectReference(featureObject, "m_FallbackSkyMaterial", LoadFallbackSkyMaterial());
            changed |= SetObjectReference(featureObject, "m_VolumetricCloudsMaterial", LoadCloudsMaterial());
            ConfigureMoonTextureImporter();
            changed |= SetObjectReference(featureObject, "m_MoonSurfaceTexture", LoadMoonTexture());
            changed |= SetFloat(featureObject, "m_SpaceEmissionHorizonFadeStart", SpaceEmissionHorizonFadeStart);
            changed |= SetFloat(featureObject, "m_SpaceEmissionHorizonFadeEnd", SpaceEmissionHorizonFadeEnd);
            changed |= SetFloat(featureObject, "m_SpaceEmissionContrast", SpaceEmissionContrast);
            changed |= SetFloat(featureObject, "m_SpaceEmissionSaturation", SpaceEmissionSaturation);
            changed |= SetFloat(featureObject, "m_SpaceEmissionTwinkleStrength", SpaceEmissionTwinkleStrength);
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
            changed |= SetFloat(featureObject, "resolutionScale", isMobileRenderer ? MobileCloudResolutionScale : DesktopCloudResolutionScale);
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

        private static bool ConfigureRendererFeature(OceanaRenderFeature feature)
        {
            var changed = false;
            var featureObject = new SerializedObject(feature);

            changed |= SetObjectReference(featureObject, "m_Settings", LoadOceanaSettings());

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

        private static bool ConfigureRendererData(UniversalRendererData rendererData, bool isMobileRenderer)
        {
            var changed = false;
            var rendererObject = new SerializedObject(rendererData);
            var renderingMode = isMobileRenderer ? RenderingMode.ForwardPlus : RenderingMode.DeferredPlus;

            changed |= SetInt(rendererObject, "m_RenderingMode", (int)renderingMode);

            if (changed)
            {
                rendererObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rendererData);
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
    }
}
