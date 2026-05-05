Shader "Custom/URP/UnlitGradientRadialTwoSliders"
{
    Properties
    {
        _ColorA ("Color A", Color) = (1,1,1,1)
        _ColorB ("Color B", Color) = (1,0,0,1)
        _ColorC ("Color C", Color) = (0,0,1,1)

        _BlendAB ("Blend A->B Width", Range(0.001, 1)) = 0.1
        _BlendAC ("Blend ->C Width", Range(0.001, 1)) = 0.1

        _SliderAB ("Slider A->B (0-100)", Range(0, 100)) = 0
        _SliderAC ("Slider ->C (0-100)", Range(0, 100)) = 0

        [Header(Texture)]
        _MainTex ("Texture", 2D) = "white" {}

        [Header(Circular UV vignette)]
        _EdgeVignetteStrength ("Darken toward sprite rim", Range(0, 1)) = 0.35
        _EdgeVignetteSoftness ("Falloff from center (radial UV)", Range(0.001, 1)) = 0.25
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uvMesh      : TEXCOORD0;
                float2 uvTex       : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorA;
                float4 _ColorB;
                float4 _ColorC;
                float4 _MainTex_ST;
                float _BlendAB;
                float _BlendAC;
                float _SliderAB;
                float _SliderAC;
                float _EdgeVignetteStrength;
                float _EdgeVignetteSoftness;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uvMesh = IN.uv;
                OUT.uvTex = IN.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 fromCenter = (IN.uvMesh - float2(0.5, 0.5)) * 2.0;
                float r = length(fromCenter);
                float x = saturate(r * 0.70710678118);

                float tAB = saturate(_SliderAB / 100.0);
                float tAC = saturate(_SliderAC / 100.0);

                float wAB = _BlendAB * 0.5;
                float wAC = _BlendAC * 0.5;

                float blendAB = smoothstep(tAB - wAB, tAB + wAB, x);
                float4 colAB = lerp(_ColorA, _ColorB, blendAB);

                float blendAC = smoothstep(tAC - wAC, tAC + wAC, x);
                float4 finalCol = lerp(colAB, _ColorC, blendAC);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uvTex);
                finalCol *= tex;

                float mBright = saturate(1.0 - x);
                float vEdge = lerp(1.0 - _EdgeVignetteStrength, 1.0, smoothstep(0.0, _EdgeVignetteSoftness, mBright));
                finalCol.rgb *= vEdge;

                return finalCol;
            }

            ENDHLSL
        }
    }
}
