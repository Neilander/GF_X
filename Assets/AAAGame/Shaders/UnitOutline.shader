Shader "Hidden/AAAGame/UnitOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.01
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+50" }
        LOD 100

        Pass
        {
            Name "Outline"
            ZWrite Off
            ZTest LEqual
            Cull Front
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _OutlineColor;
            float _OutlineWidth;

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 normal = normalize(input.normalOS);
                float4 posOS = input.positionOS;
                
                // 沿法线方向外扩
                posOS.xyz += normal * _OutlineWidth;
                
                output.positionCS = TransformObjectToHClip(posOS.xyz);
                output.color = _OutlineColor;
                
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                return input.color;
            }
            ENDHLSL
        }
    }
}
