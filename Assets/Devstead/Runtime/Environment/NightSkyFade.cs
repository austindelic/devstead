using UnityEngine;

namespace Devstead.Environment
{
    internal static class NightSkyFade
    {
        public static float Calculate(float sunAltitude, float hiddenSunAltitude, float fullSunAltitude)
        {
            var fade = Mathf.InverseLerp(hiddenSunAltitude, fullSunAltitude, sunAltitude);
            return Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(fade));
        }

        public static float CalculateEmissionMultiplier(float nightSkyEmissionMultiplier, float nightFade)
        {
            return nightSkyEmissionMultiplier * nightFade;
        }
    }
}
