// Textured bark and rock for the batched procedural flora meshes, which carry no UVs: a scanned set (albedo with
// height in A, normal map, mask with R occlusion and G smoothness) projected along the three world axes.
// Graded per material: _Burn turns the bark to charcoal and ash with pale wood where the trunk broke off, _Moss
// grows moss on the faces that look up, _Dust splashes the ground colour on the lowest part.
// Vertex colour from FlatMeshBuilder: G per-instance tint, B height ratio above the instance base (R wind and A unused).
Shader "TienTuyen/Environment/Surface"
{
    Properties
    {
        _AlbedoMap ("Albedo (A height)", 2D) = "grey" {}
        _NormalMap ("Normal", 2D) = "bump" {}
        _MaskMap ("Mask (R occlusion, G smoothness)", 2D) = "white" {}
        _Tiling ("Texture size (m)", Float) = 1.2
        _BaseColor ("Colour (x albedo)", Color) = (1, 1, 1, 1)
        _Saturation ("Albedo saturation", Range(0, 1.5)) = 1
        _TintLow ("Tint variation A (multiplier)", Color) = (0.9, 0.9, 0.88, 1)
        _TintHigh ("Tint variation B (multiplier)", Color) = (1.08, 1.06, 1.02, 1)
        _Occlusion ("Height occlusion strength", Range(0, 1)) = 0.35
        _Smoothness ("Smoothness (x mask)", Range(0, 1)) = 0.5
        _NormalStrength ("Normal strength", Range(0, 2)) = 1
        _Burn ("Charred", Range(0, 1)) = 0
        _CharDark ("Charcoal in cracks", Color) = (0.12, 0.12, 0.125, 1)
        _CharLight ("Charcoal on plates", Color) = (0.52, 0.52, 0.53, 1)
        _Ash ("Ash", Color) = (0.7, 0.69, 0.68, 1)
        _AshAmount ("Ash cover", Range(0, 1)) = 0.6
        _Wood ("Exposed wood", Color) = (0.72, 0.64, 0.52, 1)
        _WoodFrom ("Exposed wood above height ratio", Range(0, 1)) = 0.95
        _Moss ("Moss cover", Range(0, 1)) = 0
        _MossSlope ("Moss from normal.y", Range(0, 1)) = 0.72
        _MossDark ("Moss dark", Color) = (0.12, 0.16, 0.06, 1)
        _MossLight ("Moss light", Color) = (0.26, 0.31, 0.12, 1)
        _Dust ("Ground dust", Color) = (0.6, 0.49, 0.38, 1)
        _DustBand ("Dust: from, to height ratio, strength", Vector) = (0, 0.1, 0.5, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "EnvCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Tiling;
            half4 _BaseColor;
            half _Saturation;
            half4 _TintLow;
            half4 _TintHigh;
            half _Occlusion;
            half _Smoothness;
            half _NormalStrength;
            half _Burn;
            half4 _CharDark;
            half4 _CharLight;
            half4 _Ash;
            half _AshAmount;
            half4 _Wood;
            half _WoodFrom;
            half _Moss;
            half _MossSlope;
            half4 _MossDark;
            half4 _MossLight;
            half4 _Dust;
            half4 _DustBand;
        CBUFFER_END

        struct SurfaceAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_AlbedoMap); SAMPLER(sampler_AlbedoMap);
            TEXTURE2D(_NormalMap);
            TEXTURE2D(_MaskMap);

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(SurfaceAttributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color;
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
                float3 p = input.positionWS;
                half3 geometric = normalize(input.normalWS);
                half instance = input.color.g;
                half rise = input.color.b;

                // Three planar projections; the vertical ones keep the texture's v along world up, so bark furrows
                // run up the trunk. Each instance shifts the pattern so neighbouring trunks and rocks differ.
                half3 weights = pow(abs(geometric), 4.0);
                weights /= weights.x + weights.y + weights.z;
                float2 shift = float2(instance * 7.31, instance * 3.17);
                float2 uvX = p.zy / _Tiling + shift;
                float2 uvY = p.xz / _Tiling + shift.yx;
                float2 uvZ = p.xy / _Tiling - shift;
                half4 albedoHeight = SAMPLE_TEXTURE2D(_AlbedoMap, sampler_AlbedoMap, uvX) * weights.x
                    + SAMPLE_TEXTURE2D(_AlbedoMap, sampler_AlbedoMap, uvY) * weights.y
                    + SAMPLE_TEXTURE2D(_AlbedoMap, sampler_AlbedoMap, uvZ) * weights.z;
                half2 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_AlbedoMap, uvX).rg * weights.x
                    + SAMPLE_TEXTURE2D(_MaskMap, sampler_AlbedoMap, uvY).rg * weights.y
                    + SAMPLE_TEXTURE2D(_MaskMap, sampler_AlbedoMap, uvZ).rg * weights.z;
                // Whiteout blend of the three tangent-space normals onto the surface normal.
                half3 normalX = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_AlbedoMap, uvX), _NormalStrength);
                half3 normalY = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_AlbedoMap, uvY), _NormalStrength);
                half3 normalZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_AlbedoMap, uvZ), _NormalStrength);
                normalX = half3(normalX.xy + geometric.zy, abs(normalX.z) * geometric.x);
                normalY = half3(normalY.xy + geometric.xz, abs(normalY.z) * geometric.y);
                normalZ = half3(normalZ.xy + geometric.xy, abs(normalZ.z) * geometric.z);
                half3 normalWS = normalize(normalX.zyx * weights.x + normalY.xzy * weights.y + normalZ.xyz * weights.z);

                half3 scan = albedoHeight.rgb;
                half height = albedoHeight.a;
                half luma = dot(scan, half3(0.2126, 0.7152, 0.0722));
                half3 albedo = lerp(luma.xxx, scan, _Saturation) * _BaseColor.rgb;
                half smoothness = mask.g * _Smoothness;

                // Charred bark: charcoal from the deep cracks (dark) to the raised plates (lighter, with a dull sheen),
                // grey-white ash settled on some plates, a few patches where the fire only scorched the bark, and pale
                // splintered wood at the broken top.
                float2 trunk = float2(p.x + p.z, p.y);
                half charTone = saturate(height * 1.1 - 0.15 + (luma - 0.12) * 2.5);
                half3 charcoal = lerp(_CharDark.rgb, _CharLight.rgb, charTone);
                half ash = smoothstep(0.35, 0.75, height) * smoothstep(0.25, 0.65, EnvFbm(trunk * float2(1.6, 0.6) + instance * 11.0)) * _AshAmount;
                charcoal = lerp(charcoal, _Ash.rgb * saturate(0.75 + luma * 2.0), ash);
                half scorched = smoothstep(0.62, 0.82, EnvFbm(trunk * 0.6 + 3.1 + instance * 5.0)) * 0.3;
                charcoal = lerp(charcoal, lerp(luma.xxx, scan, 0.45) * 0.6, scorched);
                half wood = smoothstep(_WoodFrom, _WoodFrom + 0.03, rise + (EnvValueNoise(trunk * 6.0) - 0.5) * 0.04);
                charcoal = lerp(charcoal, _Wood.rgb * saturate(0.55 + luma * 2.5), wood);
                albedo = lerp(albedo, charcoal, _Burn);
                smoothness = lerp(smoothness, lerp(0.32 * saturate(height * 1.4), 0.12, max(ash, wood)), _Burn);

                // Moss on the faces that look up, first in the hollows, broken up by noise; it fills the relief.
                half up = lerp(geometric.y, normalWS.y, 0.5) + (EnvFbm(p.xz * 1.5 + p.y) - 0.5) * 0.7 + (0.5 - height) * 0.35;
                half moss = smoothstep(_MossSlope - 0.08, _MossSlope + 0.08, up) * _Moss;
                half3 mossColor = lerp(_MossDark.rgb, _MossLight.rgb, EnvFbm(p.xz * 3.0 + p.y * 2.0));
                mossColor *= 0.8 + 0.4 * EnvValueNoise(p.xz * 45.0 + p.y * 30.0);
                albedo = lerp(albedo, mossColor, moss);
                normalWS = normalize(lerp(normalWS, geometric, moss * 0.4));
                smoothness = lerp(smoothness, 0.04, moss);

                half dust = (1.0 - smoothstep(_DustBand.x, _DustBand.y, rise + (EnvValueNoise(trunk * 3.0) - 0.5) * 0.04)) * _DustBand.z;
                albedo = lerp(albedo, _Dust.rgb * saturate(0.7 + luma), dust * (1.0 - moss));

                albedo *= lerp(_TintLow.rgb, _TintHigh.rgb, instance);
                half occlusion = mask.r * lerp(1.0 - _Occlusion, 1.0, rise);

                InputData inputData = (InputData)0;
                inputData.positionWS = p;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(p);
                inputData.shadowCoord = TransformWorldToShadowCoord(p);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.smoothness = smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = occlusion;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = EnvApplyHaze(color.rgb, p);
                return half4(color.rgb, 1);
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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 ShadowVert(SurfaceAttributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection)));
            }

            half4 ShadowFrag() : SV_Target { return 0; }
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

            float4 DepthVert(SurfaceAttributes input) : SV_POSITION
            {
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
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

            struct NormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            NormalsVaryings NormalsVert(SurfaceAttributes input)
            {
                NormalsVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 NormalsFrag(NormalsVaryings input
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            ) : SV_Target0
            {
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
                return half4(NormalizeNormalPerPixel(input.normalWS), 0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
