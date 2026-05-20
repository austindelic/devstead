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

            if (sceneChanged)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            }

            return sceneChanged;
        }

        private static bool ConfigureRenderPipelineAssets()
        {
            var changed = false;

            var pcRpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcRenderPipelineAssetPath);
            if (pcRpAsset != null)
            {
                changed |= ConfigureRenderPipelineAsset(pcRpAsset);
            }

            var mobileRpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileRenderPipelineAssetPath);
            if (mobileRpAsset != null)
            {
                changed |= ConfigureRenderPipelineAsset(mobileRpAsset);
            }

            return changed;
        }

        private static bool ConfigureRenderPipelineAsset(UniversalRenderPipelineAsset renderPipelineAsset)
        {
            var changed = false;
            var pipelineAssetObject = new SerializedObject(renderPipelineAsset);

            changed |= SetBool(pipelineAssetObject, "m_RequireDepthTexture", true);
            changed |= SetBool(pipelineAssetObject, "m_RequireOpaqueTexture", true);
            changed |= SetBool(pipelineAssetObject, "m_SupportsLightCookies", true);

            if (changed)
            {
                pipelineAssetObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(renderPipelineAsset);
            }

            return changed;
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

            if (!Mathf.Approximately(camera.farClipPlane, MainCameraFarClipPlane))
            {
                camera.farClipPlane = MainCameraFarClipPlane;
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
    }
}
