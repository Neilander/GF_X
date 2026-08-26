Shader "AAAGame/FOG3/OverlayAlwaysOnTop"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 8
        _FogCellSize ("Fog Cell Size", Float) = 1
        _FogBoundaryFadeDistance ("Fog Boundary Fade Distance", Float) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _MainTex_TexelSize;
                float _FogCellSize;
                float _FogBoundaryFadeDistance;
            CBUFFER_END

            half4 SampleFogCell(float2 cell)
            {
                float2 textureSize = _MainTex_TexelSize.zw;
                float2 clampedCell = clamp(cell, 0.0, textureSize - 1.0);
                float2 centerUv = (clampedCell + 0.5) * _MainTex_TexelSize.xy;
                return SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, centerUv, 0);
            }

            half ResolveBoundaryAlpha(half centerAlpha, half neighborAlpha, float worldDistance)
            {
                if (neighborAlpha <= centerAlpha)
                    return centerAlpha;

                float progress = saturate(worldDistance / _FogBoundaryFadeDistance);
                return max(centerAlpha, lerp(neighborAlpha, centerAlpha, progress));
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 textureSize = _MainTex_TexelSize.zw;
                float2 gridPosition = clamp(input.uv, 0.0, 1.0) * textureSize;
                float2 cell = min(floor(gridPosition), textureSize - 1.0);
                float2 localPosition = saturate(gridPosition - cell);
                half4 color = SampleFogCell(cell);
                half alpha = color.a;

                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(-1, 0)).a, localPosition.x * _FogCellSize);
                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(1, 0)).a, (1.0 - localPosition.x) * _FogCellSize);
                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(0, -1)).a, localPosition.y * _FogCellSize);
                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(0, 1)).a, (1.0 - localPosition.y) * _FogCellSize);
                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(-1, -1)).a, length(localPosition) * _FogCellSize);
                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(-1, 1)).a, length(float2(localPosition.x, 1.0 - localPosition.y)) * _FogCellSize);
                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(1, -1)).a, length(float2(1.0 - localPosition.x, localPosition.y)) * _FogCellSize);
                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(1, 1)).a, length(1.0 - localPosition) * _FogCellSize);

                color.a = alpha;
                return color * _Color * input.color;
            }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float _FogCellSize;
            float _FogBoundaryFadeDistance;

            fixed4 SampleFogCell(float2 cell)
            {
                float2 textureSize = _MainTex_TexelSize.zw;
                float2 clampedCell = clamp(cell, 0.0, textureSize - 1.0);
                float2 centerUv = (clampedCell + 0.5) * _MainTex_TexelSize.xy;
                return tex2Dlod(_MainTex, float4(centerUv, 0, 0));
            }

            fixed ResolveBoundaryAlpha(fixed centerAlpha, fixed neighborAlpha, float worldDistance)
            {
                if (neighborAlpha <= centerAlpha)
                    return centerAlpha;

                float progress = saturate(worldDistance / _FogBoundaryFadeDistance);
                return max(centerAlpha, lerp(neighborAlpha, centerAlpha, progress));
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 textureSize = _MainTex_TexelSize.zw;
                float2 gridPosition = clamp(input.uv, 0.0, 1.0) * textureSize;
                float2 cell = min(floor(gridPosition), textureSize - 1.0);
                float2 localPosition = saturate(gridPosition - cell);
                fixed4 color = SampleFogCell(cell);
                fixed alpha = color.a;

                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(-1, 0)).a, localPosition.x * _FogCellSize);
                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(1, 0)).a, (1.0 - localPosition.x) * _FogCellSize);
                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(0, -1)).a, localPosition.y * _FogCellSize);
                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(0, 1)).a, (1.0 - localPosition.y) * _FogCellSize);
                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(-1, -1)).a, length(localPosition) * _FogCellSize);
                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(-1, 1)).a, length(float2(localPosition.x, 1.0 - localPosition.y)) * _FogCellSize);
                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(1, -1)).a, length(float2(1.0 - localPosition.x, localPosition.y)) * _FogCellSize);
                alpha = ResolveBoundaryAlpha(alpha, SampleFogCell(cell + float2(1, 1)).a, length(1.0 - localPosition) * _FogCellSize);

                color.a = alpha;
                return color * _Color;
            }
            ENDCG
        }
    }
}
