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
        private static Light GetOrCreateSunLight(ref bool changed)
        {
            var sun = Object.FindObjectsByType<Light>(FindObjectsInactive.Include)
                .FirstOrDefault(light => light.type == LightType.Directional && light.name == "Sun");

            sun ??= Object.FindObjectsByType<Light>(FindObjectsInactive.Include)
                .FirstOrDefault(light => light.type == LightType.Directional && light.name != "Moon");

            if (sun == null)
            {
                var sunObject = new GameObject("Sun");
                sun = sunObject.AddComponent<Light>();
                sunObject.AddComponent<UniversalAdditionalLightData>();
                changed = true;
            }
            else if (sun.GetComponent<UniversalAdditionalLightData>() == null)
            {
                sun.gameObject.AddComponent<UniversalAdditionalLightData>();
                changed = true;
            }

            changed |= SetObjectName(sun.gameObject, "Sun");
            changed |= SetTransform(sun.transform, new Vector3(0.0f, 3.0f, 0.0f), Quaternion.Euler(45.0f, -30.0f, 0.0f), Vector3.one);
            changed |= SetLight(sun, MainSunIntensity, 5000.0f, LightShadows.Soft);
            changed |= RemoveSunRotator(sun.gameObject);

            if (RenderSettings.sun != sun)
            {
                RenderSettings.sun = sun;
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(sun.gameObject);
                EditorUtility.SetDirty(sun);
            }

            return sun;
        }

        private static bool ConfigureMoonLight(Light sun)
        {
            var changed = false;
            var moon = Object.FindObjectsByType<Light>(FindObjectsInactive.Include)
                .FirstOrDefault(light => light.type == LightType.Directional && light.name == "Moon");

            if (moon == null)
            {
                var moonObject = new GameObject("Moon");
                moon = moonObject.AddComponent<Light>();
                moonObject.AddComponent<UniversalAdditionalLightData>();
                changed = true;
            }
            else if (moon.GetComponent<UniversalAdditionalLightData>() == null)
            {
                moon.gameObject.AddComponent<UniversalAdditionalLightData>();
                changed = true;
            }

            var moonTransform = moon.transform;
            var desiredRotation = Quaternion.Euler(225.0f, 150.0f, 0.0f);

            if (moon.type != LightType.Directional)
            {
                moon.type = LightType.Directional;
                changed = true;
            }

            changed |= SetObjectName(moon.gameObject, "Moon");
            changed |= SetTransform(moonTransform, Vector3.zero, desiredRotation, Vector3.one);
            changed |= SetLight(moon, MoonIntensity, 7000.0f, LightShadows.None);
            changed |= RemoveSunRotator(moon.gameObject);

            if (sun != null && RenderSettings.sun != sun)
            {
                RenderSettings.sun = sun;
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(moon.gameObject);
                EditorUtility.SetDirty(moon);
            }

            return changed;
        }

        private static bool ConfigureSkyController(Light sun)
        {
            var changed = false;
            var skyObject = GameObject.Find("Sky");

            if (skyObject == null)
            {
                skyObject = new GameObject("Sky");
                changed = true;
            }

            var moon = Object.FindObjectsByType<Light>(FindObjectsInactive.Include)
                .FirstOrDefault(light => light.type == LightType.Directional && light.name == "Moon");
            var volume = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include)
                .FirstOrDefault(sceneVolume => sceneVolume.name == "Global Volume");
            var controller = skyObject.GetComponent<SkyController>();

            if (controller == null)
            {
                controller = skyObject.AddComponent<SkyController>();
                changed = true;
            }

            changed |= ParentToSky(sun == null ? null : sun.transform, skyObject.transform);
            changed |= ParentToSky(moon == null ? null : moon.transform, skyObject.transform);
            changed |= ParentToSky(volume == null ? null : volume.transform, skyObject.transform);

            var controllerObject = new SerializedObject(controller);
            changed |= SetObjectReference(controllerObject, "sun", sun);
            changed |= SetObjectReference(controllerObject, "moon", moon);
            changed |= SetObjectReference(controllerObject, "skyVolume", volume);
            changed |= SetFloat(controllerObject, "timeOfDay", 0.375f);
            changed |= SetFloat(controllerObject, "moonTimeOffset", MoonTimeOffset);
            changed |= SetBool(controllerObject, "animateInPlayMode", true);
            changed |= SetFloat(controllerObject, "sunDegreesPerSecond", SunRotationSpeedDegreesPerSecond);
            changed |= SetVector3(controllerObject, "sunBaseEulerAngles", new Vector3(45.0f, -30.0f, 0.0f));
            changed |= SetVector3(controllerObject, "moonBaseEulerAngles", new Vector3(225.0f, 150.0f, 0.0f));
            changed |= SetFloat(controllerObject, "nightSkyFullSunAltitude", NightSkyFullSunAltitude);
            changed |= SetFloat(controllerObject, "nightSkyHiddenSunAltitude", NightSkyHiddenSunAltitude);
            changed |= SetFloat(controllerObject, "nightSkyEmissionMultiplier", NightSkyEmissionMultiplier);

            if (changed)
            {
                controllerObject.ApplyModifiedPropertiesWithoutUndo();
                controller.ApplySky();
                EditorUtility.SetDirty(skyObject);
                EditorUtility.SetDirty(controller);
            }

            return changed;
        }

        private static bool ParentToSky(Transform child, Transform sky)
        {
            if (child == null || sky == null || child.parent == sky)
            {
                return false;
            }

            child.SetParent(sky, true);
            return true;
        }

        private static bool SetObjectName(GameObject gameObject, string name)
        {
            if (gameObject.name == name)
            {
                return false;
            }

            gameObject.name = name;
            return true;
        }

        private static bool SetTransform(Transform transform, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var changed = false;

            if (transform.localPosition != position)
            {
                transform.localPosition = position;
                changed = true;
            }

            if (transform.localRotation != rotation)
            {
                transform.localRotation = rotation;
                changed = true;
            }

            if (transform.localScale != scale)
            {
                transform.localScale = scale;
                changed = true;
            }

            return changed;
        }

        private static bool SetLight(Light light, float intensity, float colorTemperature, LightShadows shadows)
        {
            var changed = false;

            if (!Mathf.Approximately(light.intensity, intensity))
            {
                light.intensity = intensity;
                changed = true;
            }

            if (!light.useColorTemperature)
            {
                light.useColorTemperature = true;
                changed = true;
            }

            if (!Mathf.Approximately(light.colorTemperature, colorTemperature))
            {
                light.colorTemperature = colorTemperature;
                changed = true;
            }

            if (light.shadows != shadows)
            {
                light.shadows = shadows;
                changed = true;
            }

            return changed;
        }

        private static bool RemoveSunRotator(GameObject gameObject)
        {
            var changed = false;
            var rotator = gameObject.GetComponent<SunRotator>();

            if (rotator != null)
            {
                Object.DestroyImmediate(rotator);
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(gameObject);
            }

            return changed;
        }
    }
}
