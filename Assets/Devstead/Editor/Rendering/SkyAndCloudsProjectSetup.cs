using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Devstead.Rendering;

namespace Devstead.Editor.Rendering
{
    public static class SkyAndCloudsProjectSetup
    {
        private const string MenuPath = "Tools/Rendering/Apply Sky and Clouds Setup";
        private const string RenderingSettingsFolder = "Assets/Settings";
        private const string MainScenePath = "Assets/Devstead/Scenes/MainScene.unity";
        private const string VolumeProfilePath = "Assets/Devstead/Rendering/Profiles/MainSceneProfile.asset";
        private const string MoonTexturePath = "Assets/Devstead/Rendering/Textures/MoonAlbedo.jpg";
        private const string WaterMaterialPath = "Assets/Shaders/Uber Stylized Water/Template Materials/UWa-Template-Clear.mat";
        private const string MilkyWayHighQualityCubemapPath = "Assets/Devstead/Rendering/Textures/MilkyWayHighQuality.tif";
        private const string MilkyWayMediumCubemapPath = "Packages/dev.dyrda.milky-way-skybox/Runtime/Textures/eso0932a_mediumQuality.jpg";
        private const string MilkyWayPackagePathFragment = "dev.dyrda.milky-way-skybox";

        private const string SkyShaderName = "Hidden/Skybox/PhysicallyBasedSky";
        private const string SkyLutShaderName = "Hidden/Sky/PhysicallyBasedSkyPrecomputation";
        private const string FallbackSkyMaterialGuid = "7c6697a3d51c9324c8e9ad3284a1ac04";
        private const string CloudsMaterialGuid = "3748131af20412e478461c6445c73923";

        private const float DesktopCloudResolutionScale = 0.6f;
        private const float MobileCloudResolutionScale = 0.5f;
        private const float MainSunIntensity = 3.030782f;
        private const float MoonIntensity = 0.15f;
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
        private const float OceanWaterLevel = 0.0f;
        private const float OceanScale = 5000.0f;
        private const float OceanSupportScale = OceanScale * 10.0f;
        private const float OceanViewDistance = 5000.0f;
        private const float OceanFollowGridSize = 50.0f;

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
            var changedAssets = false;

            changedAssets |= ConfigureRendererAssets();
            changedAssets |= ConfigureVolumeProfile();
            changedAssets |= ConfigureMainScene();

            if (changedAssets)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log("Sky and Clouds setup complete.");
        }

        private static bool ConfigureRendererAssets()
        {
            var changed = false;
            var rendererDataAssets = FindRendererDataAssets();

            foreach (var rendererData in rendererDataAssets)
            {
                var isMobileRenderer = rendererData.name.IndexOf("Mobile", System.StringComparison.OrdinalIgnoreCase) >= 0;
                var skyFeature = GetOrCreateRendererFeature<PhysicallyBasedSkyURP>(rendererData, "Physically Based Sky URP", ref changed);
                var cloudsFeature = GetOrCreateRendererFeature<VolumetricCloudsURP>(rendererData, "VolumetricCloudsURP", ref changed);

                changed |= ConfigureRendererFeature(skyFeature, isMobileRenderer);
                changed |= ConfigureRendererFeature(cloudsFeature, isMobileRenderer);
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
            changed |= SetParameter(volumetricClouds.shadows, false, true);
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
            var sun = GetOrCreateSunLight(ref sceneChanged);
            sceneChanged |= ConfigureMoonLight(sun);
            sceneChanged |= ConfigureSkyController(sun);
            sceneChanged |= ConfigurePlayerCamera();
            sceneChanged |= ConfigureOcean();
            sceneChanged |= ConfigureSceneColliders();

            if (sceneChanged)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            }

            return sceneChanged;
        }

        private static bool ConfigureSceneColliders()
        {
            var changed = false;
            var floor = GameObject.Find("Shadow Test Floor");

            if (floor == null)
            {
                return false;
            }

            changed |= SetTransform(floor.transform, new Vector3(0.0f, -0.05f, 0.0f), Quaternion.identity, new Vector3(OceanSupportScale, 0.1f, OceanSupportScale));

            var renderer = floor.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.enabled)
            {
                renderer.enabled = false;
                EditorUtility.SetDirty(renderer);
                changed = true;
            }

            if (floor.GetComponent<Collider>() == null)
            {
                floor.AddComponent<BoxCollider>();
                EditorUtility.SetDirty(floor);
                changed = true;
            }

