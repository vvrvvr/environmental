Shader "Custom/URP/UnlitGradientTwoSliders"
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
                float2 uv          : TEXCOORD0;
            };

            float4 _ColorA;
            float4 _ColorB;
            float4 _ColorC;

            float _BlendAB;
            float _BlendAC;

            float _SliderAB;
            float _SliderAC;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float x = IN.uv.x;

                // нормализация
                float tAB = saturate(_SliderAB / 100.0);
                float tAC = saturate(_SliderAC / 100.0);

                float wAB = _BlendAB * 0.5;
                float wAC = _BlendAC * 0.5;

                // --- этап 1: A -> B ---
                float blendAB = smoothstep(tAB - wAB, tAB + wAB, x);
                float4 colAB = lerp(_ColorA, _ColorB, blendAB);

                // --- этап 2: (A/B) -> C ---
                float blendAC = smoothstep(tAC - wAC, tAC + wAC, x);
                float4 finalCol = lerp(colAB, _ColorC, blendAC);

                return finalCol;
            }

            ENDHLSL
        }
    }
}