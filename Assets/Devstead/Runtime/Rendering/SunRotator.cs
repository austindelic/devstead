using UnityEngine;

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
            if (rotationAxis.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            transform.Rotate(rotationAxis.normalized, rotationSpeedDegreesPerSecond * Time.deltaTime, Space.World);
        }
    }
}
