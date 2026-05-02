// 风格化装饰海洋 shader（lowpoly 友好）
//
// 特点：
// - Vertex 阶段 4 层叠加 Gerstner Wave，做出海浪起伏
// - Fragment 阶段：基础水色 + Fresnel 边缘高光 + Blinn-Phong specular
// - 半透明 alpha blend，玩家不会进入，纯装饰用
// - 完全 procedural，不依赖任何贴图（开箱即用）
//
// 用法：
// 1. 在 Project 里这个 shader 上右键 Create → Material，命名 OceanMat
// 2. 准备一个高细分 Plane mesh（关键！）：
//    - 推荐 ProBuilder 工具：New Shape → Plane → Width/Length Segments 50x50 起步
//    - 或者外部建模软件做一个 100m × 100m，每米 1 个顶点的 plane fbx 导入
//    - Unity 自带的 default Plane（10x10 段）顶点太少，波浪会卡卡的
// 3. Plane GameObject 挂这个 OceanMat
// 4. 在 Material Inspector 调参看效果
//
// 调参指南：
// - WaveA-D 的 xy 是波方向（不需要单位化），z 是 steepness（0.1-0.6 合理），w 是波长（米）
// - 4 层波方向尽量错开（避免共振）；波长一大一小搭配（大波打底，小波细节）
// - WaveSpeed 全局控制波动节奏（1 = 物理真实，2-3 = 戏剧化）

Shader "AAAGame/Water/StylizedOcean"
{
    Properties
    {
        [Header(Color)]
        _ShallowColor("Shallow Color", Color) = (0.45, 0.75, 0.85, 0.95)
        _DeepColor("Deep Color", Color) = (0.05, 0.18, 0.35, 1.0)

        [Header(Gerstner Waves)]
        // 每层 wave 的参数：xy = 方向（不需要归一化），z = steepness，w = wavelength
        _WaveA("Wave A (dirX, dirZ, steepness, wavelength)", Vector) = (1.0, 0.0, 0.45, 14.0)
        _WaveB("Wave B", Vector) = (0.6, 0.8, 0.35, 9.0)
        _WaveC("Wave C", Vector) = (0.8, -0.4, 0.25, 5.0)
        _WaveD("Wave D", Vector) = (0.3, 1.0, 0.15, 2.5)
        _WaveSpeed("Wave Speed", Range(0.1, 5.0)) = 1.0

        [Header(Surface)]
        _FresnelPower("Fresnel Power (edge softness)", Range(0.5, 10.0)) = 4.0
        _FresnelStrength("Fresnel Strength", Range(0.0, 2.0)) = 0.8
        _SpecularColor("Specular Color", Color) = (1.0, 0.95, 0.85, 1.0)
        _SpecularSmoothness("Specular Smoothness", Range(0.0, 1.0)) = 0.85
        _SpecularStrength("Specular Strength", Range(0.0, 5.0)) = 1.5
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
            ZWrite On    // 半透明也写 depth：让水面成为 SceneDepth 的一部分（你扫描特效要用）
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _WaveA;
                float4 _WaveB;
                float4 _WaveC;
                float4 _WaveD;
                float  _WaveSpeed;
                float  _FresnelPower;
                float  _FresnelStrength;
                float4 _SpecularColor;
                float  _SpecularSmoothness;
                float  _SpecularStrength;
            CBUFFER_END

            // Gerstner Wave 核心：返回当前顶点的位移（offset），并把切向量/副切向量累加到 inout 引用上。
            //
            // 数学：
            //   令 D = normalize(wave.xy), k = 2π/wavelength, c = sqrt(g/k)（重力波速）
            //   f = k * (D · positionXZ - c * time * speed)
            //   位移 = (Dx · a · cos(f), a · sin(f), Dz · a · cos(f))，其中 a = steepness / k
            //
            //   tangent / binormal 是位移函数对 x / z 的偏导数累加项，最后 cross 出法线
            float3 GerstnerWave(float4 wave, float3 worldPos, inout float3 tangent, inout float3 binormal, float t)
            {
                float steepness = wave.z;
                float wavelength = max(wave.w, 0.0001);
                float k = 6.28318530718 / wavelength;     // 2π / λ
                float c = sqrt(9.8 / k);                   // 深水重力波速 √(g/k)
                float2 d = normalize(wave.xy + float2(0.0001, 0.0)); // 防 0 向量
                float f = k * (dot(d, worldPos.xz) - c * t * _WaveSpeed);
                float a = steepness / k;

                float sinF = sin(f);
                float cosF = cos(f);

                // 切向量累加（dP/dx 的差量项；初始 tangent 已含 (1,0,0)）
                tangent += float3(
                    -d.x * d.x * (steepness * sinF),
                     d.x       * (steepness * cosF),
                    -d.x * d.y * (steepness * sinF)
                );

                // 副切向量累加（dP/dz 的差量项；初始 binormal 已含 (0,0,1)）
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

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);

                // 4 层波叠加；初始 tangent/binormal 是平静水面的切向量
                float3 tangent  = float3(1.0, 0.0, 0.0);
                float3 binormal = float3(0.0, 0.0, 1.0);

                float t = _Time.y;
                worldPos += GerstnerWave(_WaveA, worldPos, tangent, binormal, t);
                worldPos += GerstnerWave(_WaveB, worldPos, tangent, binormal, t);
                worldPos += GerstnerWave(_WaveC, worldPos, tangent, binormal, t);
                worldPos += GerstnerWave(_WaveD, worldPos, tangent, binormal, t);

                // 法线 = binormal × tangent，正常水面朝 +Y
                float3 normalWS = normalize(cross(binormal, tangent));

                OUT.positionWS  = worldPos;
                OUT.positionHCS = TransformWorldToHClip(worldPos);
                OUT.normalWS    = normalWS;
                OUT.fogCoord    = ComputeFogFactor(OUT.positionHCS.z);

                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 H = normalize(L + V);

                // --- 1. 基础水色 ---
                // NdotV 越大（俯视）→ 看到深处颜色；越小（平视）→ 看到浅处颜色
                float NdotV = saturate(dot(N, V));
                float3 baseColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, NdotV);

                // --- 2. 半 Lambert 漫反射（柔和阴影感）---
                float NdotL = dot(N, L);
                float halfLambert = NdotL * 0.5 + 0.5;
                float3 diffuse = baseColor * halfLambert * mainLight.color;

                // --- 3. Blinn-Phong 高光（阳光在波峰的镜面反射）---
                float NdotH = saturate(dot(N, H));
                float specPower = exp2(_SpecularSmoothness * 10.0) + 1.0; // smoothness 0→2, 1→1025
                float spec = pow(NdotH, specPower);
                float3 specular = spec * _SpecularColor.rgb * mainLight.color * _SpecularStrength;

                // --- 4. Fresnel 边缘（视角接近水平时反射更多天空色）---
                float fresnel = pow(1.0 - NdotV, _FresnelPower) * _FresnelStrength;
                float3 fresnelColor = fresnel * _ShallowColor.rgb;

                // --- 合成 ---
                float3 finalColor = diffuse + specular + fresnelColor;

                // Fog
                finalColor = MixFog(finalColor, IN.fogCoord);

                return float4(finalColor, _ShallowColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
