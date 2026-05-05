#ifndef GRAPH_UNIFIED_SCREEN_VIGNETTE_INCLUDED
#define GRAPH_UNIFIED_SCREEN_VIGNETTE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

#ifndef GRAPH_UNIFIED_DECLARE_OPAQUE
#define GRAPH_UNIFIED_DECLARE_OPAQUE 0
#endif

#if GRAPH_UNIFIED_DECLARE_OPAQUE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#endif

// Requires URP Renderer: Depth Texture On.
// Optional: Opaque Texture On + GRAPH_UNIFIED_DECLARE_OPAQUE 1 + colorSensitivity for near-coplanar geometry.

half GraphLuminanceGraphUv(half3 rgb)
{
    return dot(rgb, half3(0.299h, 0.587h, 0.114h));
}

// 1 = no darken; lower = darker (like uv-rim).
half GraphUnifiedScreenVignetteMultiplier(
    float4 positionHCS,
    half ssStrength,
    half pixelRadius,
    half depthSensitivity,
    half colorSensitivity)
{
    if (ssStrength <= 1e-4h)
        return 1.0h;

    float2 screenUV = positionHCS.xy * _ScreenSize.zw;

    float dC = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);

    float2 o = float2(_ScreenSize.z, _ScreenSize.w) * max(0.5f, (float)pixelRadius);
    float dL = LinearEyeDepth(SampleSceneDepth(screenUV + float2(-o.x, 0)), _ZBufferParams);
    float dR = LinearEyeDepth(SampleSceneDepth(screenUV + float2(o.x, 0)), _ZBufferParams);
    float dD = LinearEyeDepth(SampleSceneDepth(screenUV + float2(0, -o.y)), _ZBufferParams);
    float dU = LinearEyeDepth(SampleSceneDepth(screenUV + float2(0, o.y)), _ZBufferParams);

    half rel = (half)(max(max(abs(dR - dC), abs(dL - dC)), max(abs(dU - dC), abs(dD - dC))) / max(dC, 1e-3f));
    half depthEdge = saturate(rel * (half)depthSensitivity);

#if GRAPH_UNIFIED_DECLARE_OPAQUE
    half lC = GraphLuminanceGraphUv(SampleSceneColor(screenUV).rgb);
    half lL = GraphLuminanceGraphUv(SampleSceneColor(screenUV + float2(-o.x, 0)).rgb);
    half lR = GraphLuminanceGraphUv(SampleSceneColor(screenUV + float2(o.x, 0)).rgb);
    half lD = GraphLuminanceGraphUv(SampleSceneColor(screenUV + float2(0, -o.y)).rgb);
    half lU = GraphLuminanceGraphUv(SampleSceneColor(screenUV + float2(0, o.y)).rgb);
    half colEdge = saturate(max(max(abs(lR - lC), abs(lL - lC)), max(abs(lU - lC), abs(lD - lC))) * colorSensitivity);
#else
    half colEdge = 0.0h;
#endif

    half boundary = saturate(max(depthEdge, colEdge));
    // ssStrength may be >1 (inspector range); clamp multiplier so color does not go negative.
    return saturate(lerp(1.0h - ssStrength, 1.0h, 1.0h - boundary));
}

#endif
