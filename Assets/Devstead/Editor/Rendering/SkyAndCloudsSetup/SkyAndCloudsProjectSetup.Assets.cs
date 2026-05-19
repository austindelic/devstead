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

        private static Material LoadScreenSpaceReflectionMaterial()
        {
            var path = AssetDatabase.GUIDToAssetPath(ScreenSpaceReflectionMaterialGuid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        private static Shader LoadScreenSpaceGlobalIlluminationShader()
        {
            var path = AssetDatabase.GUIDToAssetPath(ScreenSpaceGlobalIlluminationShaderGuid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Shader>(path);
        }

        private static Texture2D LoadMoonTexture()
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(MoonTexturePath);
        }

        private static OceanaSettings LoadOceanaSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<OceanaSettings>(OceanaSettingsPath);

            if (settings == null)
            {
                Debug.LogWarning($"Sky and Clouds setup could not find Oceana settings at {OceanaSettingsPath}.");
            }

            return settings;
        }

        private static Cubemap LoadMilkyWayCubemap()
        {
            var highQualityCubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(MilkyWayHighQualityCubemapPath);
            if (highQualityCubemap != null)
            {
                return highQualityCubemap;
            }

            var directCubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(MilkyWayMediumCubemapPath);
            if (directCubemap != null)
            {
                return directCubemap;
            }

            var cubemapPaths = AssetDatabase.FindAssets("eso0932a t:Cubemap")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.Contains(MilkyWayPackagePathFragment, System.StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var mediumPath = cubemapPaths.FirstOrDefault(path => path.Contains("medium", System.StringComparison.OrdinalIgnoreCase));
            var fallbackPath = mediumPath ?? cubemapPaths.FirstOrDefault();

            return string.IsNullOrEmpty(fallbackPath) ? null : AssetDatabase.LoadAssetAtPath<Cubemap>(fallbackPath);
        }

        private static void ConfigureMoonTextureImporter()
        {
            if (AssetImporter.GetAtPath(MoonTexturePath) is not TextureImporter textureImporter)
            {
                return;
            }

            var changed = false;
            changed |= SetTextureImporterValue(textureImporter.wrapMode != TextureWrapMode.Clamp, value => textureImporter.wrapMode = value, TextureWrapMode.Clamp);
            changed |= SetTextureImporterValue(textureImporter.filterMode != FilterMode.Bilinear, value => textureImporter.filterMode = value, FilterMode.Bilinear);
            changed |= SetTextureImporterValue(!textureImporter.mipmapEnabled, value => textureImporter.mipmapEnabled = value, true);
            changed |= SetTextureImporterValue(textureImporter.textureType != TextureImporterType.Default, value => textureImporter.textureType = value, TextureImporterType.Default);
            changed |= SetTextureImporterValue(!textureImporter.sRGBTexture, value => textureImporter.sRGBTexture = value, true);

            if (changed)
            {
                textureImporter.SaveAndReimport();
            }
        }

        private static bool SetTextureImporterValue<T>(bool shouldSet, System.Action<T> setter, T value)
        {
            if (!shouldSet)
            {
                return false;
            }

            setter(value);
            return true;
        }
    }
}
