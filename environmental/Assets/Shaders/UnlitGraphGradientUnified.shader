Shader "Custom/URP/UnlitGraphGradientUnified"
{
    Properties
    {
        [Header(Graph shape)]
        _GraphVisualMode ("Visual mode Line0 Radial1", Float) = 0

        _ColorA ("Color A", Color) = (1,1,1,1)
        _ColorB ("Color B", Color) = (1,0,0,1)
        _ColorC ("Color C", Color) = (0,0,1,1)

        _BlendAB ("Blend A to B Width", Range(0.001, 1)) = 0.1
        _BlendAC ("Blend to C Width", Range(0.001, 1)) = 0.1

        _SliderAB ("Slider AB 0 to 100", Range(0, 100)) = 0
        _SliderAC ("Slider AC 0 to 100", Range(0, 100)) = 0

        [Header(Texture)]
        _MainTex ("Texture", 2D) = "white" {}

        [Header(Line ribbon vignette UV)]
        _EdgeVignetteStrength ("Edge darken amount", Range(0, 1)) = 0.35
        _EdgeVignetteSoftness ("Edge falloff ribbon center", Range(0.001, 1)) = 0.25
        _EndsVignetteStrength ("Ends darken amount", Range(0, 1)) = 0
        _EndsVignetteSoftness ("Ends falloff segment center", Range(0.001, 1)) = 0.15

        [Header(Radial sprite uses same Edge params above)]

        [Header(Radial joint edge under sprite)]
        _RadialJointRimSuppress ("Suppress rim where edge draws first", Range(0, 1)) = 1
        _RadialJointGapSign ("Behind gap sign", Range(-1, 1)) = 1
        _RadialBehindMinGap ("Behind min gap eyespace", Range(0, 0.05)) = 0.0001
        _RadialBehindFade ("Joint ramp in eyespace width", Range(0.0001, 0.25)) = 0.02
        _RadialBehindGapMax ("Behind gap cutoff eyespace", Range(0.01, 5)) = 0.85

        [Header(Graph unified screen rim)]
        _GraphUnifiedBlend ("Blend unified screen rim", Range(0, 1)) = 0
        _GraphSsStrength ("Unified rim strength", Range(0, 5)) = 0.35
        _GraphSsPixelRadius ("Screen sample radius px", Range(0.5, 8)) = 1.8
        _GraphSsDepthSens ("Depth edge sensitivity", Range(0, 2000)) = 120
        _GraphSsColorSens ("Color edge sensitivity opaque", Range(0, 100)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define GRAPH_UNIFIED_DECLARE_OPAQUE 1
            #include "GraphUnifiedScreenVignette.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uvPrim      : TEXCOORD0;
                float2 uvTex       : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorA;
                float4 _ColorB;
                float4 _ColorC;
                float4 _MainTex_ST;
                float _GraphVisualMode;
                float _BlendAB;
                float _BlendAC;
                float _SliderAB;
                float _SliderAC;
                float _EdgeVignetteStrength;
                float _EdgeVignetteSoftness;
                float _EndsVignetteStrength;
                float _EndsVignetteSoftness;
                float _RadialJointRimSuppress;
                float _RadialJointGapSign;
                float _RadialBehindMinGap;
                float _RadialBehindFade;
                float _RadialBehindGapMax;
                float _GraphUnifiedBlend;
                float _GraphSsStrength;
                float _GraphSsPixelRadius;
                float _GraphSsDepthSens;
                float _GraphSsColorSens;
            CBUFFER_END

            float EdgeRibbonMask(float t01)
            {
                return saturate(min(t01, 1.0 - t01) * 2.0);
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uvPrim = IN.uv;
                OUT.uvTex = IN.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                const bool radial = _GraphVisualMode > 0.5f;

                float xAlong;
                if (radial)
                {
                    float2 fromCenter = (IN.uvPrim - float2(0.5, 0.5)) * 2.0;
                    float rr = length(fromCenter);
                    xAlong = saturate(rr * 0.70710678118);
                }
                else
                {
                    xAlong = IN.uvPrim.x;
                }

                float tAB = saturate(_SliderAB / 100.0);
                float tAC = saturate(_SliderAC / 100.0);

                float wAB = _BlendAB * 0.5;
                float wAC = _BlendAC * 0.5;

                float blendAB = smoothstep(tAB - wAB, tAB + wAB, xAlong);
                float4 colAB = lerp(_ColorA, _ColorB, blendAB);

                float blendAC = smoothstep(tAC - wAC, tAC + wAC, xAlong);
                float4 finalCol = lerp(colAB, _ColorC, blendAC);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uvTex);
                finalCol *= tex;

                half uvVig;
                if (radial)
                {
                    float mBright = saturate(1.0 - xAlong);
                    uvVig = (half)lerp(1.0 - _EdgeVignetteStrength, 1.0, smoothstep(0.0, _EdgeVignetteSoftness, mBright));
                }
                else
                {
                    float mEdge = EdgeRibbonMask(IN.uvPrim.y);
                    float vEdgeLine = lerp(1.0 - _EdgeVignetteStrength, 1.0, smoothstep(0.0, _EdgeVignetteSoftness, mEdge));
                    float mEnds = EdgeRibbonMask(IN.uvPrim.x);
                    float vEnds = lerp(1.0 - _EndsVignetteStrength, 1.0, smoothstep(0.0, _EndsVignetteSoftness, mEnds));
                    uvVig = (half)(vEdgeLine * vEnds);
                }

                // Radial joint: depth buffer already has edges if they render before this sprite. Remove rim where a close surface sits behind (the edge).
                if (radial && _RadialJointRimSuppress > 1e-3f)
                {
                    float2 screenUV = IN.positionHCS.xy * _ScreenSize.zw;
                    float myRaw = IN.positionHCS.z * rcp(IN.positionHCS.w);
                    float myEye = LinearEyeDepth(myRaw, _ZBufferParams);
                    float storEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    float gap = (storEye - myEye) * _RadialJointGapSign;
                    float gmin = max(_RadialBehindMinGap, 1e-6f);
                    float gin = max(_RadialBehindFade, 1e-5f);
                    float gFar = max(_RadialBehindGapMax, gmin + gin * 4.0f);
                    float rampIn = smoothstep(gmin, gmin + gin, gap);
                    float rampOut = 1.0f - smoothstep(gFar - gin * 2.0f, gFar + 1e-4f, gap);
                    half jointBlend = saturate((half)(rampIn * rampOut * _RadialJointRimSuppress));
                    uvVig = lerp(uvVig, 1.0h, jointBlend);
                }

                half ssVig = 1.0h;
                if (_GraphUnifiedBlend > 1e-3 && _GraphSsStrength > 1e-3)
                {
                    ssVig = GraphUnifiedScreenVignetteMultiplier(
                        IN.positionHCS,
                        (half)_GraphSsStrength,
                        (half)_GraphSsPixelRadius,
                        (half)_GraphSsDepthSens,
                        (half)_GraphSsColorSens);
                }
                half vig = lerp(uvVig, ssVig, (half)_GraphUnifiedBlend);
                finalCol.rgb *= vig;

                return half4(finalCol);
            }

            ENDHLSL
        }
    }

    Fallback Off
}
