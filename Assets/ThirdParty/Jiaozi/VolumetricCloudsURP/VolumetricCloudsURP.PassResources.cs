using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public partial class VolumetricCloudsURP
{
    public partial class VolumetricCloudsPass
    {
        private const RenderTextureFormat CloudsLightingFormat = RenderTextureFormat.ARGBHalf;
        private const RenderTextureFormat CloudsDepthFormat = RenderTextureFormat.RFloat;

        private static RenderTextureDescriptor CreateBaseCloudsDescriptor(RenderTextureDescriptor cameraDescriptor)
        {
            cameraDescriptor.msaaSamples = 1;
            cameraDescriptor.useMipMap = false;
            cameraDescriptor.depthBufferBits = 0;
            return cameraDescriptor;
        }

        private static RenderTextureDescriptor CreateCloudsDescriptor(RenderTextureDescriptor cameraDescriptor, RenderTextureFormat colorFormat)
        {
            RenderTextureDescriptor desc = CreateBaseCloudsDescriptor(cameraDescriptor);
            desc.colorFormat = colorFormat;
            return desc;
        }

        private RenderTextureDescriptor CreateScaledCloudsDescriptor(RenderTextureDescriptor cameraDescriptor, RenderTextureFormat colorFormat)
        {
            RenderTextureDescriptor desc = CreateCloudsDescriptor(cameraDescriptor, colorFormat);
            desc.width = (int)(desc.width * resolutionScale);
            desc.height = (int)(desc.height * resolutionScale);
            return desc;
        }

        private static void ReAllocateTexture(ref RTHandle handle, RenderTextureDescriptor desc, FilterMode filterMode, string name)
        {
        #if UNITY_6000_0_OR_NEWER
            RenderingUtils.ReAllocateHandleIfNeeded(ref handle, desc, filterMode, TextureWrapMode.Clamp, name: name);
        #else
            RenderingUtils.ReAllocateIfNeeded(ref handle, desc, filterMode, TextureWrapMode.Clamp, name: name);
        #endif
        }
    }

    public partial class VolumetricCloudsAmbientPass
    {
        private static RenderTextureDescriptor CreateAmbientProbeDescriptor(RenderTextureDescriptor cameraDescriptor, bool clearDepthStencilFormat)
        {
            cameraDescriptor.msaaSamples = 1;
            cameraDescriptor.useMipMap = true;
            cameraDescriptor.autoGenerateMips = true;
            cameraDescriptor.width = 16;
            cameraDescriptor.height = 16;
            cameraDescriptor.dimension = TextureDimension.Cube;
            if (clearDepthStencilFormat)
                cameraDescriptor.depthStencilFormat = GraphicsFormat.None;
            cameraDescriptor.depthBufferBits = 0;
            return cameraDescriptor;
        }

        private static void ReAllocateAmbientProbe(ref RTHandle handle, RenderTextureDescriptor desc)
        {
        #if UNITY_6000_0_OR_NEWER
            RenderingUtils.ReAllocateHandleIfNeeded(ref handle, desc, FilterMode.Trilinear, TextureWrapMode.Clamp, name: _VolumetricCloudsAmbientProbe);
        #else
            RenderingUtils.ReAllocateIfNeeded(ref handle, desc, FilterMode.Trilinear, TextureWrapMode.Clamp, name: _VolumetricCloudsAmbientProbe);
        #endif
        }
    }

    public partial class VolumetricCloudsShadowsPass
    {
        private static GraphicsFormat GetShadowCookieFormat()
        {
            GraphicsFormat cookieFormat = GraphicsFormat.R16_UNorm; // option 2: R8_UNorm
        #if UNITY_2023_2_OR_NEWER
            bool useSingleChannel = SystemInfo.IsFormatSupported(cookieFormat, GraphicsFormatUsage.Render);
        #else
            bool useSingleChannel = SystemInfo.IsFormatSupported(cookieFormat, FormatUsage.Render);
        #endif
            return useSingleChannel ? cookieFormat : GraphicsFormat.B10G11R11_UFloatPack32;
        }

        private static RenderTextureDescriptor CreateShadowCookieDescriptor(RenderTextureDescriptor cameraDescriptor, int shadowResolution, GraphicsFormat cookieFormat)
        {
            cameraDescriptor.msaaSamples = 1;
            cameraDescriptor.depthBufferBits = 0;
            cameraDescriptor.useMipMap = false;
            cameraDescriptor.graphicsFormat = cookieFormat;
            cameraDescriptor.height = shadowResolution;
            cameraDescriptor.width = shadowResolution;
            cameraDescriptor.dimension = TextureDimension.Tex2D;
            return cameraDescriptor;
        }

        private static void ReAllocateShadowTexture(ref RTHandle handle, RenderTextureDescriptor desc, string name)
        {
        #if UNITY_6000_0_OR_NEWER
            RenderingUtils.ReAllocateHandleIfNeeded(ref handle, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: name);
        #else
            RenderingUtils.ReAllocateIfNeeded(ref handle, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: name);
        #endif
        }
    }
}
