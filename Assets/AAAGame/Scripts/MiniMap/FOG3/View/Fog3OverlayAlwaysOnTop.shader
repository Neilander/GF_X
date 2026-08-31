Shader "AAAGame/FOG3/OverlayAlwaysOnTop"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 8
        _FogBoundaryFadeDistance ("Fog Boundary Fade Distance", Float) = 0.25
        _FogHiddenColor ("Hidden Color", Color) = (0, 0, 0, 1)
        _FogExploredColor ("Explored Color", Color) = (0, 0, 0, 0.55)
        _FogVisibleColor ("Visible Color", Color) = (0, 0, 0, 0)
        _FogOutsideColor ("Outside Color", Color) = (0, 0, 0, 1)
        _FogPresentationTexelScale ("Presentation Texel Scale", Vector) = (1, 1, 0, 0)
        _FogWorldSize ("Fog World Size", Vector) = (0, 0, 0, 0)
        _FogFadeSpeed ("Fog Fade Speed", Float) = 1
        _FogUsesTimedTransitions ("Fog Uses Timed Transitions", Float) = 0
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
                float _FogBoundaryFadeDistance;
                float4 _FogHiddenColor;
                float4 _FogExploredColor;
                float4 _FogVisibleColor;
                float4 _FogOutsideColor;
                float4 _FogPresentationTexelScale;
                float4 _FogWorldSize;
                float _FogFadeSpeed;
                float _FogUsesTimedTransitions;
            CBUFFER_END

            float4 SampleFogCell(float2 cell)
            {
                float2 textureSize = _MainTex_TexelSize.zw;
                float2 clampedCell = clamp(cell, 0.0, textureSize - 1.0);
                float2 centerUv = (clampedCell + 0.5) * _MainTex_TexelSize.xy;
                return SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, centerUv, 0);
            }

            bool IsPackedFogSample(float4 sample)
            {
                return sample.g <= 3.5 / 255.0;
            }

            float UnpackTargetAlpha(float4 sample)
            {
                return IsPackedFogSample(sample) ? sample.r : sample.a;
            }

            float ResolveCurrentAlpha(float4 sample)
            {
                if (!IsPackedFogSample(sample))
                    return sample.a;
                if (_FogUsesTimedTransitions < 0.5)
                    return sample.a;
                float maximumDelta = _FogFadeSpeed * max(0.0, _Time.y - sample.a);
                return sample.b < sample.r
                    ? min(sample.b + maximumDelta, sample.r)
                    : max(sample.b - maximumDelta, sample.r);
            }

            float4 ResolveFogColor(float4 sample)
            {
                if (!IsPackedFogSample(sample))
                    return sample;

                float state = round(sample.g * 255.0);
                float4 color = state < 0.5
                    ? _FogHiddenColor
                    : state < 1.5
                        ? _FogExploredColor
                        : state < 2.5
                            ? _FogVisibleColor
                            : _FogOutsideColor;
                color.a = ResolveCurrentAlpha(sample);
                return color;
            }



            float SampleCurrentAlphaContinuous(float2 uv)
            {
                float2 size = _MainTex_TexelSize.zw;
                float2 position = clamp(uv, 0.0, 1.0) * size - 0.5;
                float2 baseCell = floor(position);
                float2 fraction = frac(position);
                float a00 = ResolveCurrentAlpha(SampleFogCell(baseCell));
                float a10 = ResolveCurrentAlpha(SampleFogCell(baseCell + float2(1.0, 0.0)));
                float a01 = ResolveCurrentAlpha(SampleFogCell(baseCell + float2(0.0, 1.0)));
                float a11 = ResolveCurrentAlpha(SampleFogCell(baseCell + float2(1.0, 1.0)));
                return lerp(lerp(a00, a10, fraction.x), lerp(a01, a11, fraction.x), fraction.y);
            }

            float ResolveSpatialPresentationAlpha(float2 uv)
            {
                float center = SampleCurrentAlphaContinuous(uv);
                if (_FogBoundaryFadeDistance <= 0.0001 || _FogWorldSize.x <= 0.0001 || _FogWorldSize.y <= 0.0001)
                    return center;

                // Time is resolved in the logic source field first.  Only then do we
                // extend a boundary toward the more transparent side in world units.
                // Find the first darker point on each ray instead of sampling one
                // fixed radius.  A fixed sample creates constant-alpha platforms
                // whenever several output pixels see the same source cells.
                float2 uvRadius = float2(
                    _FogBoundaryFadeDistance / _FogWorldSize.x,
                    _FogBoundaryFadeDistance / _FogWorldSize.y);
                const float2 directions[8] = {
                    float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1),
                    float2(0.70710678, 0.70710678), float2(-0.70710678, 0.70710678),
                    float2(0.70710678, -0.70710678), float2(-0.70710678, -0.70710678)
                };
                float result = center;
                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float2 rayOffset = directions[i] * uvRadius;
                    float boundarySample = SampleCurrentAlphaContinuous(uv + rayOffset);
                    if (boundarySample <= center + 0.00001)
                        continue;

                    // Locate the first point whose current alpha is darker than
                    // the current fragment.  The fixed iteration count keeps the
                    // shader allocation-free and bounds the work per fragment.
                    float low = 0.0;
                    float high = 1.0;
                    [unroll]
                    for (int iteration = 0; iteration < 6; iteration++)
                    {
                        float middle = (low + high) * 0.5;
                        float sample = SampleCurrentAlphaContinuous(uv + rayOffset * middle);
                        if (sample > center + 0.00001)
                            high = middle;
                        else
                            low = middle;
                    }

                    float influence = saturate(1.0 - high);
                    result = max(result, lerp(center, boundarySample, influence));
                }
                return result;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 textureSize = _MainTex_TexelSize.zw;
                float2 gridPosition = clamp(input.uv, 0.0, 1.0) * textureSize;
                float2 cell = min(floor(gridPosition), textureSize - 1.0);
                float4 packedFog = SampleFogCell(cell);
                float4 color = ResolveFogColor(packedFog);
                color.a = ResolveSpatialPresentationAlpha(input.uv);
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
            float4 _Color;
            float _FogBoundaryFadeDistance;
            float4 _FogHiddenColor;
            float4 _FogExploredColor;
            float4 _FogVisibleColor;
            float4 _FogOutsideColor;
            float4 _FogPresentationTexelScale;
            float4 _FogWorldSize;
            float _FogFadeSpeed;
            float _FogUsesTimedTransitions;
            float4 SampleFogCell(float2 cell)
            {
                float2 textureSize = _MainTex_TexelSize.zw;
                float2 clampedCell = clamp(cell, 0.0, textureSize - 1.0);
                float2 centerUv = (clampedCell + 0.5) * _MainTex_TexelSize.xy;
                return tex2Dlod(_MainTex, float4(centerUv, 0, 0));
            }

            bool IsPackedFogSample(float4 sample)
            {
                return sample.g <= 3.5 / 255.0;
            }

            float UnpackTargetAlpha(float4 sample)
            {
                return IsPackedFogSample(sample) ? sample.r : sample.a;
            }

            float ResolveCurrentAlpha(float4 sample)
            {
                if (!IsPackedFogSample(sample))
                    return sample.a;
                if (_FogUsesTimedTransitions < 0.5)
                    return sample.a;
                float maximumDelta = _FogFadeSpeed * max(0.0, _Time.y - sample.a);
                return sample.b < sample.r
                    ? min(sample.b + maximumDelta, sample.r)
                    : max(sample.b - maximumDelta, sample.r);
            }

            float4 ResolveFogColor(float4 sample)
            {
                if (!IsPackedFogSample(sample))
                    return sample;

                float state = round(sample.g * 255.0);
                float4 color = state < 0.5
                    ? _FogHiddenColor
                    : state < 1.5
                        ? _FogExploredColor
                        : state < 2.5
                            ? _FogVisibleColor
                            : _FogOutsideColor;
                color.a = ResolveCurrentAlpha(sample);
                return color;
            }



            float SampleCurrentAlphaContinuous(float2 uv)
            {
                float2 size = _MainTex_TexelSize.zw;
                float2 position = clamp(uv, 0.0, 1.0) * size - 0.5;
                float2 baseCell = floor(position);
                float2 fraction = frac(position);
                float a00 = ResolveCurrentAlpha(SampleFogCell(baseCell));
                float a10 = ResolveCurrentAlpha(SampleFogCell(baseCell + float2(1.0, 0.0)));
                float a01 = ResolveCurrentAlpha(SampleFogCell(baseCell + float2(0.0, 1.0)));
                float a11 = ResolveCurrentAlpha(SampleFogCell(baseCell + float2(1.0, 1.0)));
                return lerp(lerp(a00, a10, fraction.x), lerp(a01, a11, fraction.x), fraction.y);
            }

            float ResolveSpatialPresentationAlpha(float2 uv)
            {
                float center = SampleCurrentAlphaContinuous(uv);
                if (_FogBoundaryFadeDistance <= 0.0001 || _FogWorldSize.x <= 0.0001 || _FogWorldSize.y <= 0.0001)
                    return center;
                float2 uvRadius = float2(
                    _FogBoundaryFadeDistance / _FogWorldSize.x,
                    _FogBoundaryFadeDistance / _FogWorldSize.y);
                const float2 directions[8] = {
                    float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1),
                    float2(0.70710678, 0.70710678), float2(-0.70710678, 0.70710678),
                    float2(0.70710678, -0.70710678), float2(-0.70710678, -0.70710678)
                };
                float result = center;
                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float2 rayOffset = directions[i] * uvRadius;
                    float boundarySample = SampleCurrentAlphaContinuous(uv + rayOffset);
                    if (boundarySample <= center + 0.00001)
                        continue;

                    float low = 0.0;
                    float high = 1.0;
                    [unroll]
                    for (int iteration = 0; iteration < 6; iteration++)
                    {
                        float middle = (low + high) * 0.5;
                        float sample = SampleCurrentAlphaContinuous(uv + rayOffset * middle);
                        if (sample > center + 0.00001)
                            high = middle;
                        else
                            low = middle;
                    }

                    float influence = saturate(1.0 - high);
                    result = max(result, lerp(center, boundarySample, influence));
                }
                return result;
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

            float4 frag(v2f input) : SV_Target
            {
                float2 textureSize = _MainTex_TexelSize.zw;
                float2 gridPosition = clamp(input.uv, 0.0, 1.0) * textureSize;
                float2 cell = min(floor(gridPosition), textureSize - 1.0);
                float4 packedFog = SampleFogCell(cell);
                float4 color = ResolveFogColor(packedFog);
                color.a = ResolveSpatialPresentationAlpha(input.uv);
                return color * _Color;
            }
            ENDCG
        }
    }
}
