// Soft puffs of mist, spray and smoke (EnvironmentRiverlands): camera-facing quads standing upright, a photographed
// cloud that billows slowly and fades where it meets the ground or water (the depth texture) and close to the camera.
// Water mist scatters the low sun forward, so against the light it glows; smoke is dark and dull. Mist thickens
// with the weather's forest haze (_EnvHaze.x).
// Position = puff centre, UV0 = corner (-1..1), UV1 = (width, height, density, seed), colour R = smoke.
Shader "TienTuyen/Environment/Mist"
{
    Properties
    {
        _MistColor ("Mist", Color) = (0.86, 0.9, 0.92, 1)
        _SmokeColor ("Smoke", Color) = (0.2, 0.19, 0.18, 1)
        _Scatter ("Against the sun: strength, tightness; broad glow; haze thickening", Vector) = (0.9, 8, 0.35, 1.4)
        _Soft ("Fade: into the scene (share of size), near the camera (m)", Vector) = (0.3, 6, 0, 0)
        _Billow ("Billow: scale, speed (1/s), puff texture gain", Vector) = (1.4, 0.05, 2, 0)
        _PuffTex ("Puff (Rocky Hills' cloud, greyscale, black border, clamped)", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "EnvCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _MistColor;
                half4 _SmokeColor;
                float4 _Scatter;
                float4 _Soft;
                float4 _Billow;
            CBUFFER_END

            TEXTURE2D(_PuffTex); SAMPLER(sampler_PuffTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 corner : TEXCOORD0;
                float4 shape : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 corner : TEXCOORD1;
                float4 shape : TEXCOORD2;
                half smoke : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 centre = TransformObjectToWorld(input.positionOS.xyz);
                // Upright: turned to the camera about the vertical, so plumes and banks keep their height.
                float3 toCamera = GetCameraPositionWS() - centre;
                float3 right = normalize(float3(toCamera.z, 0, -toCamera.x) + float3(1e-4, 0, 0));
                float3 positionWS = centre + right * input.corner.x * input.shape.x * 0.5 + float3(0, 1, 0) * input.corner.y * input.shape.y * 0.5;
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.corner = input.corner;
                output.shape = input.shape;
                output.smoke = input.color.r;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 c = input.corner;
                float seed = input.shape.w;
                // A photographed cloud, turned and mirrored per puff so no two match, its density stirred by noise that
                // drifts upward through it.
                float angle = seed * 6.2832;
                float2 turn = float2(cos(angle), sin(angle));
                float2 r = float2(c.x * turn.x - c.y * turn.y, c.x * turn.y + c.y * turn.x) * (frac(seed * 7.13) > 0.5 ? float2(-1, 1) : float2(1, 1));
                half cloud = SAMPLE_TEXTURE2D(_PuffTex, sampler_PuffTex, r * 0.5 + 0.5).r;
                float2 q = c * _Billow.x * float2(input.shape.x / max(input.shape.y, 1.0), 1.0) + seed;
                q.y -= _Time.y * _Billow.y * (1.0 + input.smoke * 3.0);
                half n = EnvFbm(q);
                half blob = saturate(1.0 - length(c));
                half puff = saturate(cloud * _Billow.z * (0.55 + 0.9 * n) * saturate(blob * 3.0));

                // Fade where the puff passes into the ground or water, and close in front of the camera.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float depth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                half soft = saturate((sceneDepth - depth) / max(_Soft.x * min(input.shape.x, input.shape.y), 0.5));
                half near = saturate((depth - _Soft.y) / (_Soft.y * 2.0));
                half thickness = lerp(0.6 + _EnvHaze.x * _Scatter.w, 1.0, input.smoke);
                half alpha = saturate(puff * input.shape.z * soft * near * thickness);

                Light light = GetMainLight();
                float3 toCamera = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half facing = saturate(dot(-toCamera, light.direction));
                half3 ambient = SampleSH(float3(0, 1, 0)) + EnvSkyColor(float3(0, 1, 0)) * 0.3;
                half3 mist = _MistColor.rgb * (ambient + light.color * (0.35 + _Scatter.z * facing * facing + _Scatter.x * pow(facing, _Scatter.y)));
                half3 smoke = _SmokeColor.rgb * (ambient + light.color * (0.5 + 0.8 * pow(facing, 4.0)));
                half3 color = lerp(mist, smoke, input.smoke);
                return half4(EnvApplyHaze(color, input.positionWS), alpha);
            }
            ENDHLSL
        }
    }
}
