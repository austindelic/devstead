using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

[Flags]
internal enum SSGIRuntimeLogFlags
{
    None = 0,
    ShaderMismatch = 1 << 0,
    RenderingDebuggerDisabled = 1 << 1,
    BackfaceLightingDeferred = 1 << 2
}

internal static class SSGILogging
{
    public static void LogOnce(ref SSGIRuntimeLogFlags logFlags, SSGIRuntimeLogFlags flag, string message, bool error = false)
    {
        if ((logFlags & flag) != 0)
            return;

        if (error)
            Debug.LogError(message);
        else
            Debug.Log(message);

        logFlags |= flag;
    }

    public static void Clear(ref SSGIRuntimeLogFlags logFlags, SSGIRuntimeLogFlags flag)
    {
        logFlags &= ~flag;
    }
}

internal static class SSGIURPPrivateAccess
{
    private const string k_GBufferPassField = "m_GBufferPass";
    private const string k_MotionVectorPassField = "m_MotionVectorPass";
    private const string k_MotionColorHandleField = "m_Color";
    private const string k_MotionDepthHandleField = "m_Depth";

    private static readonly FieldInfo s_GBufferPassFieldInfo = typeof(UniversalRenderer).GetField(k_GBufferPassField, BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo s_MotionVectorPassFieldInfo = typeof(UniversalRenderer).GetField(k_MotionVectorPassField, BindingFlags.NonPublic | BindingFlags.Instance);

    public static bool IsDeferredPathActive(ScriptableRenderer renderer)
    {
        // URP allocates m_GBufferPass for Deferred, but OpenGL backends are forced through Forward.
        return !IsOpenGLDevice() && GetGBufferPass(renderer) != null;
    }

    public static bool TryGetMotionVectorTargets(ScriptableRenderer renderer, out RTHandle colorHandle, out RTHandle depthHandle)
    {
        colorHandle = null;
        depthHandle = null;

        object motionVectorPass = GetMotionVectorPass(renderer);
        if (motionVectorPass == null)
            return false;

        Type motionVectorPassType = motionVectorPass.GetType();
        FieldInfo colorFieldInfo = motionVectorPassType.GetField(k_MotionColorHandleField, BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo depthFieldInfo = motionVectorPassType.GetField(k_MotionDepthHandleField, BindingFlags.NonPublic | BindingFlags.Instance);

        colorHandle = colorFieldInfo?.GetValue(motionVectorPass) as RTHandle;
        depthHandle = depthFieldInfo?.GetValue(motionVectorPass) as RTHandle;

        return colorHandle != null && depthHandle != null;
    }

    private static object GetGBufferPass(ScriptableRenderer renderer)
    {
        var universalRenderer = renderer as UniversalRenderer;
        if (universalRenderer == null || s_GBufferPassFieldInfo == null)
            return null;

        return s_GBufferPassFieldInfo.GetValue(universalRenderer);
    }

    private static object GetMotionVectorPass(ScriptableRenderer renderer)
    {
        var universalRenderer = renderer as UniversalRenderer;
        if (universalRenderer == null || s_MotionVectorPassFieldInfo == null)
            return null;

        return s_MotionVectorPassFieldInfo.GetValue(universalRenderer);
    }

    private static bool IsOpenGLDevice()
    {
        return SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore;
    }
}
