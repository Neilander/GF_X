// Cloud Cookie shader for animated cloud shadows on a directional light.
//
// 用法（已配套 mat 和 CRT，挂在 Real.unity 的 Directional Light 上了）：
// 1. Custom Render Texture (CloudCookieRT.asset) 用这个 shader 作为 Material
// 2. CRT Realtime 模式持续渲染 → 输出动态云形 grayscale
// 3. CRT 挂在 Directional Light 的 Cookie 槽 → 光按云影投射
//
// 算法：
// - 双层 fbm noise（不同 scale + 不同方向 + 不同速度），破除单层重复模式
// - smoothstep 切口控制软硬度
// - MinBrightness 限制最暗值（避免完全无光的区域）

Shader "AAAGame/Lighting/CloudCookie"
{
    Properties
    {
        [Header(Cloud Pattern)]
        // ⚠️ Scale 必须是整数（用 IntRange）让 noise 在贴图边界 tileable，否则 cookie tile 时能看见矩形接缝
        [IntRange] _CloudScale("Cloud Scale (tiles per cookie, integer)", Range(2, 16)) = 4
        _CloudCoverage("Cloud Coverage (0=clear, 1=overcast)", Range(0.0, 1.0)) = 0.55
        _CloudSoftness("Cloud Edge Softness", Range(0.001, 0.5)) = 0.20
        _MinBrightness("Min Brightness (darkest cloud level)", Range(0.0, 1.0)) = 0.30

        [Header(Wind)]
        _WindDirection("Wind Direction (X, Y)", Vector) = (1.0, 0.3, 0.0, 0.0)
        _WindSpeed("Wind Speed", Range(0.0, 0.5)) = 0.04

        [Header(Layer 2)]
        [IntRange] _Layer2ScaleMultiplier("Layer 2 Scale Multiplier (integer)", Range(1, 4)) = 2
        _Layer2Speed("Layer 2 Speed Multiplier", Range(0.0, 2.0)) = 0.6
        _Layer2Weight("Layer 2 Mix Weight", Range(0.0, 1.0)) = 0.5
    }

    SubShader
    {
        Lighting Off
        Blend One Zero

        Pass
        {
            Name "CloudCookie"
            CGPROGRAM
            #include "UnityCustomRenderTexture.cginc"
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag
            #pragma target 3.0

            float  _CloudScale;
            float  _CloudCoverage;
            float  _CloudSoftness;
            float  _MinBrightness;
            float4 _WindDirection;
            float  _WindSpeed;
            float  _Layer2ScaleMultiplier;
            float  _Layer2Speed;
            float  _Layer2Weight;

            // Hash with wrap：input 在 wrap 周期内重复（让 noise 边界无缝）
            float hash21Wrap(float2 p, float wrap)
            {
                // 加一个大正数避免负数 fmod 的边界问题，再 fmod 回 [0, wrap)
                p = fmod(p + wrap * 1024.0, wrap);
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }

            // Tileable smooth noise：以 wrap 为周期重复
            float tileableSmoothNoise(float2 p, float wrap)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = hash21Wrap(i,                   wrap);
                float b = hash21Wrap(i + float2(1, 0),    wrap);
                float c = hash21Wrap(i + float2(0, 1),    wrap);
                float d = hash21Wrap(i + float2(1, 1),    wrap);

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Tileable FBM：每 octave 的 wrap 周期翻倍（保持 noise 周期性）
            float tileableFbm(float2 p, float baseWrap)
            {
                float v = 0.0;
                float amp = 0.5;
                float wrap = baseWrap;
                for (int i = 0; i < 4; i++)
                {
                    v += amp * tileableSmoothNoise(p, wrap);
                    p *= 2.0;
                    wrap *= 2.0;
                    amp *= 0.5;
                }
                return v;
            }

            float4 frag(v2f_customrendertexture IN) : SV_Target
            {
                float2 uv = IN.localTexcoord.xy;

                // 防 0 向量，归一化风向
                float2 wind1 = normalize(_WindDirection.xy + float2(0.0001, 0.0));

                // 整数化（确保 wrap 是整数 → noise 在贴图边界 tileable）
                float scale1 = floor(max(2.0, _CloudScale));
                float scale2 = scale1 * floor(max(1.0, _Layer2ScaleMultiplier));

                // Layer 1：input 范围 [0, scale1]，配合 tileableFbm wrap=scale1
                float2 uv1 = uv * scale1 + wind1 * _Time.y * _WindSpeed * scale1;
                float n1 = tileableFbm(uv1, scale1);

                // Layer 2：不同 scale + 偏转方向 + 不同速度
                float2 wind2 = normalize(wind1 + float2(-0.3, 0.3));
                float2 uv2 = uv * scale2 + wind2 * _Time.y * _WindSpeed * _Layer2Speed * scale2;
                float n2 = tileableFbm(uv2, scale2);

                float n = lerp(n1, n2, _Layer2Weight);

                // 云覆盖度阈值切口
                float threshold = 1.0 - _CloudCoverage;
                float cloudMask = smoothstep(
                    threshold - _CloudSoftness,
                    threshold + _CloudSoftness,
                    n);

                // cookie：1 = 透光（晴天），0 = 遮挡（云内）
                // cloudMask 大 = 多云 = 暗，所以反转
                float light = 1.0 - cloudMask;

                // 最暗值限制：避免云完全遮黑
                light = lerp(_MinBrightness, 1.0, light);

                return float4(light, light, light, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
