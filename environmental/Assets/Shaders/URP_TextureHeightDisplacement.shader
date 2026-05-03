// Assign a RenderTexture or Texture2D to _MainTex (Unity uses the same 2D slot for both).
Shader "Custom/URP/TextureHeightDisplacement"
{
    Properties
    {
        [NoScaleOffset]
        _MainTex ("Render texture (Texture2D also works)", 2D) = "black" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _DisplacementStrength ("Displacement strength", Range(0, 10000)) = 0.15
        _HeightCenter ("Height center luminance", Range(0, 1)) = 0.5
        _HeightSmooth ("Height smooth (less spikes within color)", Range(0, 1)) = 0
        [Toggle] _InvertHeight ("Invert height (bright vs dark relief)", Float) = 0
        _NormalFromHeightScale ("Normal from height", Range(0, 8)) = 2
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
        _Metallic ("Metallic", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                float4 _BaseColor;
                float _DisplacementStrength;
                float _HeightCenter;
                float _HeightSmooth;
                float _InvertHeight;
                float _NormalFromHeightScale;
                float _Smoothness;
                float _Metallic;
            CBUFFER_END

            float SampleHeightRaw(float2 uv)
            {
                float3 rgb = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0).rgb;
                return dot(rgb, float3(0.299, 0.587, 0.114));
            }

            float SampleHeight(float2 uv)
            {
                float raw = SampleHeightRaw(uv);
                float g = saturate(_HeightSmooth);
                float h;
                if (g <= 1e-5)
                    h = raw;
                else
                {
                    float2 ts = max(_MainTex_TexelSize.xy, float2(1e-6, 1e-6));
                    float2 r = ts * (1.0 + g * 14.0);

                    float sum = raw;
                    sum += SampleHeightRaw(uv + float2(r.x, 0));
                    sum += SampleHeightRaw(uv - float2(r.x, 0));
                    sum += SampleHeightRaw(uv + float2(0, r.y));
                    sum += SampleHeightRaw(uv - float2(0, r.y));
                    float avg = sum * (1.0 / 5.0);
                    h = lerp(raw, avg, g);
                }

                return lerp(h, 1.0 - h, saturate(_InvertHeight));
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float2 uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                float du = _MainTex_TexelSize.x > 1e-8 ? _MainTex_TexelSize.x * 2.0 : (1.0 / 512.0);

                float h0 = SampleHeight(uv);
                float hu = SampleHeight(uv + float2(du, 0));
                float hv = SampleHeight(uv + float2(0, du));

                float disp0 = (h0 - _HeightCenter) * _DisplacementStrength;
                float3 positionOS = input.positionOS.xyz + input.normalOS * disp0;

                float dhdU = (hu - h0) * _DisplacementStrength / max(du, 1e-6);
                float dhdV = (hv - h0) * _DisplacementStrength / max(du, 1e-6);

                float3 n = normalize(input.normalOS);
                float3 t = normalize(input.tangentOS.xyz);
                float3 b = cross(n, t) * input.tangentOS.w;
                float3 geomN = normalize(
                    n
                    - t * dhdU * _NormalFromHeightScale
                    - b * dhdV * _NormalFromHeightScale);

                float3 positionWS = TransformObjectToWorld(positionOS);

                output.positionHCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(geomN);
                output.uv = uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _BaseColor;

                float3 N = normalize(input.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(input.positionWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 radiance = mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half NdotL = saturate(dot(N, mainLight.direction));
                half3 diffuse = albedo.rgb * radiance * NdotL;

                half3 ambient = SampleSH(N) * albedo.rgb;

                half3 specColor = lerp(half3(0.04, 0.04, 0.04), albedo.rgb, _Metallic);
                half3 halfDir = normalize(mainLight.direction + V);
                half NdotH = saturate(dot(N, halfDir));
                half roughness = 1.0 - _Smoothness;
                half spec = pow(NdotH, (1.0 - roughness) * 64.0 + 1.0) * _Smoothness;
                half3 specular = specColor * radiance * spec * (1.0 - _Metallic);

                return half4(diffuse + ambient * 0.5 + specular, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                float4 _BaseColor;
                float _DisplacementStrength;
                float _HeightCenter;
                float _HeightSmooth;
                float _InvertHeight;
                float _NormalFromHeightScale;
                float _Smoothness;
                float _Metallic;
            CBUFFER_END

            float ShadowSampleHeightRaw(float2 uv)
            {
                float3 rgb = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0).rgb;
                return dot(rgb, float3(0.299, 0.587, 0.114));
            }

            float ShadowSampleHeight(float2 uv)
            {
                float raw = ShadowSampleHeightRaw(uv);
                float g = saturate(_HeightSmooth);
                float h;
                if (g <= 1e-5)
                    h = raw;
                else
                {
                    float2 ts = max(_MainTex_TexelSize.xy, float2(1e-6, 1e-6));
                    float2 r = ts * (1.0 + g * 14.0);
                    float sum = raw;
                    sum += ShadowSampleHeightRaw(uv + float2(r.x, 0));
                    sum += ShadowSampleHeightRaw(uv - float2(r.x, 0));
                    sum += ShadowSampleHeightRaw(uv + float2(0, r.y));
                    sum += ShadowSampleHeightRaw(uv - float2(0, r.y));
                    float avg = sum * (1.0 / 5.0);
                    h = lerp(raw, avg, g);
                }

                return lerp(h, 1.0 - h, saturate(_InvertHeight));
            }

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float2 uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                float h0 = ShadowSampleHeight(uv);
                float disp0 = (h0 - _HeightCenter) * _DisplacementStrength;
                float3 positionOS = input.positionOS.xyz + input.normalOS * disp0;
                float3 positionWS = TransformObjectToWorld(positionOS);
                float4 positionCS = TransformWorldToHClip(positionWS);

                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