            return changed;
        }

        private static bool ConfigureOcean()
        {
            var changed = false;
            var oceanObject = GameObject.Find("Ocean");

            if (oceanObject == null)
            {
                oceanObject = new GameObject("Ocean");
                changed = true;
            }

            changed |= SetTransform(oceanObject.transform, Vector3.zero, Quaternion.identity, new Vector3(OceanScale, 1.0f, OceanScale));

            var meshFilter = oceanObject.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = oceanObject.AddComponent<MeshFilter>();
                changed = true;
            }

            var planeMesh = LoadBuiltinPlaneMesh();
            if (planeMesh != null && meshFilter.sharedMesh != planeMesh)
            {
                meshFilter.sharedMesh = planeMesh;
                EditorUtility.SetDirty(meshFilter);
                changed = true;
            }

            var meshRenderer = oceanObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = oceanObject.AddComponent<MeshRenderer>();
                changed = true;
            }

            var waterMaterial = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            if (waterMaterial != null && meshRenderer.sharedMaterial != waterMaterial)
            {
                meshRenderer.sharedMaterial = waterMaterial;
                changed = true;
            }
            else if (waterMaterial == null)
            {
                Debug.LogWarning($"Sky and Clouds setup could not find water material at {WaterMaterialPath}.");
            }

            if (meshRenderer.shadowCastingMode != ShadowCastingMode.Off)
            {
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                changed = true;
            }

            if (meshRenderer.receiveShadows)
            {
                meshRenderer.receiveShadows = false;
                changed = true;
            }

            var follower = oceanObject.GetComponent<InfiniteOceanFollower>();
            if (follower == null)
            {
                follower = oceanObject.AddComponent<InfiniteOceanFollower>();
                changed = true;
            }

            var followerObject = new SerializedObject(follower);
            changed |= SetObjectReference(followerObject, "target", Camera.main == null ? null : Camera.main.transform);
            changed |= SetFloat(followerObject, "waterLevel", OceanWaterLevel);
            changed |= SetFloat(followerObject, "followGridSize", OceanFollowGridSize);

            if (changed)
            {
                followerObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(oceanObject);
                EditorUtility.SetDirty(meshRenderer);
                EditorUtility.SetDirty(follower);
            }

            return changed;
        }

        private static Mesh LoadBuiltinPlaneMesh()
        {
            var temporaryPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            var mesh = temporaryPlane.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temporaryPlane);
            return mesh;
        }

        private static bool ConfigurePlayerCamera()
        {
            var changed = false;
            var camera = Camera.main;

            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                cameraObject.AddComponent<UniversalAdditionalCameraData>();
                changed = true;
            }

            var cameraTransform = camera.transform;
            changed |= SetObjectName(camera.gameObject, "Main Camera");
            changed |= SetTransform(cameraTransform, new Vector3(0.0f, 1.8f, -4.0f), Quaternion.Euler(10.0f, 0.0f, 0.0f), Vector3.one);

            if (!Mathf.Approximately(camera.farClipPlane, OceanViewDistance))
            {
                camera.farClipPlane = OceanViewDistance;
                changed = true;
            }

            var characterController = camera.GetComponent<CharacterController>();
            if (characterController == null)
            {
                characterController = camera.gameObject.AddComponent<CharacterController>();
                changed = true;
            }

            changed |= ConfigureCharacterController(characterController);

            var fpsController = camera.GetComponent<FirstPersonCameraController>();
            if (fpsController == null)
            {
                fpsController = camera.gameObject.AddComponent<FirstPersonCameraController>();
                changed = true;
            }

            changed |= ConfigureFirstPersonCameraController(fpsController);

            if (changed)
            {
                EditorUtility.SetDirty(camera.gameObject);
                EditorUtility.SetDirty(camera);
            }

            return changed;
        }

        private static bool ConfigureCharacterController(CharacterController characterController)
        {
            var changed = false;

            if (!Mathf.Approximately(characterController.height, 1.8f))
            {
                characterController.height = 1.8f;
                changed = true;
            }

            if (!Mathf.Approximately(characterController.radius, 0.35f))
            {
                characterController.radius = 0.35f;
                changed = true;
            }

            if (characterController.center != new Vector3(0.0f, -0.9f, 0.0f))
            {
                characterController.center = new Vector3(0.0f, -0.9f, 0.0f);
                changed = true;
            }

            if (!Mathf.Approximately(characterController.stepOffset, 0.3f))
            {
                characterController.stepOffset = 0.3f;
                changed = true;
            }

            if (!Mathf.Approximately(characterController.slopeLimit, 45.0f))
            {
                characterController.slopeLimit = 45.0f;
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(characterController);
            }

            return changed;
        }

        private static bool ConfigureFirstPersonCameraController(FirstPersonCameraController fpsController)
        {
            var controllerObject = new SerializedObject(fpsController);
            var changed = false;

            changed |= SetFloat(controllerObject, "moveSpeed", 5.0f);
            changed |= SetFloat(controllerObject, "sprintMultiplier", 1.75f);
            changed |= SetFloat(controllerObject, "jumpHeight", 1.2f);
            changed |= SetFloat(controllerObject, "gravity", -20.0f);
            changed |= SetFloat(controllerObject, "minimumEyeHeight", 1.8f);
            changed |= SetFloat(controllerObject, "mouseSensitivity", 2.0f);
            changed |= SetFloat(controllerObject, "minPitch", -85.0f);
            changed |= SetFloat(controllerObject, "maxPitch", 85.0f);
            changed |= SetBool(controllerObject, "lockCursorOnPlay", true);

            if (changed)
            {
                controllerObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(fpsController);
            }

            return changed;
        }

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
            var desiredAxis = new Vector3(0.2f, 1.0f, 0.35f);

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

        private static Texture2D LoadMoonTexture()
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(MoonTexturePath);
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

        private static bool SetVector3(SerializedObject serializedObject, string propertyName, Vector3 value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.vector3Value == value)
            {
                return false;
            }

            property.vector3Value = value;
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
