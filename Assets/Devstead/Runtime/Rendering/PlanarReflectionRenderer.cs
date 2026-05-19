using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Devstead.Rendering
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Devstead/Rendering/Planar Reflection Renderer")]
    public sealed class PlanarReflectionRenderer : MonoBehaviour
    {
        public const string ReflectionCameraName = "Devstead Planar Reflection Camera";

        [SerializeField] private float seaLevel = 0.0f;
        [Range(0.1f, 1.0f)]
        [SerializeField] private float resolutionScale = 0.5f;
        [SerializeField] private int maxTextureSize = 1024;
        [SerializeField] private float clipPlaneOffset = 0.07f;
        [SerializeField] private bool renderSceneView = true;

        private static readonly int PlanarReflectionTexture = Shader.PropertyToID("_DevsteadPlanarReflectionTexture");
        private static readonly int PlanarReflectionViewProjection = Shader.PropertyToID("_DevsteadPlanarReflectionVP");
        private static readonly int PlanarReflectionEnabled = Shader.PropertyToID("_DevsteadPlanarReflectionEnabled");

        private Camera reflectionCamera;
        private RenderTexture reflectionTexture;
        private bool isRenderingReflection;

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += RenderReflection;
            Shader.SetGlobalFloat(PlanarReflectionEnabled, 0.0f);
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= RenderReflection;
            Shader.SetGlobalFloat(PlanarReflectionEnabled, 0.0f);
            ReleaseResources();
        }

        private void OnValidate()
        {
            resolutionScale = Mathf.Clamp(resolutionScale, 0.1f, 1.0f);
            maxTextureSize = Mathf.Max(64, maxTextureSize);
            clipPlaneOffset = Mathf.Max(0.0f, clipPlaneOffset);
        }

        private void RenderReflection(ScriptableRenderContext context, Camera sourceCamera)
        {
            if (!isActiveAndEnabled || isRenderingReflection || sourceCamera == null || !ShouldRender(sourceCamera))
            {
                return;
            }

            EnsureReflectionCamera();
            EnsureReflectionTexture(sourceCamera);
            ConfigureReflectionCamera(sourceCamera);
            reflectionCamera.targetTexture = reflectionTexture;

            isRenderingReflection = true;
            var previousInvertCulling = GL.invertCulling;
            GL.invertCulling = !previousInvertCulling;

            try
            {
#pragma warning disable CS0618
                UniversalRenderPipeline.RenderSingleCamera(context, reflectionCamera);
#pragma warning restore CS0618
            }
            finally
            {
                GL.invertCulling = previousInvertCulling;
                isRenderingReflection = false;
            }

            var viewProjection = GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, true) * reflectionCamera.worldToCameraMatrix;
            Shader.SetGlobalTexture(PlanarReflectionTexture, reflectionTexture);
            Shader.SetGlobalMatrix(PlanarReflectionViewProjection, viewProjection);
            Shader.SetGlobalFloat(PlanarReflectionEnabled, 1.0f);
        }

        private bool ShouldRender(Camera sourceCamera)
        {
            if (!sourceCamera.enabled || sourceCamera.cameraType == CameraType.Preview)
            {
                return false;
            }

            if (sourceCamera.name.StartsWith(ReflectionCameraName, System.StringComparison.Ordinal))
            {
                return false;
            }

            if (sourceCamera.cameraType == CameraType.SceneView)
            {
                return renderSceneView;
            }

            return sourceCamera.cameraType == CameraType.Game;
        }

        private void EnsureReflectionCamera()
        {
            if (reflectionCamera != null)
            {
                return;
            }

            var cameraObject = new GameObject(ReflectionCameraName)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            reflectionCamera = cameraObject.AddComponent<Camera>();
            reflectionCamera.enabled = false;

            var additionalCameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            additionalCameraData.renderPostProcessing = false;
            additionalCameraData.allowXRRendering = false;
        }

        private void EnsureReflectionTexture(Camera sourceCamera)
        {
            var width = Mathf.Clamp(Mathf.RoundToInt(sourceCamera.pixelWidth * resolutionScale), 64, maxTextureSize);
            var height = Mathf.Clamp(Mathf.RoundToInt(sourceCamera.pixelHeight * resolutionScale), 64, maxTextureSize);

            if (reflectionTexture != null && reflectionTexture.width == width && reflectionTexture.height == height)
            {
                return;
            }

            if (reflectionTexture != null)
            {
                reflectionTexture.Release();
                DestroyObject(reflectionTexture);
            }

            reflectionTexture = new RenderTexture(width, height, 16, RenderTextureFormat.DefaultHDR)
            {
                name = "Devstead Planar Reflection Texture",
                hideFlags = HideFlags.HideAndDontSave,
                useMipMap = false,
                autoGenerateMips = false
            };

            reflectionTexture.Create();
        }

        private void ConfigureReflectionCamera(Camera sourceCamera)
        {
            reflectionCamera.CopyFrom(sourceCamera);
            reflectionCamera.enabled = false;
            reflectionCamera.targetTexture = null;
            reflectionCamera.name = ReflectionCameraName;
            reflectionCamera.transform.SetPositionAndRotation(sourceCamera.transform.position, sourceCamera.transform.rotation);
            reflectionCamera.cullingMask = sourceCamera.cullingMask;
            reflectionCamera.clearFlags = sourceCamera.clearFlags;
            reflectionCamera.backgroundColor = sourceCamera.backgroundColor;
            reflectionCamera.allowMSAA = false;
            reflectionCamera.allowHDR = sourceCamera.allowHDR;

            var plane = new Vector4(0.0f, 1.0f, 0.0f, -seaLevel);
            var reflectionMatrix = CalculateReflectionMatrix(plane);
            var sourcePosition = sourceCamera.transform.position;
            var reflectedPosition = reflectionMatrix.MultiplyPoint(sourcePosition);
            var reflectedForward = reflectionMatrix.MultiplyVector(sourceCamera.transform.forward);
            var reflectedUp = reflectionMatrix.MultiplyVector(sourceCamera.transform.up);

            reflectionCamera.transform.SetPositionAndRotation(reflectedPosition, Quaternion.LookRotation(reflectedForward, reflectedUp));
            reflectionCamera.worldToCameraMatrix = sourceCamera.worldToCameraMatrix * reflectionMatrix;

            var clipPlane = CameraSpacePlane(reflectionCamera, new Vector3(0.0f, seaLevel, 0.0f), Vector3.up, 1.0f);
            reflectionCamera.projectionMatrix = sourceCamera.CalculateObliqueMatrix(clipPlane);
        }

        private Vector4 CameraSpacePlane(Camera camera, Vector3 position, Vector3 normal, float sideSign)
        {
            var offsetPosition = position + normal * clipPlaneOffset;
            var worldToCamera = camera.worldToCameraMatrix;
            var cameraPosition = worldToCamera.MultiplyPoint(offsetPosition);
            var cameraNormal = worldToCamera.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(cameraNormal.x, cameraNormal.y, cameraNormal.z, -Vector3.Dot(cameraPosition, cameraNormal));
        }

        private static Matrix4x4 CalculateReflectionMatrix(Vector4 plane)
        {
            var reflection = Matrix4x4.identity;

            reflection.m00 = 1.0f - 2.0f * plane.x * plane.x;
            reflection.m01 = -2.0f * plane.x * plane.y;
            reflection.m02 = -2.0f * plane.x * plane.z;
            reflection.m03 = -2.0f * plane.w * plane.x;

            reflection.m10 = -2.0f * plane.y * plane.x;
            reflection.m11 = 1.0f - 2.0f * plane.y * plane.y;
            reflection.m12 = -2.0f * plane.y * plane.z;
            reflection.m13 = -2.0f * plane.w * plane.y;

            reflection.m20 = -2.0f * plane.z * plane.x;
            reflection.m21 = -2.0f * plane.z * plane.y;
            reflection.m22 = 1.0f - 2.0f * plane.z * plane.z;
            reflection.m23 = -2.0f * plane.w * plane.z;

            return reflection;
        }

        private void ReleaseResources()
        {
            if (reflectionCamera != null)
            {
                DestroyObject(reflectionCamera.gameObject);
                reflectionCamera = null;
            }

            if (reflectionTexture != null)
            {
                reflectionTexture.Release();
                DestroyObject(reflectionTexture);
                reflectionTexture = null;
            }
        }

        private static void DestroyObject(Object target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
