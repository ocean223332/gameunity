// Still water in bomb craters: dark tannin body, analytic sky reflection,
// slow ripples and a sun glint.
Shader "TienTuyen/Environment/Water"
{
    Properties
    {
        _DeepColor ("Deep colour", Color) = (0.06, 0.07, 0.05, 1)
        _RippleScale ("Ripple scale", Float) = 1.6
        _RippleStrength ("Ripple strength", Range(0, 0.3)) = 0.06
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "EnvCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                float _RippleScale;
                half _RippleStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xz * _RippleScale;
                float t = _Time.y * 0.35;
                float2 slope = float2(EnvValueNoise(p + t) - EnvValueNoise(p + float2(0.37, 0) + t),
                                      EnvValueNoise(p * 1.3 - t) - EnvValueNoise(p * 1.3 + float2(0, 0.37) - t));
                float3 normal = normalize(float3(slope.x * _RippleStrength * 10.0, 1.0, slope.y * _RippleStrength * 10.0));
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = 0.04 + 0.96 * pow(1.0 - saturate(dot(normal, view)), 5.0);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                // The crater banks fill most of the reflection, so the sky shows dimmed, never as a bright sheet.
                half3 reflection = EnvSkyColor(reflect(-view, normal)) * 0.5;
                half3 body = _DeepColor.rgb * (SampleSH(float3(0, 1, 0)) + mainLight.color * 0.15);
                half3 color = lerp(body, reflection, saturate(fresnel * 0.8 + 0.06));
                float3 halfVector = normalize(mainLight.direction + view);
                color += mainLight.color * pow(saturate(dot(normal, halfVector)), 380.0) * 6.0 * mainLight.shadowAttenuation;
                color = EnvApplyHaze(color, input.positionWS);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
