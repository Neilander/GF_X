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
                float _FogSpatialTargetsReady;
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

            float SampleTargetAlphaDiscrete(float2 uv)
            {
                float2 size = _MainTex_TexelSize.zw;
                float2 cell = min(floor(clamp(uv, 0.0, 1.0) * size), size - 1.0);
                return UnpackTargetAlpha(SampleFogCell(cell));
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

            float2 SampleFogAlphaContinuous(float2 uv)
            {
                float2 size = _MainTex_TexelSize.zw;
                float2 texelPosition = clamp(uv, 0.0, 1.0) * size - 0.5;
                float2 baseCell = floor(texelPosition);
                float2 fraction = frac(texelPosition);
                float4 sample00 = SampleFogCell(baseCell);
                float4 sample10 = SampleFogCell(baseCell + float2(1.0, 0.0));
                float4 sample01 = SampleFogCell(baseCell + float2(0.0, 1.0));
                float4 sample11 = SampleFogCell(baseCell + float2(1.0, 1.0));
                float2 a00 = float2(UnpackTargetAlpha(sample00), ResolveCurrentAlpha(sample00));
                float2 a10 = float2(UnpackTargetAlpha(sample10), ResolveCurrentAlpha(sample10));
                float2 a01 = float2(UnpackTargetAlpha(sample01), ResolveCurrentAlpha(sample01));
                float2 a11 = float2(UnpackTargetAlpha(sample11), ResolveCurrentAlpha(sample11));
                return lerp(lerp(a00, a10, fraction.x), lerp(a01, a11, fraction.x), fraction.y);
            }

            float2 SampleFogAlphaRow(
                float2 uv,
                float cellOffsetY,
                out float maximumTargetAlpha,
                out float minimumTargetAlpha,
                out float boundaryCurrentAlpha)
            {
                float2 texel = _MainTex_TexelSize.xy;
                float2 rowUv = uv + float2(0.0, cellOffsetY * _FogPresentationTexelScale.y * texel.y);
                float2 alpha0 = SampleFogAlphaContinuous(clamp(rowUv + float2(-7.058824 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                float2 alpha1 = SampleFogAlphaContinuous(clamp(rowUv + float2(-5.176471 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                float2 alpha2 = SampleFogAlphaContinuous(clamp(rowUv + float2(-3.294118 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                float2 alpha3 = SampleFogAlphaContinuous(clamp(rowUv + float2(-1.411765 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                float2 alpha4 = SampleFogAlphaContinuous(clamp(rowUv, 0.0, 1.0));
                float2 alpha5 = SampleFogAlphaContinuous(clamp(rowUv + float2(1.411765 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                float2 alpha6 = SampleFogAlphaContinuous(clamp(rowUv + float2(3.294118 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                float2 alpha7 = SampleFogAlphaContinuous(clamp(rowUv + float2(5.176471 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                float2 alpha8 = SampleFogAlphaContinuous(clamp(rowUv + float2(7.058824 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                maximumTargetAlpha = alpha0.x;
                boundaryCurrentAlpha = alpha0.y;
                if (alpha1.x > maximumTargetAlpha) { maximumTargetAlpha = alpha1.x; boundaryCurrentAlpha = alpha1.y; }
                if (alpha2.x > maximumTargetAlpha) { maximumTargetAlpha = alpha2.x; boundaryCurrentAlpha = alpha2.y; }
                if (alpha3.x > maximumTargetAlpha) { maximumTargetAlpha = alpha3.x; boundaryCurrentAlpha = alpha3.y; }
                if (alpha4.x > maximumTargetAlpha) { maximumTargetAlpha = alpha4.x; boundaryCurrentAlpha = alpha4.y; }
                if (alpha5.x > maximumTargetAlpha) { maximumTargetAlpha = alpha5.x; boundaryCurrentAlpha = alpha5.y; }
                if (alpha6.x > maximumTargetAlpha) { maximumTargetAlpha = alpha6.x; boundaryCurrentAlpha = alpha6.y; }
                if (alpha7.x > maximumTargetAlpha) { maximumTargetAlpha = alpha7.x; boundaryCurrentAlpha = alpha7.y; }
                if (alpha8.x > maximumTargetAlpha) { maximumTargetAlpha = alpha8.x; boundaryCurrentAlpha = alpha8.y; }
                minimumTargetAlpha = min(min(min(min(alpha0.x, alpha1.x), min(alpha2.x, alpha3.x)), alpha4.x), min(min(alpha5.x, alpha6.x), min(alpha7.x, alpha8.x)));
                return (alpha0 * 17.0 + alpha1 * 680.0 + alpha2 * 6188.0 + alpha3 * 19448.0 + alpha4 * 12870.0 + alpha5 * 19448.0 + alpha6 * 6188.0 + alpha7 * 680.0 + alpha8 * 17.0) / 65536.0;
            }

            float2 SampleContinuousFogAlpha(
                float2 uv,
                out float maximumTargetAlpha,
                out float minimumTargetAlpha,
                out float boundaryCurrentAlpha)
            {
                float maximumTarget0, maximumTarget1, maximumTarget2, maximumTarget3, maximumTarget4;
                float maximumTarget5, maximumTarget6, maximumTarget7, maximumTarget8;
                float minimumTarget0, minimumTarget1, minimumTarget2, minimumTarget3, minimumTarget4;
                float minimumTarget5, minimumTarget6, minimumTarget7, minimumTarget8;
                float boundaryCurrent0, boundaryCurrent1, boundaryCurrent2, boundaryCurrent3, boundaryCurrent4;
                float boundaryCurrent5, boundaryCurrent6, boundaryCurrent7, boundaryCurrent8;
                float2 row0 = SampleFogAlphaRow(uv, -7.058824, maximumTarget0, minimumTarget0, boundaryCurrent0);
                float2 row1 = SampleFogAlphaRow(uv, -5.176471, maximumTarget1, minimumTarget1, boundaryCurrent1);
                float2 row2 = SampleFogAlphaRow(uv, -3.294118, maximumTarget2, minimumTarget2, boundaryCurrent2);
                float2 row3 = SampleFogAlphaRow(uv, -1.411765, maximumTarget3, minimumTarget3, boundaryCurrent3);
                float2 row4 = SampleFogAlphaRow(uv, 0.0, maximumTarget4, minimumTarget4, boundaryCurrent4);
                float2 row5 = SampleFogAlphaRow(uv, 1.411765, maximumTarget5, minimumTarget5, boundaryCurrent5);
                float2 row6 = SampleFogAlphaRow(uv, 3.294118, maximumTarget6, minimumTarget6, boundaryCurrent6);
                float2 row7 = SampleFogAlphaRow(uv, 5.176471, maximumTarget7, minimumTarget7, boundaryCurrent7);
                float2 row8 = SampleFogAlphaRow(uv, 7.058824, maximumTarget8, minimumTarget8, boundaryCurrent8);
                maximumTargetAlpha = max(max(max(max(maximumTarget0, maximumTarget1), max(maximumTarget2, maximumTarget3)), maximumTarget4), max(max(maximumTarget5, maximumTarget6), max(maximumTarget7, maximumTarget8)));
                minimumTargetAlpha = min(min(min(min(minimumTarget0, minimumTarget1), min(minimumTarget2, minimumTarget3)), minimumTarget4), min(min(minimumTarget5, minimumTarget6), min(minimumTarget7, minimumTarget8)));
                boundaryCurrentAlpha = boundaryCurrent0;
                if (maximumTarget1 > maximumTarget0) boundaryCurrentAlpha = boundaryCurrent1;
                if (maximumTarget2 > max(maximumTarget0, maximumTarget1)) boundaryCurrentAlpha = boundaryCurrent2;
                if (maximumTarget3 > max(max(maximumTarget0, maximumTarget1), maximumTarget2)) boundaryCurrentAlpha = boundaryCurrent3;
                if (maximumTarget4 > max(max(max(maximumTarget0, maximumTarget1), maximumTarget2), maximumTarget3)) boundaryCurrentAlpha = boundaryCurrent4;
                if (maximumTarget5 > max(max(max(max(maximumTarget0, maximumTarget1), maximumTarget2), maximumTarget3), maximumTarget4)) boundaryCurrentAlpha = boundaryCurrent5;
                if (maximumTarget6 > max(max(max(max(max(maximumTarget0, maximumTarget1), maximumTarget2), maximumTarget3), maximumTarget4), maximumTarget5)) boundaryCurrentAlpha = boundaryCurrent6;
                if (maximumTarget7 > max(max(max(max(max(max(maximumTarget0, maximumTarget1), maximumTarget2), maximumTarget3), maximumTarget4), maximumTarget5), maximumTarget6)) boundaryCurrentAlpha = boundaryCurrent7;
                if (maximumTarget8 > max(max(max(max(max(max(max(maximumTarget0, maximumTarget1), maximumTarget2), maximumTarget3), maximumTarget4), maximumTarget5), maximumTarget6), maximumTarget7)) boundaryCurrentAlpha = boundaryCurrent8;
                return (
                    row0 * 17.0 + row1 * 680.0 + row2 * 6188.0 + row3 * 19448.0
                    + row4 * 12870.0
                    + row5 * 19448.0 + row6 * 6188.0 + row7 * 680.0 + row8 * 17.0) / 65536.0;
            }

            float LinearStepInfluence(float transparentPosition)
            {
                return saturate(0.5 - transparentPosition);
            }

            float StraightBoundaryInfluence(float transparentPosition)
            {
                return (
                    LinearStepInfluence(transparentPosition - 7.058824) * 17.0
                    + LinearStepInfluence(transparentPosition - 5.176471) * 680.0
                    + LinearStepInfluence(transparentPosition - 3.294118) * 6188.0
                    + LinearStepInfluence(transparentPosition - 1.411765) * 19448.0
                    + LinearStepInfluence(transparentPosition) * 12870.0
                    + LinearStepInfluence(transparentPosition + 1.411765) * 19448.0
                    + LinearStepInfluence(transparentPosition + 3.294118) * 6188.0
                    + LinearStepInfluence(transparentPosition + 5.176471) * 680.0
                    + LinearStepInfluence(transparentPosition + 7.058824) * 17.0) / 65536.0;
            }

            float RecoverStraightBoundaryDistance(float normalizedInfluence)
            {
                if (normalizedInfluence >= 0.5)
                    return 0.0;
                if (normalizedInfluence <= StraightBoundaryInfluence(2.0) + 1.0 / 255.0)
                    return 2.0;

                float minimumDistance = 0.0;
                float maximumDistance = 8.0;
                [unroll]
                for (int iteration = 0; iteration < 12; iteration++)
                {
                    float distance = (minimumDistance + maximumDistance) * 0.5;
                    if (StraightBoundaryInfluence(distance) > normalizedInfluence)
                        minimumDistance = distance;
                    else
                        maximumDistance = distance;
                }

                return (minimumDistance + maximumDistance) * 0.5;
            }

            float ResolveNearestBoundaryDistance(
                float2 uv,
                float centerTargetAlpha,
                float centerCurrentAlpha,
                out float boundaryCurrentAlpha)
            {
                float2 logicSize = _MainTex_TexelSize.zw / max(_FogPresentationTexelScale.xy, 0.0001);
                float2 logicPosition = clamp(uv, 0.0, 1.0) * logicSize;
                float2 cellFraction = frac(logicPosition);
                float nearestDistance = 3.0;
                boundaryCurrentAlpha = centerCurrentAlpha;
                float threshold = centerTargetAlpha + 1.0 / 255.0;
                const float diagonal = 0.70710678;
                const float diagonalNear = 0.38268343;
                const float diagonalFar = 0.92387953;
                const float2 directions[16] = {
                    float2(1.0, 0.0), float2(diagonalFar, diagonalNear),
                    float2(diagonal, diagonal), float2(diagonalNear, diagonalFar),
                    float2(0.0, 1.0), float2(-diagonalNear, diagonalFar),
                    float2(-diagonal, diagonal), float2(-diagonalFar, diagonalNear),
                    float2(-1.0, 0.0), float2(-diagonalFar, -diagonalNear),
                    float2(-diagonal, -diagonal), float2(-diagonalNear, -diagonalFar),
                    float2(0.0, -1.0), float2(diagonalNear, -diagonalFar),
                    float2(diagonal, -diagonal), float2(diagonalFar, -diagonalNear)
                };

                [unroll]
                for (int directionIndex = 0; directionIndex < 16; directionIndex++)
                {
                    float2 direction = directions[directionIndex];
                    float2 previousAlpha = float2(centerTargetAlpha, centerCurrentAlpha);
                    float previousDistance = 0.0;
                    [unroll]
                    for (int step = 1; step <= 4; step++)
                    {
                        float distance = step * 0.5;
                        float2 sampleUv = clamp(uv + direction * distance / logicSize, 0.0, 1.0);
                        float2 samplePosition = min(
                            floor(sampleUv * _MainTex_TexelSize.zw),
                            _MainTex_TexelSize.zw - 1.0);
                        float4 packedSample = SampleFogCell(samplePosition);
                        float2 sampleAlpha = float2(
                            UnpackTargetAlpha(packedSample),
                            ResolveCurrentAlpha(packedSample));
                        if (sampleAlpha.x > threshold && sampleAlpha.x > previousAlpha.x)
                        {
                            float crossing = lerp(
                                previousDistance,
                                distance,
                                saturate((threshold - previousAlpha.x) / (sampleAlpha.x - previousAlpha.x)));
                            if (crossing < nearestDistance)
                            {
                                nearestDistance = crossing;
                                boundaryCurrentAlpha = lerp(
                                    previousAlpha.y,
                                    sampleAlpha.y,
                                    saturate((crossing - previousDistance) / max(0.0001, distance - previousDistance)));
                            }
                        }
                        previousAlpha = sampleAlpha;
                        previousDistance = distance;
                    }
                }

                return nearestDistance;
            }

            float SampleTargetAlphaContinuous(float2 uv)
            {
                float2 size = _MainTex_TexelSize.zw;
                float2 position = clamp(uv, 0.0, 1.0) * size - 0.5;
                float2 baseCell = floor(position);
                float2 fraction = frac(position);
                float a00 = UnpackTargetAlpha(SampleFogCell(baseCell));
                float a10 = UnpackTargetAlpha(SampleFogCell(baseCell + float2(1.0, 0.0)));
                float a01 = UnpackTargetAlpha(SampleFogCell(baseCell + float2(0.0, 1.0)));
                float a11 = UnpackTargetAlpha(SampleFogCell(baseCell + float2(1.0, 1.0)));
                return lerp(lerp(a00, a10, fraction.x), lerp(a01, a11, fraction.x), fraction.y);
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

            float ResolveSpatialPresentationAlpha(float2 uv, float centerTargetAlpha, float centerCurrentAlpha)
            {
                float continuousTargetAlpha = SampleTargetAlphaContinuous(uv);
                if (continuousTargetAlpha <= centerTargetAlpha + 1.0 / 255.0)
                    return centerCurrentAlpha;
                return SampleCurrentAlphaContinuous(uv);
            }

            float MoveTowardAlpha(float startAlpha, float targetAlpha, float elapsed)
            {
                float delta = _FogFadeSpeed * max(0.0, elapsed);
                return startAlpha < targetAlpha
                    ? min(startAlpha + delta, targetAlpha)
                    : max(startAlpha - delta, targetAlpha);
            }

            float ResolveBoundaryAlpha(
                float2 uv,
                float centerTargetAlpha,
                float centerCurrentAlpha,
                float centerStartAlpha,
                float centerStartTime)
            {
                float2 worldSize = _FogWorldSize.xy;
                if (worldSize.x <= 0.0 || worldSize.y <= 0.0)
                    worldSize = _MainTex_TexelSize.zw;
                float nearestDistance = _FogBoundaryFadeDistance;
                float boundaryTargetAlpha = centerTargetAlpha;
                float threshold = centerTargetAlpha + 1.0 / 255.0;
                const float2 directions[8] = {
                    float2(1.0, 0.0), float2(0.70710678, 0.70710678),
                    float2(0.0, 1.0), float2(-0.70710678, 0.70710678),
                    float2(-1.0, 0.0), float2(-0.70710678, -0.70710678),
                    float2(0.0, -1.0), float2(0.70710678, -0.70710678)
                };
                [unroll]
                for (int directionIndex = 0; directionIndex < 8; directionIndex++)
                {
                    float2 direction = directions[directionIndex];
                    float previousAlpha = centerTargetAlpha;
                    float previousDistance = 0.0;
                    float crossingDistance = _FogBoundaryFadeDistance + 1.0;
                    float darkerAlpha = centerTargetAlpha;
                    [unroll]
                    for (int step = 1; step <= 8; step++)
                    {
                        float distance = _FogBoundaryFadeDistance * step / 8.0;
                        float2 sampleUv = clamp(uv + direction * distance / worldSize, 0.0, 1.0);
                        float sampleAlpha = SampleTargetAlphaContinuous(sampleUv);
                        if (crossingDistance > _FogBoundaryFadeDistance
                            && sampleAlpha > threshold
                            && sampleAlpha > previousAlpha)
                        {
                            crossingDistance = lerp(
                                previousDistance,
                                distance,
                                saturate((threshold - previousAlpha) / max(0.0001, sampleAlpha - previousAlpha)));
                        }
                        if (crossingDistance <= _FogBoundaryFadeDistance && distance >= crossingDistance)
                            darkerAlpha = max(darkerAlpha, sampleAlpha);
                        previousAlpha = sampleAlpha;
                        previousDistance = distance;
                    }
                    if (crossingDistance <= nearestDistance)
                    {
                        nearestDistance = crossingDistance;
                        boundaryTargetAlpha = max(centerTargetAlpha, darkerAlpha);
                    }
                }

                if (nearestDistance > _FogBoundaryFadeDistance)
                    return centerCurrentAlpha;

                float spatialTargetAlpha = lerp(
                    centerTargetAlpha,
                    boundaryTargetAlpha,
                    saturate(1.0 - nearestDistance / max(0.0001, _FogBoundaryFadeDistance)));
                if (_FogUsesTimedTransitions < 0.5)
                    return spatialTargetAlpha;
                return MoveTowardAlpha(centerStartAlpha, spatialTargetAlpha, _Time.y - centerStartTime);
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
                color.a = _FogSpatialTargetsReady > 0.5
                    ? ResolveSpatialPresentationAlpha(input.uv, UnpackTargetAlpha(packedFog), ResolveCurrentAlpha(packedFog))
                    : ResolveBoundaryAlpha(
                        input.uv,
                        UnpackTargetAlpha(packedFog),
                        ResolveCurrentAlpha(packedFog),
                        IsPackedFogSample(packedFog) ? packedFog.b : packedFog.a,
                        IsPackedFogSample(packedFog) ? packedFog.a : 0.0);
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
            float _FogSpatialTargetsReady;

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

            float SampleTargetAlphaDiscrete(float2 uv)
            {
                float2 size = _MainTex_TexelSize.zw;
                float2 cell = min(floor(clamp(uv, 0.0, 1.0) * size), size - 1.0);
                return UnpackTargetAlpha(SampleFogCell(cell));
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

            float2 SampleFogAlphaContinuous(float2 uv)
            {
                float2 size = _MainTex_TexelSize.zw;
                float2 texelPosition = clamp(uv, 0.0, 1.0) * size - 0.5;
                float2 baseCell = floor(texelPosition);
                float2 fraction = frac(texelPosition);
                float4 sample00 = SampleFogCell(baseCell);
                float4 sample10 = SampleFogCell(baseCell + float2(1.0, 0.0));
                float4 sample01 = SampleFogCell(baseCell + float2(0.0, 1.0));
                float4 sample11 = SampleFogCell(baseCell + float2(1.0, 1.0));
                float2 a00 = float2(UnpackTargetAlpha(sample00), ResolveCurrentAlpha(sample00));
                float2 a10 = float2(UnpackTargetAlpha(sample10), ResolveCurrentAlpha(sample10));
                float2 a01 = float2(UnpackTargetAlpha(sample01), ResolveCurrentAlpha(sample01));
                float2 a11 = float2(UnpackTargetAlpha(sample11), ResolveCurrentAlpha(sample11));
                return lerp(lerp(a00, a10, fraction.x), lerp(a01, a11, fraction.x), fraction.y);
            }

            float2 SampleFogAlphaRow(
                float2 uv,
                float cellOffsetY,
                out float maximumTargetAlpha,
                out float minimumTargetAlpha,
                out float maximumCurrentAlpha)
            {
                float2 texel = _MainTex_TexelSize.xy;
                float2 rowUv = uv + float2(0.0, cellOffsetY * _FogPresentationTexelScale.y * texel.y);
                float2 alpha0 = SampleFogAlphaContinuous(clamp(rowUv + float2(-7.058824 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                float2 alpha1 = SampleFogAlphaContinuous(clamp(rowUv + float2(-5.176471 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                float2 alpha2 = SampleFogAlphaContinuous(clamp(rowUv + float2(-3.294118 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                float2 alpha3 = SampleFogAlphaContinuous(clamp(rowUv + float2(-1.411765 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                float2 alpha4 = SampleFogAlphaContinuous(clamp(rowUv, 0.0, 1.0));
                float2 alpha5 = SampleFogAlphaContinuous(clamp(rowUv + float2(1.411765 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                float2 alpha6 = SampleFogAlphaContinuous(clamp(rowUv + float2(3.294118 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                float2 alpha7 = SampleFogAlphaContinuous(clamp(rowUv + float2(5.176471 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                float2 alpha8 = SampleFogAlphaContinuous(clamp(rowUv + float2(7.058824 * _FogPresentationTexelScale.x * texel.x, 0.0), 0.0, 1.0));
                maximumTargetAlpha = max(max(max(max(alpha0.x, alpha1.x), max(alpha2.x, alpha3.x)), alpha4.x), max(max(alpha5.x, alpha6.x), max(alpha7.x, alpha8.x)));
                minimumTargetAlpha = min(min(min(min(alpha0.x, alpha1.x), min(alpha2.x, alpha3.x)), alpha4.x), min(min(alpha5.x, alpha6.x), min(alpha7.x, alpha8.x)));
                maximumCurrentAlpha = max(max(max(max(alpha0.y, alpha1.y), max(alpha2.y, alpha3.y)), alpha4.y), max(max(alpha5.y, alpha6.y), max(alpha7.y, alpha8.y)));
                return (alpha0 * 17.0 + alpha1 * 680.0 + alpha2 * 6188.0 + alpha3 * 19448.0 + alpha4 * 12870.0 + alpha5 * 19448.0 + alpha6 * 6188.0 + alpha7 * 680.0 + alpha8 * 17.0) / 65536.0;
            }

            float2 SampleContinuousFogAlpha(
                float2 uv,
                out float maximumTargetAlpha,
                out float minimumTargetAlpha,
                out float maximumCurrentAlpha)
            {
                float maximumTarget0, maximumTarget1, maximumTarget2, maximumTarget3, maximumTarget4;
                float maximumTarget5, maximumTarget6, maximumTarget7, maximumTarget8;
                float minimumTarget0, minimumTarget1, minimumTarget2, minimumTarget3, minimumTarget4;
                float minimumTarget5, minimumTarget6, minimumTarget7, minimumTarget8;
                float maximumCurrent0, maximumCurrent1, maximumCurrent2, maximumCurrent3, maximumCurrent4;
                float maximumCurrent5, maximumCurrent6, maximumCurrent7, maximumCurrent8;
                float2 row0 = SampleFogAlphaRow(uv, -7.058824, maximumTarget0, minimumTarget0, maximumCurrent0);
                float2 row1 = SampleFogAlphaRow(uv, -5.176471, maximumTarget1, minimumTarget1, maximumCurrent1);
                float2 row2 = SampleFogAlphaRow(uv, -3.294118, maximumTarget2, minimumTarget2, maximumCurrent2);
                float2 row3 = SampleFogAlphaRow(uv, -1.411765, maximumTarget3, minimumTarget3, maximumCurrent3);
                float2 row4 = SampleFogAlphaRow(uv, 0.0, maximumTarget4, minimumTarget4, maximumCurrent4);
                float2 row5 = SampleFogAlphaRow(uv, 1.411765, maximumTarget5, minimumTarget5, maximumCurrent5);
                float2 row6 = SampleFogAlphaRow(uv, 3.294118, maximumTarget6, minimumTarget6, maximumCurrent6);
                float2 row7 = SampleFogAlphaRow(uv, 5.176471, maximumTarget7, minimumTarget7, maximumCurrent7);
                float2 row8 = SampleFogAlphaRow(uv, 7.058824, maximumTarget8, minimumTarget8, maximumCurrent8);
                maximumTargetAlpha = max(max(max(max(maximumTarget0, maximumTarget1), max(maximumTarget2, maximumTarget3)), maximumTarget4), max(max(maximumTarget5, maximumTarget6), max(maximumTarget7, maximumTarget8)));
                minimumTargetAlpha = min(min(min(min(minimumTarget0, minimumTarget1), min(minimumTarget2, minimumTarget3)), minimumTarget4), min(min(minimumTarget5, minimumTarget6), min(minimumTarget7, minimumTarget8)));
                maximumCurrentAlpha = max(max(max(max(maximumCurrent0, maximumCurrent1), max(maximumCurrent2, maximumCurrent3)), maximumCurrent4), max(max(maximumCurrent5, maximumCurrent6), max(maximumCurrent7, maximumCurrent8)));
                return (
                    row0 * 17.0 + row1 * 680.0 + row2 * 6188.0 + row3 * 19448.0
                    + row4 * 12870.0
                    + row5 * 19448.0 + row6 * 6188.0 + row7 * 680.0 + row8 * 17.0) / 65536.0;
            }

            float LinearStepInfluence(float transparentPosition)
            {
                return saturate(0.5 - transparentPosition);
            }

            float StraightBoundaryInfluence(float transparentPosition)
            {
                return (
                    LinearStepInfluence(transparentPosition - 7.058824) * 17.0
                    + LinearStepInfluence(transparentPosition - 5.176471) * 680.0
                    + LinearStepInfluence(transparentPosition - 3.294118) * 6188.0
                    + LinearStepInfluence(transparentPosition - 1.411765) * 19448.0
                    + LinearStepInfluence(transparentPosition) * 12870.0
                    + LinearStepInfluence(transparentPosition + 1.411765) * 19448.0
                    + LinearStepInfluence(transparentPosition + 3.294118) * 6188.0
                    + LinearStepInfluence(transparentPosition + 5.176471) * 680.0
                    + LinearStepInfluence(transparentPosition + 7.058824) * 17.0) / 65536.0;
            }

            float RecoverStraightBoundaryDistance(float normalizedInfluence)
            {
                if (normalizedInfluence >= 0.5)
                    return 0.0;
                if (normalizedInfluence <= StraightBoundaryInfluence(2.0) + 1.0 / 255.0)
                    return 2.0;

                float minimumDistance = 0.0;
                float maximumDistance = 8.0;
                [unroll]
                for (int iteration = 0; iteration < 12; iteration++)
                {
                    float distance = (minimumDistance + maximumDistance) * 0.5;
                    if (StraightBoundaryInfluence(distance) > normalizedInfluence)
                        minimumDistance = distance;
                    else
                        maximumDistance = distance;
                }

                return (minimumDistance + maximumDistance) * 0.5;
            }

            float SampleTargetAlphaContinuous(float2 uv)
            {
                float2 size = _MainTex_TexelSize.zw;
                float2 position = clamp(uv, 0.0, 1.0) * size - 0.5;
                float2 baseCell = floor(position);
                float2 fraction = frac(position);
                float a00 = UnpackTargetAlpha(SampleFogCell(baseCell));
                float a10 = UnpackTargetAlpha(SampleFogCell(baseCell + float2(1.0, 0.0)));
                float a01 = UnpackTargetAlpha(SampleFogCell(baseCell + float2(0.0, 1.0)));
                float a11 = UnpackTargetAlpha(SampleFogCell(baseCell + float2(1.0, 1.0)));
                return lerp(lerp(a00, a10, fraction.x), lerp(a01, a11, fraction.x), fraction.y);
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

            float ResolveSpatialPresentationAlpha(float2 uv, float centerTargetAlpha, float centerCurrentAlpha)
            {
                float continuousTargetAlpha = SampleTargetAlphaContinuous(uv);
                if (continuousTargetAlpha <= centerTargetAlpha + 1.0 / 255.0)
                    return centerCurrentAlpha;
                return SampleCurrentAlphaContinuous(uv);
            }

            float MoveTowardAlpha(float startAlpha, float targetAlpha, float elapsed)
            {
                float delta = _FogFadeSpeed * max(0.0, elapsed);
                return startAlpha < targetAlpha
                    ? min(startAlpha + delta, targetAlpha)
                    : max(startAlpha - delta, targetAlpha);
            }

            float ResolveBoundaryAlpha(
                float2 uv,
                float centerTargetAlpha,
                float centerCurrentAlpha,
                float centerStartAlpha,
                float centerStartTime)
            {
                float2 worldSize = _FogWorldSize.xy;
                if (worldSize.x <= 0.0 || worldSize.y <= 0.0)
                    worldSize = _MainTex_TexelSize.zw;
                float nearestDistance = _FogBoundaryFadeDistance;
                float boundaryTargetAlpha = centerTargetAlpha;
                float threshold = centerTargetAlpha + 1.0 / 255.0;
                const float2 directions[8] = {
                    float2(1.0, 0.0), float2(0.70710678, 0.70710678),
                    float2(0.0, 1.0), float2(-0.70710678, 0.70710678),
                    float2(-1.0, 0.0), float2(-0.70710678, -0.70710678),
                    float2(0.0, -1.0), float2(0.70710678, -0.70710678)
                };
                [unroll]
                for (int directionIndex = 0; directionIndex < 8; directionIndex++)
                {
                    float2 direction = directions[directionIndex];
                    float previousAlpha = centerTargetAlpha;
                    float previousDistance = 0.0;
                    float crossingDistance = _FogBoundaryFadeDistance + 1.0;
                    float darkerAlpha = centerTargetAlpha;
                    [unroll]
                    for (int step = 1; step <= 8; step++)
                    {
                        float distance = _FogBoundaryFadeDistance * step / 8.0;
                        float2 sampleUv = clamp(uv + direction * distance / worldSize, 0.0, 1.0);
                        float sampleAlpha = SampleTargetAlphaContinuous(sampleUv);
                        if (crossingDistance > _FogBoundaryFadeDistance
                            && sampleAlpha > threshold
                            && sampleAlpha > previousAlpha)
                        {
                            crossingDistance = lerp(
                                previousDistance,
                                distance,
                                saturate((threshold - previousAlpha) / max(0.0001, sampleAlpha - previousAlpha)));
                        }
                        if (crossingDistance <= _FogBoundaryFadeDistance && distance >= crossingDistance)
                            darkerAlpha = max(darkerAlpha, sampleAlpha);
                        previousAlpha = sampleAlpha;
                        previousDistance = distance;
                    }
                    if (crossingDistance <= nearestDistance)
                    {
                        nearestDistance = crossingDistance;
                        boundaryTargetAlpha = max(centerTargetAlpha, darkerAlpha);
                    }
                }
                if (nearestDistance > _FogBoundaryFadeDistance)
                    return centerCurrentAlpha;

                float spatialTargetAlpha = lerp(
                    centerTargetAlpha,
                    boundaryTargetAlpha,
                    saturate(1.0 - nearestDistance / max(0.0001, _FogBoundaryFadeDistance)));
                if (_FogUsesTimedTransitions < 0.5)
                    return spatialTargetAlpha;
                return MoveTowardAlpha(centerStartAlpha, spatialTargetAlpha, _Time.y - centerStartTime);
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
                color.a = _FogSpatialTargetsReady > 0.5
                    ? ResolveSpatialPresentationAlpha(input.uv, UnpackTargetAlpha(packedFog), ResolveCurrentAlpha(packedFog))
                    : ResolveBoundaryAlpha(
                        input.uv,
                        UnpackTargetAlpha(packedFog),
                        ResolveCurrentAlpha(packedFog),
                        IsPackedFogSample(packedFog) ? packedFog.b : packedFog.a,
                        IsPackedFogSample(packedFog) ? packedFog.a : 0.0);
                return color * _Color;
            }
            ENDCG
        }
    }
}
