using UnityEngine;
using UnityEngine.Rendering;
using Devstead.Environment;

namespace Devstead.Rendering
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Devstead/Rendering/Sky Controller")]
    public sealed class SkyController : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private Light sun;
        [SerializeField] private Light moon;
        [SerializeField] private Volume skyVolume;

        [Header("Time")]
        [Range(0.0f, 1.0f)]
        [SerializeField] private float timeOfDay = 0.375f;
        [Range(0.0f, 1.0f)]
        [SerializeField] private float moonTimeOffset = 0.5f;
        [SerializeField] private bool animateInPlayMode = true;
        [SerializeField] private float sunDegreesPerSecond = 10.0f;

        [Header("Orbits")]
        [SerializeField] private Vector3 sunBaseEulerAngles = new(45.0f, -30.0f, 0.0f);
        [SerializeField] private Vector3 moonBaseEulerAngles = new(225.0f, 150.0f, 0.0f);

        [Header("Night Sky")]
        [SerializeField] private float nightSkyFullSunAltitude = -0.12f;
        [SerializeField] private float nightSkyHiddenSunAltitude = -0.02f;
        [SerializeField] private float nightSkyEmissionMultiplier = 0.45f;

        public Light Sun => sun;
        public Light Moon => moon;
        public Volume SkyVolume => skyVolume;
        public float TimeOfDay
        {
            get => timeOfDay;
            set
            {
                timeOfDay = SkyTime.Normalize(value);
                ApplySky();
            }
        }

        public float MoonTimeOffset
        {
            get => moonTimeOffset;
            set
            {
                moonTimeOffset = SkyTime.Normalize(value);
                ApplySky();
            }
        }

        private void OnEnable()
        {
            ApplySky();
        }

        private void OnDisable()
        {
            Shader.SetGlobalFloat(DevsteadShaderPropertyIds.NightWaterFactor, 0.0f);
        }

        private void OnValidate()
        {
            timeOfDay = SkyTime.Normalize(timeOfDay);
            moonTimeOffset = SkyTime.Normalize(moonTimeOffset);
            ApplySky();
        }

        private void Update()
        {
            if (Application.isPlaying && animateInPlayMode)
            {
                timeOfDay = SkyTime.AdvanceByDegrees(timeOfDay, sunDegreesPerSecond, Time.deltaTime);
            }

            ApplySky();
        }

        public void ApplySky()
        {
            ApplyDayArc(sun, sunBaseEulerAngles, timeOfDay);
            ApplyDayArc(moon, moonBaseEulerAngles, timeOfDay + moonTimeOffset);

            if (sun != null && RenderSettings.sun != sun)
            {
                RenderSettings.sun = sun;
            }

            var nightFade = CalculateNightFade();
            Shader.SetGlobalFloat(DevsteadShaderPropertyIds.NightWaterFactor, nightFade);
            ApplyNightSkyEmission(nightFade);
        }

        private static void ApplyDayArc(Light light, Vector3 baseEulerAngles, float normalizedOrbit)
        {
            if (light == null)
            {
                return;
            }

            light.transform.rotation = SkyOrbit.CreateDayArcRotation(baseEulerAngles, normalizedOrbit);
        }

        private float CalculateNightFade()
        {
            if (sun == null)
            {
                return 0.0f;
            }

            var sunAltitude = SkyOrbit.CalculateSunAltitude(sun.transform.forward);
            return NightSkyFade.Calculate(sunAltitude, nightSkyHiddenSunAltitude, nightSkyFullSunAltitude);
        }

        private void ApplyNightSkyEmission(float nightFade)
        {
            if (skyVolume == null || skyVolume.profile == null)
            {
                return;
            }

            if (!skyVolume.profile.TryGet<PhysicallyBasedSky>(out var physicallyBasedSky))
            {
                return;
            }

            physicallyBasedSky.spaceEmissionMultiplier.overrideState = true;
            physicallyBasedSky.spaceEmissionMultiplier.value = NightSkyFade.CalculateEmissionMultiplier(nightSkyEmissionMultiplier, nightFade);
        }
    }
}
