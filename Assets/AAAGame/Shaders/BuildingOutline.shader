Shader "Hidden/Custom/BuildingOutline"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        Cull Off
        ZWrite Off

        Pass
        {
            Name "Outline"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_OutlineMask);
            SAMPLER(sampler_OutlineMask);
            float4 _OutlineMask_TexelSize;

            half4 _OutlineColor;
            float _OutlineThickness;

            // 读 mask R = 建筑 raw NDC depth；0 表示无建筑
            float SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_OutlineMask, sampler_OutlineMask, uv).r;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float center = SampleMask(uv);

                float2 px = _OutlineMask_TexelSize.xy * _OutlineThickness;

                // 8 邻域取最大 mask 值（建筑深度最近邻居）
                float maxN = 0;
                maxN = max(maxN, SampleMask(uv + float2( px.x, 0    )));
                maxN = max(maxN, SampleMask(uv + float2(-px.x, 0    )));
                maxN = max(maxN, SampleMask(uv + float2(0    ,  px.y)));
                maxN = max(maxN, SampleMask(uv + float2(0    , -px.y)));
                maxN = max(maxN, SampleMask(uv + float2( px.x,  px.y)));
                maxN = max(maxN, SampleMask(uv + float2(-px.x,  px.y)));
                maxN = max(maxN, SampleMask(uv + float2( px.x, -px.y)));
                maxN = max(maxN, SampleMask(uv + float2(-px.x, -px.y)));

                // 边缘判定：自身=0 但邻居有建筑（>0）
                float isEdge = step(1e-3, maxN) * (1.0 - step(1e-3, center));

                // 前景遮挡判定：当前像素 SceneDepth 比邻居建筑深度更近 → 前景挡住，不画
                // raw NDC depth (reverse-Z): 近=1 远=0。所以 sceneDepth_ndc > buildingDepth_ndc 表示前景更近
                float sceneNdcDepth = SampleSceneDepth(uv);
                float buildingNdcDepth = maxN;
                // epsilon 容忍 alpha 像素 / 浮点误差
                float occluded = step(buildingNdcDepth + 1e-4, sceneNdcDepth);

                float drawEdge = isEdge * (1.0 - occluded);

                return lerp(sceneColor, _OutlineColor, drawEdge * _OutlineColor.a);
            }
            ENDHLSL
        }
    }
}
