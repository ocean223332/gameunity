// Low cloud on the peaks and mist belts between the distant ranges: soft noise cards placed and squeezed
// like EnvMountains (virtual size, logarithmic distance squeeze). Coloured from the sky behind them, lit a little
// on top, and faded out where they meet the terrain so no card ever shows a hard edge.
Shader "TienTuyen/Environment/MountainCloud"
{
    Properties
    {
        _Lit ("Sunlit top (x sky colour)", Float) = 1.12
        _Shade ("Shaded base (x sky colour)", Float) = 0.9
        _Opacity ("Opacity", Range(0, 1)) = 0.5
        _SoftDepth ("Terrain fade (fraction of distance)", Range(0.001, 0.2)) = 0.05
        _Squeeze ("Squeeze start (m), scale (m)", Vector) = (300, 15, 0, 0)
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
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "EnvCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Lit;
                half _Shade;
                half _Opacity;
                half _SoftDepth;
                float4 _Squeeze;
            CBUFFER_END

            // Inverse of the vertex squeeze: drawn distance from the camera back to the virtual one.
            float Unsqueeze(float squeezed)
            {
                return squeezed <= _Squeeze.x ? squeezed : _Squeeze.x + _Squeeze.y * (exp(min((squeezed - _Squeeze.x) / _Squeeze.y, 80.0)) - 1.0);
            }

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 seed : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 virtualWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float2 seed : TEXCOORD2;
                float2 drawn : TEXCOORD3;    // x: eye depth where drawn, y: drawn distance from the camera
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.virtualWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 camera = GetCameraPositionWS();
                float3 view = output.virtualWS - camera;
                float distance = max(length(view), 1e-3);
                float squeezed = distance <= _Squeeze.x ? distance : _Squeeze.x + _Squeeze.y * log(1.0 + (distance - _Squeeze.x) / _Squeeze.y);
                float3 drawnWS = camera + view * (squeezed / distance);
                output.positionCS = TransformWorldToHClip(drawnWS);
                output.drawn = float2(-TransformWorldToView(drawnWS).z, squeezed);
                output.uv = input.uv;
                output.seed = input.seed;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv * 2.0 - 1.0;
                // Lens shape, flatter underneath, broken up by slowly drifting noise.
                half shape = saturate(1.0 - pow(abs(centered.x), 2.0)) * saturate(1.0 - pow(abs(centered.y + 0.15), 1.5));
                float2 q = float2(input.uv.x * 4.0 * input.seed.y, input.uv.y * 1.3) + input.seed.x * 17.0 + float2(_Time.y * 0.003, 0);
                half noise = EnvFbm(q * 2.2);
                // Wide, soft threshold and a feathered rim: wisps thin out instead of ending on a line.
                half alpha = smoothstep(0.0, 1.0, noise * 2.0 - (1.45 - shape * 1.25)) * smoothstep(0.0, 0.6, shape) * _Opacity;

                float3 view = input.virtualWS - GetCameraPositionWS();
                float distance = length(view);
                float3 viewDir = view / distance;
                // Soft contact: the terrain behind the card, unsqueezed along the same view ray, against the card.
                float sceneEye = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS)), _ZBufferParams);
                float sceneDistance = Unsqueeze(sceneEye * input.drawn.y / max(input.drawn.x, 1e-3));
                alpha *= saturate((sceneDistance - distance) / (distance * _SoftDepth));

                half3 sky = EnvHazeColor(viewDir);
                half towards = pow(saturate(dot(viewDir, _EnvSunDir.xyz)), 3.0);
                half3 color = sky * lerp(_Shade, _Lit, saturate(input.uv.y * 0.8 + noise * 0.4));
                color += _EnvSkyGlow.rgb * towards * (1.0 - shape) * 0.4;     // thin edges glow against the sun
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
