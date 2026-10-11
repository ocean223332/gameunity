// Textured flora (FloraScatter prefabs): bark, leaves and their far cross-cards. Alpha-clipped in every
// pass, so leaf shadows and depth match the cut-outs. Vertex colour: R wind weight, G wind phase,
// B leaf flutter, A occlusion. Each instance gets a small tint shift from its position. Instanced.
Shader "TienTuyen/Environment/Leaf"
{
    Properties
    {
        _BaseMap ("Albedo (A: cut-out)", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha clip", Float) = 0
        _Cutoff ("Cut-out threshold", Range(0, 1)) = 0.45
        [Toggle(_NORMALMAP)] _UseNormalMap ("Normal map", Float) = 0
        [Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0, 2)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
        _Occlusion ("Occlusion strength", Range(0, 1)) = 0.8
        _Translucency ("Backlit translucency", Range(0, 3)) = 0.8
        _Saturation ("Albedo saturation", Range(0, 1.5)) = 0.85
        _TintVariation ("Per-plant tint variation", Range(0, 0.5)) = 0.18
        _WindScale ("Wind response", Range(0, 3)) = 1
        _Flutter ("Leaf flutter (m)", Range(0, 0.2)) = 0.04
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "EnvCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half _BumpScale;
            half _Smoothness;
            half _Occlusion;
            half _Translucency;
            half _Saturation;
            half _TintVariation;
            half _WindScale;
            half _Flutter;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

        struct LeafAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // 0..1 per plant, from where its instance stands.
        float LeafPlantHash() { return EnvHash21(UNITY_MATRIX_M._m03_m23 * 0.37); }

        float3 LeafPositionWS(LeafAttributes input)
        {
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float phase = frac(input.color.g + LeafPlantHash());
            positionWS += EnvWindOffset(positionWS, input.color.r * _WindScale, phase);
            // Leaves shiver along their normal, out of step with the branch sway.
            float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            float t = _Time.y * 7.3 + phase * 40.0 + dot(positionWS, float3(1.7, 2.3, 1.1));
            positionWS += normalWS * (sin(t) * 0.6 + sin(t * 2.7) * 0.4) * _Flutter * input.color.b * input.color.r * _EnvWind.z * 8.0;
            return positionWS;
        }

        void LeafClip(float2 uv)
        {
        #if defined(_ALPHATEST_ON)
            clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a - _Cutoff);
        #endif
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _NORMALMAP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half4 color : TEXCOORD4;
                half tint : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(LeafAttributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = LeafPositionWS(input);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS = normals.normalWS;
                output.tangentWS = float4(normals.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.tint = LeafPlantHash();
                return output;
            }

            half4 Frag(Varyings input
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            ) : SV_Target0
            {
                UNITY_SETUP_INSTANCE_ID(input);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
            #if defined(_ALPHATEST_ON)
                clip(texel.a - _Cutoff);
            #endif
                float3 normalWS = normalize(input.normalWS);
            #if defined(_NORMALMAP)
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                float3 bitangent = input.tangentWS.w * cross(normalWS, input.tangentWS.xyz);
                normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, normalWS)));
            #endif
                // Plants differ a little: some yellower, some darker.
                half shift = (input.tint - 0.5) * 2.0 * _TintVariation;
                half3 albedo = texel.rgb * _BaseColor.rgb * half3(1.0 + shift * 0.6, 1.0 + shift * 0.25, 1.0 - shift * 0.5) * (1.0 - abs(shift) * 0.4);
                albedo = lerp(dot(albedo, half3(0.2126, 0.7152, 0.0722)).xxx, albedo, _Saturation);
                half occlusion = lerp(1.0, input.color.a, _Occlusion);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = occlusion;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);

                // Low sun through the leaves: they glow when the view looks towards the light.
                Light mainLight = GetMainLight(inputData.shadowCoord);
                half towardsSun = pow(saturate(dot(-inputData.viewDirectionWS, mainLight.direction)), 4.0);
                color.rgb += albedo * mainLight.color * mainLight.shadowAttenuation *
                    (towardsSun * 1.4 + 0.1) * _Translucency * input.color.b * occlusion;

                return half4(EnvApplyHaze(color.rgb, input.positionWS), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            ShadowVaryings ShadowVert(LeafAttributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = LeafPositionWS(input);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection)));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target { LeafClip(input.uv); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct DepthVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            DepthVaryings DepthVert(LeafAttributes input)
            {
                DepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformWorldToHClip(LeafPositionWS(input));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target { LeafClip(input.uv); return input.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct NormalsVaryings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            NormalsVaryings NormalsVert(LeafAttributes input)
            {
                NormalsVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformWorldToHClip(LeafPositionWS(input));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 NormalsFrag(NormalsVaryings input
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            ) : SV_Target0
            {
                UNITY_SETUP_INSTANCE_ID(input);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
                LeafClip(input.uv);
                return half4(NormalizeNormalPerPixel(input.normalWS), 0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
