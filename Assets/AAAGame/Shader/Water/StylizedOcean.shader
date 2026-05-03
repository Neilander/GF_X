// 风格化装饰海洋 shader（v2 - 加入 foam + 破除重复模式）
//
// 改动 vs v1：
// - 大幅降低波动频率（波长拉大、speed 减半），消除高频抖动感
// - 4 层 wave 方向 / 波长非整除关系，破除"看出周期"的视觉重复
// - 新增 Foam（基于 SceneDepth 的边缘白色泡沫 + procedural noise）
// - 改 ZWrite Off：让 _CameraDepthTexture 保留水下物体的 depth，foam 才能算交界
//
// 特点：
// - Vertex 阶段 4 层叠加 Gerstner Wave，慢节奏大尺度起伏
// - Fragment 阶段：基础水色 + Fresnel + Specular + Foam（智能边缘泡沫）
// - 半透明 alpha blend，玩家不会进入
// - 完全 procedural，不依赖任何贴图
//
// 前提：URP Asset 必须勾选 Depth Texture（用于 SceneDepth 采样）
//
// 调参指南：
// - Wave A-D xy = 方向（不需归一化），z = steepness（0.05-0.25 合理），w = wavelength
// - 4 层方向尽量错开 + 波长非倍数关系（避免共振条纹）
// - 4 层 steepness 总和最好 < 1.0（防自相交）
// - Foam Distance 越大 = foam 范围越广（深水也染白）
// - Foam Noise Scale 越大 = foam 边缘越破碎；越小 = 越平滑
// - Wave Speed 1.0 = 物理速度，0.4 = 慢半拍（更"沉稳"）

