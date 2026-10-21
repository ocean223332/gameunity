// The valley cloud's skirt (EnvironmentBackdrop): a ring far behind the ranges carrying the same cloud band as
// their slopes (EnvKarst.hlsl), seen where the view between the ranges and the towers opens onto the sky. Squeezed
// like EnvMountains; blended over the sky by the band's cover. UV0.x: degrees of elevation above the level of all
// the ranges.
Shader "TienTuyen/Environment/MountainCloud"
{
    Properties
    {
        _Aerial ("Aerial perspective: distance (m), strength, near-layer darkening", Vector) = (3000, 0.85, 0.25, 0)
        _AerialTint ("Aerial perspective tint (x horizon luminance)", Color) = (0.74, 0.86, 1.0, 1)
        _CloudSea ("Valley cloud: thickness over the nearer ridge (deg), billow, start (m), strength", Vector) = (3, 1.0, 2200, 1)
        _Squeeze ("Squeeze start (m), scale (m), 30 km at (m), terrain half size (m)", Vector) = (300, 15, 414, 340)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-100" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "EnvCommon.hlsl"
            #include "EnvSqueeze.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Aerial;
                half4 _AerialTint;
                float4 _CloudSea;
                float4 _Squeeze;
            CBUFFER_END

            #include "EnvKarstCloud.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 virtualWS : TEXCOORD0; float rise : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.virtualWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 camera = GetCameraPositionWS();
                output.positionCS = TransformWorldToHClip(EnvSqueeze(output.virtualWS, camera, _Squeeze));
                output.rise = input.uv.x;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 view = input.virtualWS - GetCameraPositionWS();
                float distance = length(view);
                float3 viewDir = view / distance;
                half inside, lumps;
                half cover = KarstValleyCloud(viewDir, input.rise, distance, _Time.y, inside, lumps);
                return half4(KarstCloudColor(viewDir, inside, lumps, GetMainLight().color), cover);
            }
            ENDHLSL
        }
    }
}
