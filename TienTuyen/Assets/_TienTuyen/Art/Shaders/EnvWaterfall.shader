// Falling water (EnvironmentRiverlands): sheets of glassy water turning white as they fall, streaked along the drop
// and racing faster the further they fall, frayed into strands at the edges; a fainter veil of spray in front; foam
// discs spreading where a tier lands. Thin water scatters light: against a low sun it glows.
// UV0 = (metres across, seconds since the lip; foam: metres around, radius), UV1 = (share of the whole drop fallen,
// 0..1 across or centre to rim, tier, layer: 0 sheet, 1 veil, 2 foam).
Shader "TienTuyen/Environment/Waterfall"
{
    Properties
    {
        _Glass ("Water at the lip", Color) = (0.36, 0.44, 0.42, 1)
        _Foam ("White water", Color) = (0.9, 0.93, 0.93, 1)
        _Opacity ("Opacity: sheet, veil, foam", Vector) = (0.88, 0.32, 0.85, 0)
        _Streaks ("Streaks: across (1/m), along (1/s), speed, contrast", Vector) = (0.9, 2.4, 1.0, 1.5)
        _FoamTex ("White water (Rocky Hills' waterfall foam, greyscale, tiling)", 2D) = "grey" {}
        _FoamMap ("White water texture: tile across (m), tile along (s of flight), share, speed", Vector) = (6, 1.2, 0.65, 1)
        _Backlight ("Against the sun: strength, tightness; ambient", Vector) = (1.0, 5, 0.8, 0)
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
            #include "EnvCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Glass;
                half4 _Foam;
                float4 _Opacity;
                float4 _Streaks;
                float4 _Backlight;
                float4 _FoamMap;
            CBUFFER_END

            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 data : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.data = input.data;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float layer = input.data.w;
                half fallen = saturate(input.data.x);
                half edge = input.data.y;
                // Streaks run along the drop: narrow across, long along, and the pattern moves at a constant rate in
                // flight time, so it stretches as the water speeds up. A slow churn breaks the sheet into strands.
                float across = input.uv.x * _Streaks.x;
                float along = (input.uv.y - _Time.y * _Streaks.z) * _Streaks.y;
                float n = EnvValueNoise(float2(across, along * 0.35)) * 0.6 + EnvValueNoise(float2(across * 2.7 + 3.1, along * 0.8)) * 0.4;
                float churn = EnvValueNoise(float2(across * 0.45 - 7.0, along * 0.12 + layer * 5.0));
                // The photographed white water rides on the streaks, moving with them; each layer its own part of it.
                float2 foamUV = float2(input.uv.x / _FoamMap.x + layer * 0.37, (input.uv.y - _Time.y * _Streaks.z * _FoamMap.w) / _FoamMap.y);
                half foam = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, foamUV).r;
                n = lerp(n, foam * 1.35, _FoamMap.z);
                half body;
                if (layer < 1.5)
                    body = smoothstep(0.0, 0.16 + 0.12 * churn, edge) * smoothstep(1.0, 0.84 - 0.12 * churn, edge);
                else
                    body = (1.0 - smoothstep(0.5 + 0.3 * churn, 1.0, edge)) * smoothstep(0.0, 0.15, edge);
                half strands = saturate((n - 0.5) * _Streaks.w + 0.5 + fallen * 0.3);
                half opacity = layer < 0.5 ? _Opacity.x : (layer < 1.5 ? _Opacity.y * (0.4 + 0.6 * fallen) : _Opacity.z);
                half alpha = saturate(body * strands * opacity * (0.7 + 0.5 * fallen));

                // White water: glassy at the lip, aerated and white lower down.
                half white = saturate(fallen * 1.3 - 0.1 + (n - 0.5) * 0.5);
                if (layer > 1.5) white = 1.0;
                half3 albedo = lerp(_Glass.rgb, _Foam.rgb, white);

                Light light = GetMainLight();
                float3 toCamera = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 normal = normalize(input.normalWS);
                // Thin and scattering: lit through from behind as well as in front.
                half wrap = abs(dot(normal, light.direction)) * 0.55 + 0.45;
                half against = pow(saturate(dot(-toCamera, light.direction)), _Backlight.y);
                half3 ambient = SampleSH(float3(0, 1, 0)) * _Backlight.z + EnvSkyColor(float3(0, 1, 0)) * 0.25;
                half3 color = albedo * (light.color * wrap * 0.75 + ambient)
                    + light.color * against * _Backlight.x * albedo * (0.35 + 0.65 * white);
                return half4(EnvApplyHaze(color, input.positionWS), alpha);
            }
            ENDHLSL
        }
    }
}
