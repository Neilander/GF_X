Shader "AAAGame/UI/CardEdgeGlow"
{
    Properties
    {
        [PerRendererData][MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor] _Color ("Tint", Color) = (1, 1, 1, 1)

        [HDR] _OutlineColor_0 ("Glow Color A", Color) = (0.78, 0.94, 1, 1)
        [HDR] _OutlineColor_1 ("Glow Color B", Color) = (1, 1, 1, 1)
        _OutlineMinAlpha ("Glow Threshold", Range(0, 1)) = 0.04
        _OutlineMaxAlpha ("Glow Strength", Range(0, 2)) = 0.55
        _GlowSpeed ("Glow Speed", Range(0, 10)) = 2.2
        _OutLineWidth ("Glow Width", Range(0, 8)) = 1.35
        _Overall_Alpha ("Overall Alpha", Range(0, 1)) = 1

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _ClipRect ("Clip Rect", Vector) = (-32767, -32767, 32767, 32767)
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    CGINCLUDE
        #include "UnityCG.cginc"
        #include "UnityUI.cginc"

        sampler2D _MainTex;
        fixed4 _TextureSampleAdd;
        fixed4 _Color;
        float4 _MainTex_TexelSize;
        float4 _ClipRect;

        half4 _OutlineColor_0;
        half4 _OutlineColor_1;
        half _OutlineMinAlpha;
        half _OutlineMaxAlpha;
        half _GlowSpeed;
        half _OutLineWidth;
        half _Overall_Alpha;

        struct appdata_t
        {
            float4 vertex : POSITION;
            float4 color : COLOR;
            float2 texcoord : TEXCOORD0;
        };

        struct v2f
        {
            float4 vertex : SV_POSITION;
            fixed4 color : COLOR;
            float2 texcoord : TEXCOORD0;
            float4 worldPosition : TEXCOORD1;
        };

        v2f vert(appdata_t input)
        {
            v2f output;
            output.worldPosition = input.vertex;
            output.vertex = UnityObjectToClipPos(output.worldPosition);
            output.texcoord = input.texcoord;

            #ifdef UNITY_HALF_TEXEL_OFFSET
                output.vertex.xy += (_ScreenParams.zw - 1.0) * float2(-1, 1);
            #endif

            output.color = input.color * _Color;
            return output;
        }

        inline half SampleSpriteAlpha(float2 uv)
        {
            return (tex2D(_MainTex, uv) + _TextureSampleAdd).a;
        }

        inline half ComputeGlowMask(float2 uv, half centerAlpha)
        {
            float2 texelStep = _MainTex_TexelSize.xy * max((float)_OutLineWidth, 0.001);
            half maxAlpha = 0.0h;
            half minAlpha = 1.0h;
            half sampleAlpha = 0.0h;

            #define SAMPLE_NEIGHBOR(dx, dy) \
                sampleAlpha = SampleSpriteAlpha(uv + float2(dx, dy) * texelStep); \
                maxAlpha = max(maxAlpha, sampleAlpha); \
                minAlpha = min(minAlpha, sampleAlpha);

            SAMPLE_NEIGHBOR(1.0, 0.0)
            SAMPLE_NEIGHBOR(-1.0, 0.0)
            SAMPLE_NEIGHBOR(0.0, 1.0)
            SAMPLE_NEIGHBOR(0.0, -1.0)
            SAMPLE_NEIGHBOR(0.7071, 0.7071)
            SAMPLE_NEIGHBOR(-0.7071, 0.7071)
            SAMPLE_NEIGHBOR(0.7071, -0.7071)
            SAMPLE_NEIGHBOR(-0.7071, -0.7071)

            #undef SAMPLE_NEIGHBOR

            half outerEdge = saturate(maxAlpha - centerAlpha);
            half innerEdge = saturate(centerAlpha - minAlpha) * centerAlpha;
            half edgeMask = max(outerEdge, innerEdge * 0.65h);
            return smoothstep(_OutlineMinAlpha, 1.0h, edgeMask);
        }

        fixed4 frag(v2f input) : SV_Target
        {
            half4 baseColor = (tex2D(_MainTex, input.texcoord) + _TextureSampleAdd) * input.color;
            half glowMask = ComputeGlowMask(input.texcoord, baseColor.a);
            half pulse = 0.5h + 0.5h * sin(_Time.y * _GlowSpeed);
            half4 glowColor = lerp(_OutlineColor_0, _OutlineColor_1, pulse);
            half glowAlpha = saturate(glowMask * _OutlineMaxAlpha * glowColor.a);

            half3 finalRgb = baseColor.rgb + glowColor.rgb * glowAlpha;
            half finalAlpha = saturate(max(baseColor.a, glowAlpha));
            half4 color = half4(finalRgb, finalAlpha) * _Overall_Alpha;

            #if UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
            #endif

            #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001h);
            #endif

            return color;
        }
    ENDCG

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "CardEdgeGlowURP"

            CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #pragma multi_compile_local __ UNITY_UI_CLIP_RECT
                #pragma multi_compile_local __ UNITY_UI_ALPHACLIP
            ENDCG
        }
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "CardEdgeGlow"

            CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #pragma multi_compile_local __ UNITY_UI_CLIP_RECT
                #pragma multi_compile_local __ UNITY_UI_ALPHACLIP
            ENDCG
        }
    }
}
