// Простой материал эффектов для HDRP (VFX-инструкция v3, §1.3: общий Unlit-материал частиц).
// HDRP/Unlit не берёт цвет вершин, поэтому частицы (цвет и прозрачность по времени жизни), шлейф меча
// и тень птицы рисуются этим шейдером. Яркость — в нитах: экспозиция дневная, кадр уже умножен на неё.
// _Mode: 0 — обычная прозрачность, 1 — сложение (свечение), 2 — затемнение (умножение: тень, вмятины).
Shader "TanShadow/VfxUnlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _Nits ("Brightness, nits", Float) = 500
        [Enum(Alpha, 0, Additive, 1, Multiply, 2)] _Mode ("Mode", Float) = 0
        [HideInInspector] _SrcBlend ("", Float) = 5
        [HideInInspector] _DstBlend ("", Float) = 10
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "VfxUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _Color;
            float _Nits;
            float _Mode;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.color = input.color;
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color * input.color;
                if (_Mode > 1.5)
                    return float4(lerp(float3(1, 1, 1), c.rgb, saturate(c.a)), 1);   // Blend DstColor Zero
                float3 rgb = c.rgb * (_Nits * GetCurrentExposureMultiplier());
                if (_Mode > 0.5)
                    return float4(rgb * c.a, 1);                                    // Blend One One
                return float4(rgb, c.a);                                            // Blend SrcAlpha OneMinusSrcAlpha
            }
            ENDHLSL
        }
    }
}
