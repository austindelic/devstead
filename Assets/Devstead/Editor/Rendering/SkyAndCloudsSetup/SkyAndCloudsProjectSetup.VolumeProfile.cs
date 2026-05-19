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
        private static bool ConfigureVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                Debug.LogWarning($"Sky and Clouds setup could not find Volume Profile at {VolumeProfilePath}.");
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

            var milkyWayCubemap = LoadMilkyWayCubemap();
            if (milkyWayCubemap != null)
            {
                changed |= SetParameter(physicallyBasedSky.spaceEmissionTexture, milkyWayCubemap, true);
                changed |= SetParameter(physicallyBasedSky.spaceEmissionMultiplier, NightSkyEmissionMultiplier, true);
            }
            else
            {
                Debug.LogWarning("Sky and Clouds setup could not find the Milky Way cubemap. Install dev.dyrda.milky-way-skybox from https://github.com/dyrdadev/milky-way-skybox-for-unity.git#0.0.3, then re-run setup.");
            }

            if (!profile.TryGet<VolumetricClouds>(out var volumetricClouds))
            {
                volumetricClouds = profile.Add<VolumetricClouds>(false);
                changed = true;
            }

            if (volumetricClouds.cloudPreset != VolumetricClouds.CloudPresets.Custom)
            {
                volumetricClouds.cloudPreset = VolumetricClouds.CloudPresets.Custom;
                changed = true;
            }

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
            changed |= SetParameter(volumetricClouds.shadows, true, true);
            changed |= SetParameter(volumetricClouds.shadowResolution, VolumetricClouds.CloudShadowResolution.High512, true);
            changed |= SetParameter(volumetricClouds.shadowDistance, 8000.0f, true);
            changed |= SetParameter(volumetricClouds.shadowOpacity, 0.55f, true);
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

            var screenSpaceReflection = GetOrCreateScreenSpaceReflectionVolume(profile, ref changed);
            if (screenSpaceReflection != null)
            {
                changed |= ConfigureScreenSpaceReflectionVolume(screenSpaceReflection);
            }

            var screenSpaceGlobalIllumination = GetOrCreateScreenSpaceGlobalIlluminationVolume(profile, ref changed);
            if (screenSpaceGlobalIllumination != null)
            {
                changed |= ConfigureScreenSpaceGlobalIlluminationVolume(screenSpaceGlobalIllumination);
            }

            if (changed)
            {
                EditorUtility.SetDirty(profile);
                EditorUtility.SetDirty(visualEnvironment);
                EditorUtility.SetDirty(physicallyBasedSky);
                EditorUtility.SetDirty(volumetricClouds);
                EditorUtility.SetDirty(fog);
                if (screenSpaceReflection != null)
                {
                    EditorUtility.SetDirty(screenSpaceReflection);
                }

                if (screenSpaceGlobalIllumination != null)
                {
                    EditorUtility.SetDirty(screenSpaceGlobalIllumination);
                }
            }

            return changed;
        }

        private static VolumeComponent GetOrCreateScreenSpaceGlobalIlluminationVolume(VolumeProfile profile, ref bool changed)
        {
            var componentType = System.Type.GetType("ScreenSpaceGlobalIlluminationVolume, SSGIURP");
            if (componentType == null)
            {
                Debug.LogWarning("Sky and Clouds setup could not find ScreenSpaceGlobalIlluminationVolume. Import UnitySSGIURP and re-run setup.");
                return null;
            }

            var component = profile.components.FirstOrDefault(volumeComponent =>
                volumeComponent != null && volumeComponent.GetType() == componentType);

            if (component != null)
            {
                return component;
            }

            component = profile.Add(componentType, false);
            changed = true;
            return component;
        }

        private static bool ConfigureScreenSpaceGlobalIlluminationVolume(VolumeComponent component)
        {
            var componentObject = new SerializedObject(component);
            var changed = false;

            changed |= SetActive(component, true);
            changed |= SetVolumeParameter(componentObject, "enable", true, true);
            changed |= SetVolumeParameter(componentObject, "quality", 1, true);
            changed |= SetVolumeParameter(componentObject, "thicknessMode", 0, true);
            changed |= SetVolumeParameter(componentObject, "depthBufferThickness", 0.1f, true);
            changed |= SetVolumeParameter(componentObject, "fullResolutionSS", false, true);
            changed |= SetVolumeParameter(componentObject, "resolutionScaleSS", 0.5f, true);
            changed |= SetVolumeParameter(componentObject, "sampleCount", 2, true);
            changed |= SetVolumeParameter(componentObject, "maxRaySteps", 32, true);
            changed |= SetVolumeParameter(componentObject, "denoiseSS", true, true);
            changed |= SetVolumeParameter(componentObject, "denoiserAlgorithmSS", 1, true);
            changed |= SetVolumeParameter(componentObject, "secondDenoiserPassSS", true, true);
            changed |= SetVolumeParameter(componentObject, "rayMiss", 3, true);
            changed |= SetVolumeParameter(componentObject, "indirectDiffuseLightingMultiplier", 1.0f, true);

            if (changed)
            {
                componentObject.ApplyModifiedPropertiesWithoutUndo();
            }

            return changed;
        }

        private static VolumeComponent GetOrCreateScreenSpaceReflectionVolume(VolumeProfile profile, ref bool changed)
        {
            var componentType = System.Type.GetType("ScreenSpaceReflection, ScreenSpaceReflection.Runtime");
            if (componentType == null)
            {
                Debug.LogWarning("Sky and Clouds setup could not find ScreenSpaceReflection. Import UnitySSReflectionURP and re-run setup.");
                return null;
            }

            var component = profile.components.FirstOrDefault(volumeComponent =>
                volumeComponent != null && volumeComponent.GetType() == componentType);

            if (component != null)
            {
                return component;
            }

            component = profile.Add(componentType, false);
            changed = true;
            return component;
        }

        private static bool ConfigureScreenSpaceReflectionVolume(VolumeComponent component)
        {
            var componentObject = new SerializedObject(component);
            var changed = false;

            changed |= SetActive(component, true);
            changed |= SetVolumeParameter(componentObject, "state", 1, true);
            changed |= SetVolumeParameter(componentObject, "algorithm", 0, true);
            changed |= SetVolumeParameter(componentObject, "edgeFade", 0.3f, true);
            changed |= SetVolumeParameter(componentObject, "thicknessMode", 0, true);
            changed |= SetVolumeParameter(componentObject, "thickness", 0.35f, true);
            changed |= SetVolumeParameter(componentObject, "quality", 3, true);
            changed |= SetVolumeParameter(componentObject, "maxStep", 32, true);
            changed |= SetVolumeParameter(componentObject, "accumFactor", 0.9f, true);

            if (changed)
            {
                componentObject.ApplyModifiedPropertiesWithoutUndo();
            }

            return changed;
        }
    }
}
