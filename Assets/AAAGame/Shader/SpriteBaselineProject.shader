Shader "Custom/SpriteBaselineProject"
{
    Properties
    {
        _SpriteTex ("Sprite", 2D) = "white" {}
        _AlphaClip ("Alpha Clip", Range(0,1)) = 0.3
        _BaseA ("Base A (world)", Vector) = (0,0,0,0)
        _BaseB ("Base B (world)", Vector) = (1,0,0,0)
        _BaseOrigin ("Base Origin (world)", Vector) = (0,0,0,0)
        _SpriteSize ("Sprite Size (meters)", Vector) = (1,1,0,0) // x=width, y=height
        _Color ("Tint", Color) = (1,1,1,1)
        _CamRight ("Cam Right", Vector) = (1,0,0,0)
        _CamUp ("Cam Up", Vector) = (0,1,0,0)
        _SpriteUVScale ("Sprite UV Scale", Vector) = (1,1,0,0)
        _SpriteUVOffset ("Sprite UV Offset", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalRenderPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SpriteTex); SAMPLER(sampler_SpriteTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseA;
            float4 _BaseB;
            float4 _BaseOrigin;
            float4 _SpriteSize;   // xy = width, height
            float4 _Color;
            float _AlphaClip;
            float4 _CamRight;
            float4 _CamUp;
            float4 _SpriteUVScale;
            float4 _SpriteUVOffset;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            float3 SafeNormalizeDir(float3 v, float3 fallback)
            {
                float len = length(v);
                return len > 1e-5 ? v / len : fallback;
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                float3 baseOrigin = _BaseOrigin.xyz;

                float spriteWidth = _SpriteSize.x;
                float spriteHeightRaw = _SpriteSize.y;
                float spriteHeight = spriteHeightRaw;

                float3 worldUp = float3(0,1,0);
                float3 camRight = _CamRight.xyz;
                float3 camRightFlat = float3(camRight.x, 0, camRight.z);
                float flatLen = length(camRightFlat);
                float3 right = flatLen > 1e-5 ? camRightFlat / flatLen : float3(1,0,0);
                float3 upProj = worldUp;

                // Compensate screen width loss due to camera tilt: scale width by 1/cos(theta)
                float widthComp = rcp(max(flatLen, 1e-4));
                spriteWidth *= widthComp;

                // Compensate height foreshortening: camera up vs world up
                float upDot = abs(dot(SafeNormalizeDir(_CamUp.xyz, worldUp), worldUp));
                float heightComp = rcp(max(upDot, 1e-4));
                spriteHeight *= heightComp;

                float2 rawUV = IN.uv;
                float2 uvScale = _SpriteUVScale.xy;
                float2 uvOffset = _SpriteUVOffset.xy;
                float2 uvNorm = (rawUV - uvOffset) / max(uvScale, float2(1e-4, 1e-4));

                float u = uvNorm.x - 0.5; // center align width using normalized (0..1) uv
                float v = uvNorm.y;       // bottom at v=0

                // Anchor bottom at explicit world-space origin (collider bottom center)
                float3 origin = baseOrigin;
                float3 posWS = origin + right * (u * spriteWidth) + upProj * (v * spriteHeight);

                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Use raw UV for sampling; normalization only affects geometry
                half4 col = SAMPLE_TEXTURE2D(_SpriteTex, sampler_SpriteTex, IN.uv) * _Color;
                clip(col.a - _AlphaClip);
                return col;
            }
            ENDHLSL
        }
    }
}
