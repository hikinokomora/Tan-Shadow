Shader "TanShadow/Winter/Localized Glow"
{
    Properties
    {
        [NoScaleOffset] _BaseMap("Surface Detail", 2D) = "white" {}
        [HDR] _GlowColor("Diffused Color", Color) = (0.72, 0.021, 0.038, 1)
        [HDR] _CoreColor("Hot Detail Color", Color) = (1, 0.16, 0.035, 1)
        _Glow("Glow Intensity", Range(0, 8)) = 1.8
        _Translucency("Translucency", Range(0, 1)) = 0.3
        [HideInInspector] _RattleBands("Rattle Profile", Float) = 0
        _GrainAmount("Texture Influence", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "HDUnlitShader" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend One OneMinusSrcAlpha, Zero One
            Cull Back
            ZWrite Off
            ZTest LEqual
            Offset -1, -2

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _GlowColor;
                float4 _CoreColor;
                float _Glow;
                float _RattleBands;
                float _Translucency;
                float _GrainAmount;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 rattleUV : TEXCOORD1;
                float4 mask : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionRWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 mask : TEXCOORD3;
                float rattleStation : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionRWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionRWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.mask = input.mask.rgb;
                output.rattleStation = input.rattleUV.x;
                return output;
            }

            float3 RattleMask(float station)
            {
                float4 distanceToGroove = abs(station - float4(0.850, 0.878, 0.908, 0.938));
                distanceToGroove *= rcp(float4(0.0068, 0.0063, 0.0057, 0.0052));
                float distance = min(min(distanceToGroove.x, distanceToGroove.y),
                                     min(distanceToGroove.z, distanceToGroove.w));
                float fade = smoothstep(0.828, 0.848, station) * (1.0 - smoothstep(0.948, 0.964, station));
                float seam = exp(-distance * distance);
                return fade * float3(seam, 0.085 + 0.52 * seam, exp(-3.5 * distance * distance));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 mask = saturate(_RattleBands > 0.5 ? RattleMask(input.rattleStation) : input.mask);
                float core = mask.r;
                float diffusion = mask.g;
                float detail = mask.b;
                clip(max(core, diffusion) - 0.003);

                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionRWS);
                float facing = saturate(dot(normalize(input.normalWS), viewDir));
                float rim = (1.0 - facing) * (1.0 - facing);
                float3 surface = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                float grain = lerp(1.0, 0.45 + 0.55 * sqrt(saturate(Max3(surface.r, surface.g, surface.b))), _GrainAmount);

                float diffuseLight = diffusion * (0.13 + 0.19 * rim);
                float hotLight = 0.78 * pow(core, 1.65) + 0.72 * detail;
                float3 hotColor = lerp(_GlowColor.rgb, _CoreColor.rgb, saturate(core * 0.75 + detail * 0.7));
                float3 emission = _Glow * grain * (diffuseLight * _GlowColor.rgb + hotLight * hotColor);

                float opacity = saturate(diffusion * _Translucency * (0.34 + 0.48 * rim));
                float3 scatter = _GlowColor.rgb * (0.05 + 0.09 * rim) * opacity;
                return float4(emission + scatter, opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
