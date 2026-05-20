using UnityEngine;
using Devstead.Environment;

namespace Devstead.Rendering
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Devstead/Rendering/Sun Rotator")]
    public sealed class SunRotator : MonoBehaviour
    {
        [SerializeField] private float rotationSpeedDegreesPerSecond = 10.0f;
        [SerializeField] private Vector3 rotationAxis = Vector3.up;

        private void Update()
        {
            if (!SkyOrbit.HasRotationAxis(rotationAxis))
            {
                return;
            }

            transform.Rotate(SkyOrbit.NormalizeRotationAxis(rotationAxis), rotationSpeedDegreesPerSecond * Time.deltaTime, Space.World);
        }
    }
}
