// Far shafts of sun through the cloud onto the valleys (EnvironmentSkyLife, EnvSkyLife.hlsl); the near ones round the
// station are EnvLightShaft's. Additive strips shaped from the sun's direction in the vertex stage, squeezed like
// EnvMountains so they stand among the ranges. They show only where the sky or the far haze lies behind them
// (ShaftBackdrop, from the depth texture unsqueezed): no veil over a near tower's face.
// Vertex: position the shaft's anchor on the ground; normal (half width m, cloud height m, seed); uv corner.
Shader "TienTuyen/Environment/FarLightShafts"
{
    Properties
    {
        _Strength ("Strength", Range(0, 3)) = 0.6
        _Tint ("Tint (x sun colour)", Color) = (1, 0.93, 0.8, 1)
        _Squeeze ("Squeeze start (m), scale (m), 30 km at (m), terrain half size (m)", Vector) = (300, 15, 414, 340)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-90" "IgnoreProjector" = "True" }
        Blend One One
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
            #include "EnvSkyLife.hlsl"
            #include "EnvSqueeze.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Strength;
                half4 _Tint;
                float4 _Squeeze;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 virtualWS : TEXCOORD0; float3 corner : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 camera = GetCameraPositionWS();
                output.virtualWS = ShaftPosition(TransformObjectToWorld(input.positionOS.xyz), input.uv, input.normalOS, camera);
                output.positionCS = TransformWorldToHClip(EnvSqueeze(output.virtualWS, camera, _Squeeze));
                output.corner = float3(input.uv, input.normalOS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 view = input.virtualWS - GetCameraPositionWS();
                float distance = length(view);
                float3 viewDir = view / distance;
                float3 camera = GetCameraPositionWS();
                float sceneEye = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS)), _ZBufferParams);
                float behind = EnvUnsqueeze(sceneEye / max(dot(viewDir, -UNITY_MATRIX_V[2].xyz), 1e-3), viewDir, camera, _Squeeze);
                half3 sun = GetMainLight().color;
                // Shafts need a sun to cast them and air to show them: none under an overcast, most in mist.
                half weather = saturate((dot(sun, half3(0.2126, 0.7152, 0.0722)) - 0.9) / 1.0) * (0.5 + _EnvHaze.x);
                half amount = ShaftAmount(input.corner.xy, input.corner.z, viewDir, distance, _Time.y) * weather * _Strength * ShaftBackdrop(behind);
                amount *= 1.0 - 0.6 * EnvHazeAmount(distance, input.virtualWS.y);
                return half4(sun * _Tint.rgb * amount, 0);
            }
            ENDHLSL
        }
    }
}
