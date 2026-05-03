// 全屏 Multiply 后处理（Photoshop "正片叠底" 图层混合）
//
// PS 公式（顶层贴图 = blend，底层屏幕 = base）：
//   result = base × blend
//   final  = lerp(base, result, opacity × blend.a)   // blend 的 alpha 也参与
//
// 用法：
// 1. 右键此 shader → Create → Material
// 2. 在 Material 上把贴图拖到 Overlay Texture 槽
// 3. URP Renderer Asset → Add Full Screen Pass Renderer Feature
// 4. Pass Material 选这个 Material；Injection Point = After Rendering Post Processing
// 5. 调 Tiling / Offset / Opacity

Shader "AAAGame/PostFX/Multiply"
{
    Properties
    {
        [Header(Layer)]
        _OverlayTex("Overlay Texture (multiplied with screen)", 2D) = "white" {}
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
            Name "Multiply"
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

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // 屏幕原色
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv);

                // overlay 图层（受 Material 上 Tiling/Offset 控制）
                float2 overlayUV = uv * _OverlayTex_ST.xy + _OverlayTex_ST.zw;
                half4 blend = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, overlayUV);

                half3 multiplied = src.rgb * blend.rgb;
                // blend.a 跟 _Opacity 一起决定混合强度（贴图本身的透明度也生效）
                half3 final = lerp(src.rgb, multiplied, _Opacity * blend.a);

                return half4(final, src.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
