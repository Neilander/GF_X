// 全屏 Saturation 后处理（PS Hue/Saturation 调整层 + 蒙版）
//
// 算法：
//   gray  = luma(src)                                  // Rec. 709 luma weights
//   adj   = lerp(gray, src, _Saturation)               // 0=灰度, 1=原图, 2=过饱和
//   mask  = sampleMask(uv)                             // 蒙版灰度（白=应用，黑=不应用）
//   final = lerp(src, adj, _Opacity × mask)
//
// _Saturation 滑杆对照 PS：
//   0   = PS -100（完全去色，灰度）
//   1   = PS 0（原图）
//   2   = PS +100（最大饱和）
//
// _MaskTex 默认 "white"（全画面应用）。拖一张灰度图就能局部应用饱和度调整。
//
// 用法：Create Material → Full Screen Pass Renderer Feature → 拖 mask 调 Saturation/Opacity

Shader "AAAGame/PostFX/Saturation"
{
    Properties
    {
        [Header(Saturation)]
        _Saturation("Saturation (0=gray, 1=original, 2=oversaturated)", Range(0.0, 2.0)) = 1.0
        _Opacity("Opacity (0=off, 1=full)", Range(0.0, 1.0)) = 1.0

        [Header(Mask Layer)]
        _MaskTex("Mask Texture (white=apply, black=skip)", 2D) = "white" {}
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
            Name "Saturation"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            SAMPLER(sampler_BlitTexture);

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            float4 _MaskTex_ST;
            float  _Saturation;
            float  _Opacity;

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv);

                // Rec. 709 luma weights（与 sRGB 标准、modern PS 一致）
                half luma = dot(src.rgb, half3(0.2126, 0.7152, 0.0722));
                half3 adjusted = lerp(half3(luma, luma, luma), src.rgb, _Saturation);

                // 蒙版：default "white" = 全屏应用；拖灰度图可局部应用
                float2 maskUV = uv * _MaskTex_ST.xy + _MaskTex_ST.zw;
                half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, maskUV);
                half maskValue = mask.r * mask.a;  // grayscale × alpha

                half3 final = lerp(src.rgb, adjusted, _Opacity * maskValue);
                return half4(final, src.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
