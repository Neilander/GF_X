Shader "Hidden/Custom/BuildingOutlineMask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        // 用 mask RT 自己的 depth：多建筑互相遮挡时 ZTest 取最近那个
        ZWrite On
        ZTest LEqual
        Cull Off
        ColorMask R

        Pass
        {
            Name "OutlineMask"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            // fragment 阶段 i.positionHCS 是 raster 后的 screen-space 坐标，z = NDC depth
            // (D3D/Metal reverse-Z: 1=近 0=远)。clear color 0 表示无建筑，所以 epsilon 防极远建筑深度=0 与 clear 混淆。
            half4 frag(Varyings i) : SV_Target
            {
                return half4(max(i.positionHCS.z, 1e-4), 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
