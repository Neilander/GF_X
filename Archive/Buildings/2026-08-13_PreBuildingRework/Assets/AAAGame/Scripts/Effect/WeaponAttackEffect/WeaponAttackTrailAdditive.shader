Shader "AAAGame/Effect/WeaponAttackTrailAdditive"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.45, 0.95, 1, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1.6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "WeaponAttackTrailForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = input.color * _BaseColor;
                half3 boostedColor = color.rgb * _Intensity;
                half maxChannel = max(max(boostedColor.r, boostedColor.g), boostedColor.b);
                color.rgb = maxChannel > 1.0 ? boostedColor / maxChannel : boostedColor;
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Particles/Unlit"
}
