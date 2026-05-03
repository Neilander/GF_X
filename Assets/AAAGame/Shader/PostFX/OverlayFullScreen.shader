// 全屏 Overlay 后处理（Photoshop "叠加" 图层混合）
//
// PS 标准公式（顶层贴图 = blend，底层屏幕 = base）：
//   result = (base < 0.5) ? 2 × base × blend
//                         : 1 - 2 × (1 - base) × (1 - blend)
//   final  = lerp(base, result, opacity × blend.a)
//
// 效果：base 亮处变更亮（screen），暗处变更暗（multiply），保留 base 整体明度
//
// 用法跟 Multiply 一样：Material → 拖贴图 → Full Screen Pass Renderer Feature

Shader "AAAGame/PostFX/Overlay"
{
    Properties
    {
        [Header(Layer)]
        _OverlayTex("Overlay Texture (overlay blended with screen)", 2D) = "gray" {}
        _Opacity("Opacity (0=off, 1=full)", Range(0.0, 1.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "Overlay"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            SAMPLER(sampler_BlitTexture);

            TEXTURE2D(_OverlayTex);
            SAMPLER(sampler_OverlayTex);
            float4 _OverlayTex_ST;
            float  _Opacity;

            // PS overlay blend formula (per channel)
            half3 OverlayBlend(half3 base, half3 blend)
            {
                half3 low  = 2.0 * base * blend;
                half3 high = 1.0 - 2.0 * (1.0 - base) * (1.0 - blend);
                return lerp(low, high, step(0.5, base));
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv);

                float2 overlayUV = uv * _OverlayTex_ST.xy + _OverlayTex_ST.zw;
                half4 blend = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, overlayUV);

                half3 overlaid = OverlayBlend(src.rgb, blend.rgb);
                half3 final = lerp(src.rgb, overlaid, _Opacity * blend.a);

                return half4(final, src.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
