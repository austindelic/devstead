using UnityEngine;

public partial class ScreenSpaceReflectionURP
{
    private const string BackfaceKeyword = "_BACKFACE_ENABLED";
    private const string ApproximationColorMipmapsKeyword = "_SSR_APPROX_COLOR_MIPMAPS";

    private static class ShaderIDs
    {
        public static readonly int MinSmoothness = Shader.PropertyToID("_MinSmoothness");
        public static readonly int FadeSmoothness = Shader.PropertyToID("_FadeSmoothness");
        public static readonly int EdgeFade = Shader.PropertyToID("_EdgeFade");
        public static readonly int Thickness = Shader.PropertyToID("_Thickness");
        public static readonly int StepSize = Shader.PropertyToID("_StepSize");
        public static readonly int StepSizeMultiplier = Shader.PropertyToID("_StepSizeMultiplier");
        public static readonly int MaxStep = Shader.PropertyToID("_MaxStep");
        public static readonly int DownSample = Shader.PropertyToID("_DownSample");
        public static readonly int AccumFactor = Shader.PropertyToID("_AccumulationFactor");

        public static readonly int[] GBuffer =
        {
            Shader.PropertyToID("_GBuffer0"),
            Shader.PropertyToID("_GBuffer1"),
            Shader.PropertyToID("_GBuffer2")
        };
    }

    private static void ApplyMaterialSettings(Material material, ScreenSpaceReflection volume, Resolution resolution)
    {
        SetQualitySettings(material, volume);

        material.SetFloat(ShaderIDs.MinSmoothness, volume.minSmoothness.value);
        material.SetFloat(ShaderIDs.FadeSmoothness, volume.fadeSmoothness.value <= volume.minSmoothness.value ? volume.minSmoothness.value + 0.01f : volume.fadeSmoothness.value);
        material.SetFloat(ShaderIDs.EdgeFade, volume.edgeFade.value);
        material.SetFloat(ShaderIDs.Thickness, volume.thickness.value);
        material.SetFloat(ShaderIDs.DownSample, (float)resolution * 0.25f);
    }

    private static void DisableBackfaceKeyword(Material material)
    {
        material.DisableKeyword(BackfaceKeyword);
    }

    private static void SetApproximationMipmapsKeyword(Material material, MipmapsMode mipmapsMode)
    {
        SetKeyword(material, ApproximationColorMipmapsKeyword, mipmapsMode == MipmapsMode.Trilinear);
    }

    private static void SetKeyword(Material material, string keyword, bool enabled)
    {
        if (enabled)
            material.EnableKeyword(keyword);
        else
            material.DisableKeyword(keyword);
    }

    private static void SetQualitySettings(Material material, ScreenSpaceReflection volume)
    {
        switch (volume.quality.value)
        {
            case ScreenSpaceReflection.Quality.Low:
                SetRayMarchingQuality(material, 0.4f, 1.33f, 16);
                break;
            case ScreenSpaceReflection.Quality.Medium:
                SetRayMarchingQuality(material, 0.3f, 1.33f, 32);
                break;
            case ScreenSpaceReflection.Quality.High:
                SetRayMarchingQuality(material, 0.2f, 1.33f, 64);
                break;
            default:
                SetRayMarchingQuality(material, 0.2f, 1.1f, volume.maxStep.value);
                break;
        }
    }

    private static void SetRayMarchingQuality(Material material, float stepSize, float stepSizeMultiplier, int maxStep)
    {
        material.SetFloat(ShaderIDs.StepSize, stepSize);
        material.SetFloat(ShaderIDs.StepSizeMultiplier, stepSizeMultiplier);
        material.SetFloat(ShaderIDs.MaxStep, maxStep);
    }
}
