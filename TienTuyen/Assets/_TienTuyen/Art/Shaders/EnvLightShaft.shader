// Additive sun shafts through the canopy edge. UV.x runs across the shaft,
// UV.y from the canopy (0) to the ground (1); vertex colour R seeds each shaft
// and A sets its strength. Soft-edged and streaked with drifting dust, they
// taper towards the ground, scatter mostly when looking towards the sun, and
// fade near the camera and when seen edge-on, so they read as lit air rather
// than geometry and never smear over gameplay.
Shader "TienTuyen/Environment/LightShaft"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.8, 0.5, 1)
        _Intensity ("Intensity", Float) = 0.35
        _NearFade ("Near fade distance", Float) = 7
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "EnvCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                float _NearFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                half strength : TEXCOORD4;
                half seed : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.strength = input.color.a;
                output.seed = input.color.r * 31.0;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float x = saturate(input.uv.x), y = saturate(input.uv.y);
                // Feathered edges, broken into a few streaks of lit dust that drift slowly.
                float edge = sin(x * PI);
                float across = edge * edge * edge;
                float streaks = 0.35 + 0.65 * EnvValueNoise(float2(x * 5.0 + input.seed, y * 0.7 + _Time.y * 0.03));
                // Brightest just below the canopy, thinning out long before the ground.
                float along = smoothstep(0.0, 0.3, y) * pow(1.0 - y, 1.6);
                along *= 0.55 + 0.45 * EnvValueNoise(float2(y * 3.0 - _Time.y * 0.05 + input.seed * 1.7, x * 1.5));
                float3 toCamera = GetCameraPositionWS() - input.positionWS;
                float distance = length(toCamera);
                float3 view = toCamera / max(distance, 0.001);
                float near = saturate((distance - _NearFade * 0.4) / _NearFade);
                float facing = abs(dot(normalize(input.normalWS), view));
                // Forward scattering: shafts glow when looking into the light and all but vanish with the sun behind.
                float phase = 0.18 + 0.82 * pow(saturate(dot(-view, _EnvSunDir.xyz) * 0.5 + 0.5), 4.0);
                half3 color = _Color.rgb * (_Intensity * across * streaks * along * near * phase * smoothstep(0.1, 0.6, facing) * input.strength);
                color = MixFogColor(color, half3(0, 0, 0), input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