Shader "AAAGame/Water/StylizedOcean"
{
    Properties
    {
        [Header(Color)]
        _ShallowColor("Shallow Color", Color) = (0.45, 0.75, 0.85, 0.95)
        _DeepColor("Deep Color", Color) = (0.05, 0.18, 0.35, 1.0)

        [Header(Gerstner Waves)]
        // 大波长打底 + 4 层方向错开 + 非整除波长，破除重复模式
        _WaveA("Wave A (dirX, dirZ, steepness, wavelength)", Vector) = (1.0, 0.0, 0.20, 32.0)
        _WaveB("Wave B", Vector) = (-0.7, 0.6, 0.16, 18.0)
        _WaveC("Wave C", Vector) = (0.5, -0.9, 0.10, 11.0)
        _WaveD("Wave D", Vector) = (-0.3, -0.8, 0.06, 6.5)
        _WaveSpeed("Wave Speed", Range(0.1, 5.0)) = 0.4

        [Header(Foam)]
        // 参考 Alex Ameye stylized water shader 的 intersection foam 思路：
        //   1. Depth Fade Mask = 接触线衰减（贴岸=1，远端=0）
        //   2. Foam Texture = 一张"泡沫贴图"（这里用两层 procedural noise 模拟）
        //   3. Threshold = Cutoff × Mask（mask 越小，threshold 越低，但下面会乘以 mask 让 alpha 也衰减）
        //   4. 最终 = step(Threshold, Texture) × Mask × Strength
        //   核心：让 foam 既受 mask 范围约束，又受贴图本身的形状约束 → 出来是不规则斑点
        _FoamColor("Foam Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _FoamWidth("Foam Width (m, total range)", Range(0.0, 50.0)) = 0.15
        _FoamFalloff("Foam Mask Hardness (higher = sharper edge)", Range(0.5, 8.0)) = 2.0
        _FoamStrength("Foam Strength", Range(0.0, 2.0)) = 1.0
        _FoamNoiseScale("Foam Noise Scale (larger = smaller spots)", Range(0.1, 30.0)) = 8.0
        _FoamNoiseSpeed("Foam Noise Speed", Range(0.0, 5.0)) = 0.8
        _FoamCutoff("Foam Cutoff (texture step threshold)", Range(0.0, 1.0)) = 0.5

        [Header(Surface)]
        _FresnelPower("Fresnel Power (edge softness)", Range(0.5, 10.0)) = 4.0
        _FresnelStrength("Fresnel Strength", Range(0.0, 2.0)) = 0.8
        _SpecularColor("Specular Color", Color) = (1.0, 0.95, 0.85, 1.0)
        _SpecularSmoothness("Specular Smoothness", Range(0.0, 1.0)) = 0.85
        _SpecularStrength("Specular Strength", Range(0.0, 5.0)) = 1.5

        [Header(Debug)]
        // 0=正常渲染
        // 1=输出 depthDiff 红色梯度（黑=0，红=Width，亮红=超出）
        // 2=输出 sceneEyeDepth 蓝色梯度（除以 100 米）
        // 3=输出 waterEyeDepth 绿色梯度（除以 100 米）
        // 4=输出 unity_OrthoParams.w（ortho=白，persp=黑）
        [IntRange] _DebugMode("Debug Mode (0=off, 1-4=show)", Range(0, 4)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off    // 不写深度，让 foam 能采样到水下物体的 SceneDepth
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float fogCoord     : TEXCOORD2;
                float4 screenPos   : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _WaveA;
                float4 _WaveB;
                float4 _WaveC;
                float4 _WaveD;
                float  _WaveSpeed;
                float4 _FoamColor;
                float  _FoamWidth;
                float  _FoamFalloff;
                float  _FoamStrength;
                float  _FoamNoiseScale;
                float  _FoamNoiseSpeed;
                float  _FoamCutoff;
                float  _FresnelPower;
                float  _FresnelStrength;
                float4 _SpecularColor;
                float  _SpecularSmoothness;
                float  _SpecularStrength;
                float  _DebugMode;
            CBUFFER_END

            // Gerstner Wave：返回顶点位移，并把切向量/副切向量累加到 inout 引用
            float3 GerstnerWave(float4 wave, float3 worldPos, inout float3 tangent, inout float3 binormal, float t)
            {
                float steepness = wave.z;
                float wavelength = max(wave.w, 0.0001);
                float k = 6.28318530718 / wavelength;     // 2π / λ
                float c = sqrt(9.8 / k);                   // 深水重力波速
                float2 d = normalize(wave.xy + float2(0.0001, 0.0));
                float f = k * (dot(d, worldPos.xz) - c * t * _WaveSpeed);
                float a = steepness / k;

                float sinF = sin(f);
                float cosF = cos(f);

                tangent += float3(
                    -d.x * d.x * (steepness * sinF),
                     d.x       * (steepness * cosF),
                    -d.x * d.y * (steepness * sinF)
                );

                binormal += float3(
                    -d.x * d.y * (steepness * sinF),
                     d.y       * (steepness * cosF),
                    -d.y * d.y * (steepness * sinF)
                );

                return float3(
                    d.x * (a * cosF),
                    a * sinF,
                    d.y * (a * cosF)
                );
            }

            // Procedural smooth noise（hash + bilinear smoothstep），用来打破 foam 的圆润梯度
            float hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }

            float smoothNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float dn = hash21(i + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, dn, f.x), f.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);

                float3 tangent  = float3(1.0, 0.0, 0.0);
                float3 binormal = float3(0.0, 0.0, 1.0);

                float t = _Time.y;
                worldPos += GerstnerWave(_WaveA, worldPos, tangent, binormal, t);
                worldPos += GerstnerWave(_WaveB, worldPos, tangent, binormal, t);
                worldPos += GerstnerWave(_WaveC, worldPos, tangent, binormal, t);
                worldPos += GerstnerWave(_WaveD, worldPos, tangent, binormal, t);

                float3 normalWS = normalize(cross(binormal, tangent));

                OUT.positionWS  = worldPos;
                OUT.positionHCS = TransformWorldToHClip(worldPos);
                OUT.normalWS    = normalWS;
                OUT.fogCoord    = ComputeFogFactor(OUT.positionHCS.z);
                OUT.screenPos   = ComputeScreenPos(OUT.positionHCS);

                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 H = normalize(L + V);

                // === 1. 基础水色 ===
                float NdotV = saturate(dot(N, V));
                float3 baseColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, NdotV);

                // === 2. 半 Lambert 漫反射 ===
                float NdotL = dot(N, L);
                float halfLambert = NdotL * 0.5 + 0.5;
                float3 diffuse = baseColor * halfLambert * mainLight.color;

                // === 3. Blinn-Phong 高光 ===
                float NdotH = saturate(dot(N, H));
                float specPower = exp2(_SpecularSmoothness * 10.0) + 1.0;
                float spec = pow(NdotH, specPower);
                float3 specular = spec * _SpecularColor.rgb * mainLight.color * _SpecularStrength;

                // === 4. Fresnel 边缘 ===
                float fresnel = pow(1.0 - NdotV, _FresnelPower) * _FresnelStrength;
                float3 fresnelColor = fresnel * _ShallowColor.rgb;

                // === 5. Foam（按 Alex Ameye 公式：depth fade mask + foam texture + cutoff step）===
                // final = step(Cutoff × Mask, FoamTexture) × Mask × Strength
                // 关键点：alpha 也乘以 Mask，远端即使 step 偶尔通过也被 alpha=0 抹掉
                //
                // ⚠️ Ortho/Perspective 通用 linear eye depth：
                //   - LinearEyeDepth(rawDepth, _ZBufferParams) 是 perspective 1/z 公式，ortho 下错
                //   - ortho 下 ndcDepth 直接是 [near,far] 的 linear，要用 lerp(near, far, ndc)
                //   - URP 14 默认 reversed Z（near=1, far=0），所以 lerp(far, near, raw)
                //   - unity_OrthoParams.w == 1 时是 ortho mode，自动切换公式
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float sceneDepthRaw = SampleSceneDepth(screenUV);

                // Ortho linear eye depth
                #if UNITY_REVERSED_Z
                    float orthoSceneEyeDepth = lerp(_ProjectionParams.z, _ProjectionParams.y, sceneDepthRaw);
                #else
                    float orthoSceneEyeDepth = lerp(_ProjectionParams.y, _ProjectionParams.z, sceneDepthRaw);
                #endif
                // Perspective linear eye depth
                float perspSceneEyeDepth = LinearEyeDepth(sceneDepthRaw, _ZBufferParams);
                // 自动选：unity_OrthoParams.w = 1 → ortho；= 0 → perspective
                float sceneEyeDepth = lerp(perspSceneEyeDepth, orthoSceneEyeDepth, unity_OrthoParams.w);

                // 水面像素的 eye depth 用 view matrix 算（两种相机都对）
                float waterEyeDepth = LinearEyeDepth(IN.positionWS, UNITY_MATRIX_V);

                float depthDiff = max(0.0, sceneEyeDepth - waterEyeDepth);

                // --- Depth Fade Mask（接触岸 = 1，远端 = 0，pow 控制硬度）---
                float prox = saturate(depthDiff / max(_FoamWidth, 0.001));
                float depthFade = pow(1.0 - prox, _FoamFalloff);

                // --- Foam "Texture"（用两层 panning noise 模拟泡沫贴图）---
                float2 noiseBase = IN.positionWS.xz * _FoamNoiseScale * 0.1;
                float2 panning1  = float2(_Time.y * _FoamNoiseSpeed * 0.10, _Time.y * _FoamNoiseSpeed * 0.07);
                float2 panning2  = float2(_Time.y * _FoamNoiseSpeed * -0.13, _Time.y * _FoamNoiseSpeed * 0.11);
                float n1 = smoothNoise(noiseBase + panning1);
                float n2 = smoothNoise(noiseBase * 2.7 + 13.0 + panning2);
                float foamTex = saturate(n1 * 0.7 + n2 * 0.5);

                // --- Cutoff = base × mask（远端 cutoff → 0，但 alpha 也 → 0 所以不会反而泛白）---
                float threshold = _FoamCutoff * depthFade;
                float foamPattern = step(threshold, foamTex);

                // --- 最终 mask = pattern × mask × strength（alpha 受 mask 限制，远端 alpha 0）---
                float foamMask = foamPattern * depthFade * _FoamStrength;
                foamMask = saturate(foamMask);

                // === Debug 输出（不透明、不混合，直接覆盖屏幕）===
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                {
                    // depthDiff 红色梯度：0=黑, FoamWidth=正红, 超出=亮红
                    float v = saturate(depthDiff / max(_FoamWidth, 0.001));
                    return float4(v, 0, 0, 1);
                }
                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                {
                    // sceneEyeDepth 蓝色梯度（除以 100 米归一化）
                    return float4(0, 0, saturate(sceneEyeDepth / 100.0), 1);
                }
                if (_DebugMode > 2.5 && _DebugMode < 3.5)
                {
                    // waterEyeDepth 绿色梯度
                    return float4(0, saturate(waterEyeDepth / 100.0), 0, 1);
                }
                if (_DebugMode > 3.5)
                {
                    // unity_OrthoParams.w：ortho=白，persp=黑
                    float v = unity_OrthoParams.w;
                    return float4(v, v, v, 1);
                }

                // === 正常合成 ===
                float3 waterColor = diffuse + specular + fresnelColor;
                float3 finalColor = lerp(waterColor, _FoamColor.rgb, foamMask);

                // Fog
                finalColor = MixFog(finalColor, IN.fogCoord);

                // foam 处不透明（白沫看起来扎实），其他地方按 ShallowColor.a
                float alpha = lerp(_ShallowColor.a, 1.0, foamMask);

                return float4(finalColor, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
