Shader "AAAGame/Effect/UnitDeathVFXParticle"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1
        _Softness ("Softness", Range(0.01, 0.5)) = 0.22
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
            Name "UnitDeathVFXParticle"
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
                half _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centeredUv = input.uv * 2.0 - 1.0;
                half distanceToCenter = saturate(length(centeredUv));
                half radialMask = 1.0 - smoothstep(max(0.01, 1.0 - _Softness), 1.0, distanceToCenter);

                half4 color = input.color * _BaseColor;
                color.rgb *= _Intensity;
                color.a *= radialMask;
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Particles/Unlit"
}
