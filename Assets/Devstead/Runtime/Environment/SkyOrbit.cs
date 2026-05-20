using UnityEngine;

namespace Devstead.Environment
{
    internal static class SkyOrbit
    {
        public static Quaternion CreateDayArcRotation(Vector3 baseEulerAngles, float normalizedOrbit)
        {
            var eulerAngles = baseEulerAngles;
            eulerAngles.x = normalizedOrbit * 360.0f - 90.0f;
            return Quaternion.Euler(eulerAngles);
        }

        public static float CalculateSunAltitude(Vector3 sunForward)
        {
            var sunDirection = -sunForward;
            return Vector3.Dot(sunDirection.normalized, Vector3.up);
        }

        public static bool HasRotationAxis(Vector3 rotationAxis)
        {
            return rotationAxis.sqrMagnitude > Mathf.Epsilon;
        }

        public static Vector3 NormalizeRotationAxis(Vector3 rotationAxis)
        {
            return rotationAxis.normalized;
        }
    }
}
