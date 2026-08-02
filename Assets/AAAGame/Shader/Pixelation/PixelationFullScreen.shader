// 全屏像素化后处理 shader。
//
// 用法：
// 1. 在 Project 里右键 Create → Material，挂这个 shader 上得到一个 PixelationMat.mat
// 2. URP Renderer Asset → Add Renderer Feature → Full Screen Pass Renderer Feature
//    - Pass Material = 上面那个 Material
//    - Injection Point = After Rendering Post Processing
// 3. 在 Material Inspector 里调 Block Size 控制像素粗细
//
// 调参指南：
//   Block Size = 1   等于关闭（原图）
//   Block Size = 4   微像素感
//   Block Size = 8   常用（默认）
//   Block Size = 16  中等复古
//   Block Size = 32  重度像素化
//   Block Size = 64  极致马赛克

Shader "AAAGame/Pixelation/FullScreen"
{
    Properties
    {
        [Header(Pixelation)]
        _PixelSize("Block Size (screen pixels per block)", Range(1, 64)) = 8
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Stencil
        {
            Ref 128
            Comp NotEqual
            ReadMask 128
        }

        Pass
        {
            Name "Pixelation"

            // Full screen blit pass：不写深度、不剔除、永远通过深度测试
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Fragment

            // URP 14 标准 Blit 顶点函数和 Varyings 结构都在这里
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // _BlitTexture 由 Blit.hlsl 声明（Full Screen Pass Feature 自动绑定为前一阶段的 color buffer）
            SAMPLER(sampler_BlitTexture);

            // Material 暴露的参数
            float _PixelSize;

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                // 一个 block 占多少屏幕像素（防御 0/负数）
                float blockPixels = max(_PixelSize, 1.0);

                // 把"屏幕像素尺寸"换算到 UV 空间
                // _ScreenParams.xy 是当前 RT 的分辨率（width, height），URP 自动设置
                float2 blockSizeUV = blockPixels / _ScreenParams.xy;

                // 量化 UV：屏幕被分成 blockSizeUV 一格的网格，每格采样中心点
                // floor(uv / step) 拿到当前格的索引，+ 0.5 偏移到格中心，再乘回去得到中心 UV
                float2 quantizedUV = (floor(uv / blockSizeUV) + 0.5) * blockSizeUV;

                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, quantizedUV);
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
