Shader "Custom/IrregularEdgeGlow"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Texture", 2D) = "white" {}
        _BaseColor("Base Color Tint", Color) = (1, 1, 1, 1)
        
        [Header(Edge Settings)]
        _EdgeColor("Edge Color", Color) = (0, 1, 0, 1)
        _EdgeWidth("Outline Width", Range(0, 0.1)) = 0.02
        _GlowRange("Glow Falloff Range", Range(0, 0.5)) = 0.2
        _GlowIntensity("Glow Intensity", Range(0, 5)) = 1.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _EdgeColor;
                float _EdgeWidth;
                float _GlowRange;
                float _GlowIntensity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 1. 采样原始贴图
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                // 2. 计算 UV 距离边缘的距离 (0 到 0.5)
                // 取 UV 到 0 和 1 的最小值，得到该点距离最近边缘的距离
                float2 distToEdge2D = min(input.uv, 1.0 - input.uv);
                float dist = min(distToEdge2D.x, distToEdge2D.y);

                // 3. 计算描边 (Outline)
                // 如果距离小于宽度，则为 1，否则为 0
                float outlineMask = smoothstep(_EdgeWidth + 0.005, _EdgeWidth, dist);
                half4 outlineColor = _EdgeColor * outlineMask;

                // 4. 计算向内的渐变 (Inner Glow)
                // 在 _EdgeWidth 到 _GlowRange 之间产生渐变
                float glowMask = smoothstep(_GlowRange, _EdgeWidth, dist);
                // 排除掉纯描边部分，只留下向内渐变的部分
                glowMask = saturate(glowMask - outlineMask);
                half4 glowColor = _EdgeColor * glowMask * _GlowIntensity;

                // 5. 颜色合成
                // 逻辑：中心显示原色，边缘叠加上描边和渐变
                half4 finalColor = baseTex;
                
                // 混合描边和发光，渐变部分会叠加到原图上
                // 我们让 glowColor 的 Alpha 随渐变消失，实现向中心透明
                finalColor.rgb = lerp(finalColor.rgb, _EdgeColor.rgb, outlineMask);
                finalColor.rgb += glowColor.rgb;
                
                // 如果需要边缘处的原图也变透明，可以修改 alpha
                 finalColor.a = saturate(baseTex.a + outlineMask + glowMask);

                return finalColor;
            }
            ENDHLSL
        }
    }
}