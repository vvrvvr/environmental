// Датамош-переход для рёбер без ролика (см. EdgeDatamoshTransition.cs).
// Используется только через Graphics.Blit — в сцене материалы с этим шейдером не нужны.
//
// Pass 0 Flow      : поле движения следующего ролика (Lucas–Kanade по блокам), RG = смещение в UV за кадр.
// Pass 1 Mosh      : шаг «P-кадра»: предыдущий мош тащится по полю движения блоками + residual;
//                    блоки по одному «прорастают» первым кадром следующего ролика (I-блоки) и дальше тоже тащатся.
// Pass 2 Composite : мош → итоговая render texture, в конце — сведение к чистому первому кадру.
// Pass 3 FitCopy   : копия кадра видео в RT с теми же правилами пропорций, что у VideoPlayer (Aspect Ratio).
Shader "Hidden/Environmental/EdgeDatamosh"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        sampler2D _CurTex;
        sampler2D _PrevTex;
        sampler2D _FlowTex;
        sampler2D _TargetTex;

        float2 _BlockCount;      // число блоков по X/Y
        float  _FlowStrength;    // множитель смещения
        float  _MaxFlow;         // ограничение длины смещения (UV за кадр)
        float  _Residual;        // доля разницы кадров следующего ролика, добавляемая к мошу
        float  _Refresh;         // доля блоков, уже «проросших» первым кадром (0..1)
        float  _RefreshPrev;     // то же на прошлом шаге
        float  _GlitchChance;    // шанс случайного I-блока за шаг
        float  _Seed;
        float  _Settle;          // 0..1 сведение к чистому _TargetTex
        float4 _FitScaleOffset;  // xy = scale, zw = offset (UV RT → UV видео)
        float  _VoronoiCells;    // число ячеек «клоков» по ширине кадра
        float  _VoronoiSpread;   // доля порога от расстояния до центра ячейки (рост клока от центра к краю)
        float  _VoronoiJitter;   // разброс порога по блокам (рваная кромка клока)
        float  _VoronoiSeed;     // случайный на каждый переход
        float  _Aspect;          // ширина / высота кадра
        float  _FlipY;

        float Luma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

        float Hash12(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float2 Hash22(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.xx + p3.yz) * p3.zy);
        }

        // Расстояние до ближайшей точки Вороного (F1) и id её ячейки.
        float VoronoiF1(float2 p, float seed, out float2 cellId)
        {
            float2 ip = floor(p);
            float2 fp = frac(p);
            float best = 8.0;
            cellId = ip;
            [unroll]
            for (int y = -1; y <= 1; y++)
            {
                [unroll]
                for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 o = Hash22(ip + g + seed);
                    float2 r = g + o - fp;
                    float d = dot(r, r);
                    if (d < best)
                    {
                        best = d;
                        cellId = ip + g;
                    }
                }
            }
            return sqrt(best);
        }

        float LumaCur(float2 uv)  { return Luma(tex2Dlod(_CurTex,  float4(uv, 0, 0)).rgb); }
        float LumaPrev(float2 uv) { return Luma(tex2Dlod(_PrevTex, float4(uv, 0, 0)).rgb); }
        ENDCG

        // 0 — Flow
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0

            float4 frag(v2f_img i) : SV_Target
            {
                float2 blockUv = 1.0 / _BlockCount;
                // Шаг производной ~ полблока: ловит смещения порядка размера блока за кадр.
                float2 h = blockUv * 0.5;

                float a11 = 1e-4, a12 = 0, a22 = 1e-4, b1 = 0, b2 = 0;

                [unroll]
                for (int y = 0; y < 5; y++)
                {
                    [unroll]
                    for (int x = 0; x < 5; x++)
                    {
                        float2 p = i.uv + (float2(x, y) / 4.0 - 0.5) * blockUv * 1.5;
                        // Производные в единицах «на шаг h» — так система хорошо обусловлена.
                        float ix = (LumaCur(p + float2(h.x, 0)) - LumaCur(p - float2(h.x, 0))) * 0.5;
                        float iy = (LumaCur(p + float2(0, h.y)) - LumaCur(p - float2(0, h.y))) * 0.5;
                        float it = LumaCur(p) - LumaPrev(p);

                        a11 += ix * ix;
                        a12 += ix * iy;
                        a22 += iy * iy;
                        b1  += ix * it;
                        b2  += iy * it;
                    }
                }

                float det = a11 * a22 - a12 * a12;
                float2 flow = 0;
                if (det > 1e-6)
                {
                    float2 uvSteps = -float2(a22 * b1 - a12 * b2, a11 * b2 - a12 * b1) / det;
                    flow = uvSteps * h;
                }

                float len = length(flow);
                if (len > _MaxFlow && len > 1e-6)
                    flow *= _MaxFlow / len;

                return float4(flow, 0, 1);
            }
            ENDCG
        }

        // 1 — Mosh step
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0

            float4 frag(v2f_img i) : SV_Target
            {
                float2 block = floor(i.uv * _BlockCount);
                float2 blockCenterUv = (block + 0.5) / _BlockCount;

                float2 flow = tex2D(_FlowTex, blockCenterUv).xy * _FlowStrength;
                float4 col = tex2D(_MainTex, i.uv - flow);

                float3 residual = tex2D(_CurTex, i.uv).rgb - tex2D(_PrevTex, i.uv).rgb;
                col.rgb += residual * _Residual;

                // Порог «прорастания» блока: фиксированный на весь переход. Клоки: у каждой ячейки Вороного своё время,
                // внутри ячейки блоки прорастают от центра к краю, кромка рваная за счёт разброса по блокам.
                float2 vp = blockCenterUv * float2(_VoronoiCells, _VoronoiCells / max(_Aspect, 1e-3));
                float2 cellId;
                float f1 = VoronoiF1(vp, _VoronoiSeed, cellId);
                float cellTime = Hash12(cellId * 1.13 + _VoronoiSeed * 3.7);
                float grow = saturate(f1 / 0.8);
                float threshold = cellTime * (1.0 - _VoronoiSpread) + grow * _VoronoiSpread;
                threshold += (Hash12(block + 17.13) - 0.5) * _VoronoiJitter;
                threshold = clamp(threshold, 0.01, 0.99);
                bool refreshNow = (_Refresh >= threshold) && (_RefreshPrev < threshold);

                // Редкие случайные I-блоки — «ключевые кадры» посреди моша.
                if (Hash12(block * 1.37 + _Seed) < _GlitchChance)
                    refreshNow = true;

                if (refreshNow)
                    col = tex2D(_TargetTex, i.uv);

                col.rgb = saturate(col.rgb);
                col.a = 1;
                return col;
            }
            ENDCG
        }

        // 2 — Composite
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                float4 mosh = tex2D(_MainTex, i.uv);
                float4 target = tex2D(_TargetTex, i.uv);
                float4 col = lerp(mosh, target, saturate(_Settle));
                col.a = 1;
                return col;
            }
            ENDCG
        }

        // 3 — FitCopy (кадр видео → RT с пропорциями как у VideoPlayer)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv * _FitScaleOffset.xy + _FitScaleOffset.zw;
                if (_FlipY > 0.5)
                    uv.y = 1.0 - uv.y;
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
                    return float4(0, 0, 0, 1);
                float4 col = tex2D(_MainTex, uv);
                col.a = 1;
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}
