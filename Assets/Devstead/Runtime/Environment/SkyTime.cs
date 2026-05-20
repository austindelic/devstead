using UnityEngine;

namespace Devstead.Environment
{
    internal static class SkyTime
    {
        public static float Normalize(float normalizedTime)
        {
            return Mathf.Repeat(normalizedTime, 1.0f);
        }

        public static float AdvanceByDegrees(float normalizedTime, float degreesPerSecond, float deltaTime)
        {
            return Normalize(normalizedTime + degreesPerSecond * deltaTime / 360.0f);
        }
    }
}
