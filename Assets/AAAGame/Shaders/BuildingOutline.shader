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
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_OutlineMask);
            SAMPLER(sampler_OutlineMask);
            float4 _OutlineMask_TexelSize;

            half4 _OutlineColor;
            float _OutlineThickness;

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float center = SAMPLE_TEXTURE2D(_OutlineMask, sampler_OutlineMask, uv).r;

                float2 px = _OutlineMask_TexelSize.xy * _OutlineThickness;

                // 8 邻域采样取最大值
                float maxN = 0;
                maxN = max(maxN, SAMPLE_TEXTURE2D(_OutlineMask, sampler_OutlineMask, uv + float2( px.x, 0     )).r);
                maxN = max(maxN, SAMPLE_TEXTURE2D(_OutlineMask, sampler_OutlineMask, uv + float2(-px.x, 0     )).r);
                maxN = max(maxN, SAMPLE_TEXTURE2D(_OutlineMask, sampler_OutlineMask, uv + float2(0    ,  px.y)).r);
                maxN = max(maxN, SAMPLE_TEXTURE2D(_OutlineMask, sampler_OutlineMask, uv + float2(0    , -px.y)).r);
                maxN = max(maxN, SAMPLE_TEXTURE2D(_OutlineMask, sampler_OutlineMask, uv + float2( px.x,  px.y)).r);
                maxN = max(maxN, SAMPLE_TEXTURE2D(_OutlineMask, sampler_OutlineMask, uv + float2(-px.x,  px.y)).r);
                maxN = max(maxN, SAMPLE_TEXTURE2D(_OutlineMask, sampler_OutlineMask, uv + float2( px.x, -px.y)).r);
                maxN = max(maxN, SAMPLE_TEXTURE2D(_OutlineMask, sampler_OutlineMask, uv + float2(-px.x, -px.y)).r);

                // 自身=0 但邻居≥0.5 → 这是建筑外缘像素
                float edge = step(0.5, maxN) * (1.0 - center);

                return lerp(sceneColor, _OutlineColor, edge * _OutlineColor.a);
            }
            ENDHLSL
        }
    }
}
