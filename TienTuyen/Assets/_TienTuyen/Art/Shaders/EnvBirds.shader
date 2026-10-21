// Birds over the valley (EnvironmentSkyLife, EnvSkyLife.hlsl): each one a body and two wings, flown round its circle
// and beating its wings in the vertex stage; squeezed like EnvMountains. Vertex: position the circle's centre; normal
// the bird's local corner (x span, y lift, z forward); tangent (radius, angular speed, phase, half span); colour
// its plumage.
Shader "TienTuyen/Environment/Birds"
{
    Properties
    {
        _Squeeze ("Squeeze start (m), scale (m), 30 km at (m), terrain half size (m)", Vector) = (300, 15, 414, 340)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest+45" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
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

            CBUFFER_START(UnityPerMaterial)
                float4 _Squeeze;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 virtualWS : TEXCOORD0; half4 color : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                half beat;
                output.virtualWS = BirdPosition(TransformObjectToWorld(input.positionOS.xyz), input.normalOS, input.tangentOS, _Time.y, beat);
                float3 camera = GetCameraPositionWS();
                output.positionCS = TransformWorldToHClip(EnvSqueeze(output.virtualWS, camera, _Squeeze));
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                Light sun = GetMainLight();
                half3 lit = input.color.rgb * (SampleSH(half3(0, 1, 0)) + sun.color * (0.25 + 0.5 * saturate(_EnvSunDir.y)));
                return half4(EnvApplyHaze(lit, input.virtualWS), 1);
            }
            ENDHLSL
        }
    }
}
