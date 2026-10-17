// Golden-hour sky: haze gradient, warm scatter around a low sun and soft
// drifting cloud banks. Colours come from the EnvCommon globals. With a
// panorama (a photographed sky with its sun painted out) the photo replaces
// the gradient and the procedural clouds above the horizon.
Shader "TienTuyen/Environment/Sky"
{
    Properties
    {
        _CloudColor ("Cloud lit colour", Color) = (1, 0.86, 0.68, 1)
        _CloudShade ("Cloud shade colour", Color) = (0.55, 0.58, 0.6, 1)
        _CloudCover ("Cloud cover", Range(0, 1)) = 0.45
        _SunDisk ("Sun disk intensity", Float) = 6
        _Panorama ("Panorama (equirectangular HDR)", 2D) = "black" {}
        _PanoramaWeight ("Panorama weight", Range(0, 1)) = 0
        _PanoramaExposure ("Panorama exposure", Float) = 1
        _PanoramaOffset ("Panorama u offset", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "EnvCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CloudColor;
                half4 _CloudShade;
                half _CloudCover;
                half _SunDisk;
                half _PanoramaWeight;
                half _PanoramaExposure;
                float _PanoramaOffset;
            CBUFFER_END
            TEXTURE2D(_Panorama); SAMPLER(sampler_Panorama);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                half3 color = EnvSkyColor(direction);
                float sun = saturate(dot(direction, _EnvSunDir.xyz));

                // Cloud banks projected on a high plane, thinning towards the horizon.
                float height = max(direction.y, 0.0);
                float2 plane = direction.xz / (height + 0.09);
                float drift = _Time.y * 0.004;
                float density = EnvFbm(plane * 0.55 + float2(drift, drift * 0.4));
                density = smoothstep(1.0 - _CloudCover, 1.0 - _CloudCover + 0.32, density * (0.75 + 0.35 * EnvValueNoise(plane * 0.13)));
                density *= smoothstep(0.0, 0.18, height) * (1.0 - _PanoramaWeight);
                half3 cloud = lerp(_CloudShade.rgb, _CloudColor.rgb, saturate(0.35 + pow(sun, 3.0) * 0.9));
                // Silver lining where clouds pass in front of the sun.
                cloud += _EnvSkyGlow.rgb * pow(sun, 24.0) * 1.5;
                color = lerp(color, cloud, density * 0.78);

                if (_PanoramaWeight > 0)
                {
                    // Turned so the painted-out sun lies under _EnvSunDir; level 0, so the u wrap leaves no seam.
                    float2 uv = float2(frac(atan2(direction.x, direction.z) * (0.5 * INV_PI) + _PanoramaOffset),
                        0.5 + asin(clamp(direction.y, -1.0, 1.0)) * INV_PI);
                    half3 photo = SAMPLE_TEXTURE2D_LOD(_Panorama, sampler_Panorama, uv, 0).rgb * _PanoramaExposure;
                    // Low down the haze gradient stays, so the far ranges still fade into the sky behind them.
                    half weight = _PanoramaWeight * smoothstep(0.0, 0.16, direction.y);
                    color = lerp(color, photo, weight);
                    color += _EnvSkyGlow.rgb * (pow(sun, 6.0) * 0.2 + pow(sun, 48.0) * 0.9) * weight;
                }

                half disk = smoothstep(0.99935, 0.99965, sun);
                color += _EnvSkyGlow.rgb * disk * _SunDisk * (1.0 - density * 0.85);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
