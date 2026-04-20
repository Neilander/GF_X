Shader "Unlit/Edge Glowing"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR]_OutlineColor_0 ("Outline Color_0", Color) = (1,1,1,1)
        [HDR]_OutlineColor_1 ("Outline Color_1", Color) = (1,1,1,1)
        _OutlineMinAlpha ("Outline Min Alpha", Range(0,0.5)) = 0.1
        _OutlineMaxAlpha ("Outline Max Alpha", Range(0,2)) = 0.3
        _GlowSpeed ("Glow Speed", Range(0,10)) = 2
        _OutLineWidth ("Outline Width", Range(0,0.1)) = 0.01
        _Overall_Alpha ("_Overall_Alpha", float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass 
        {
            Name "OUTLINE"
            Tags {"Queue"="Transparent" "RenderType"="Transparent"}
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
    
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _OutlineMinAlpha, _OutlineMaxAlpha, _OutLineWidth, _GlowSpeed;
            float4 _OutlineColor_0, _OutlineColor_1;
            float _Overall_Alpha;

            struct appdata_t {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
    
            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };
    
            v2f vert (appdata_t v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
    
            fixed4 frag (v2f i) : SV_Target {
               
                float4 col = tex2D(_MainTex, i.uv);
                float Alpha = col.a;

                for (int x = -10 ; x < 10; x++)
                {
                    for(int y = -10; y < 10; y++)
                    {
                        float2 offset = (float2(x, y) * _OutLineWidth)/10;
                        Alpha += tex2D(_MainTex, i.uv + offset).a;
                    }
                }
                Alpha /= 300;

                //Alpha = clamp(Alpha,0,1);

                clip(Alpha - _OutlineMinAlpha);
                //clip(_OutlineMaxAlpha - Alpha);

                Alpha = clamp(Alpha,0,_OutlineMaxAlpha);

                float3 OutLine =  lerp(_OutlineColor_0, _OutlineColor_1, (0.5 * sin(_GlowSpeed * _Time.y + 2*i.uv.x) + 0.5)) * Alpha;

                col.a += Alpha;
                col.rgb += OutLine;
                col.a *= pow(_Overall_Alpha,3);

                return col;
            }
            ENDCG
        }
    }
}