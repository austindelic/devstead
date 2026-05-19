using UnityEngine;
using UnityEngine.Rendering;

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
                timeOfDay = Mathf.Repeat(value, 1.0f);
                ApplySky();
            }
        }

        public float MoonTimeOffset
        {
            get => moonTimeOffset;
            set
            {
                moonTimeOffset = Mathf.Repeat(value, 1.0f);
                ApplySky();
            }
        }

        private void OnEnable()
        {
            ApplySky();
        }

        private void OnValidate()
        {
            timeOfDay = Mathf.Repeat(timeOfDay, 1.0f);
            moonTimeOffset = Mathf.Repeat(moonTimeOffset, 1.0f);
            ApplySky();
        }

        private void Update()
        {
            if (Application.isPlaying && animateInPlayMode)
            {
                timeOfDay = Mathf.Repeat(timeOfDay + sunDegreesPerSecond * Time.deltaTime / 360.0f, 1.0f);
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

            ApplyNightSkyEmission();
        }

        private static void ApplyDayArc(Light light, Vector3 baseEulerAngles, float normalizedOrbit)
        {
            if (light == null)
            {
                return;
            }

            var eulerAngles = baseEulerAngles;
            eulerAngles.x = normalizedOrbit * 360.0f - 90.0f;
            light.transform.rotation = Quaternion.Euler(eulerAngles);
        }

        private void ApplyNightSkyEmission()
        {
            if (skyVolume == null || sun == null || skyVolume.profile == null)
            {
                return;
            }

            if (!skyVolume.profile.TryGet<PhysicallyBasedSky>(out var physicallyBasedSky))
            {
                return;
            }

            var sunDirection = -sun.transform.forward;
            var sunAltitude = Vector3.Dot(sunDirection.normalized, Vector3.up);
            var fade = Mathf.InverseLerp(nightSkyHiddenSunAltitude, nightSkyFullSunAltitude, sunAltitude);
            fade = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(fade));

            physicallyBasedSky.spaceEmissionMultiplier.overrideState = true;
            physicallyBasedSky.spaceEmissionMultiplier.value = nightSkyEmissionMultiplier * fade;
        }
    }
}
