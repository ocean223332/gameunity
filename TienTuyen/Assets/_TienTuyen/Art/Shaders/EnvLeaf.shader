// Textured flora (FloraScatter prefabs): bark, leaves and their far cross-cards. Alpha-clipped in every
// pass, so leaf shadows and depth match the cut-outs. Vertex colour: R wind weight, G wind phase,
// B leaf flutter, A occlusion. Each instance gets a tint shift, mostly shared by its grove. Instanced.
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
        // Set by FloraScatter when the camera renders with MSAA: soft cut-out edges by alpha to coverage.
        [ToggleUI] _AlphaToMask ("Alpha to coverage", Float) = 0
        // Rocks only (FloraPrefabBuilder): moss and grass over the upward faces, and the foot fading into the ground.
        [Toggle(_ROCK_TOPBLEND)] _RockTopBlend ("Rock: moss top and soft foot", Float) = 0
        _RockSink ("Rock: how deep the foot is sunk, per unit scale (m)", Float) = 0.18
        _RockMoss ("Rock: moss cover", Range(0, 1)) = 0.6
    }

    SubShader
    {
        // Wind moves the vertices of plants that never move as objects: always draw their motion vectors.
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" "AlwaysRenderMotionVectors" = "true" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
        #include "EnvCommon.hlsl"
        #include "EnvTerrain.hlsl"

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
            half _AlphaToMask;
            half _RockSink;
            half _RockMoss;
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

        // 0..1 per plant for its tint: mostly a field some 50 m across, so whole groves turn yellower or bluer
        // together, with a little of the plant's own hash on top.
        half LeafTint() { return lerp(LeafPlantHash(), EnvValueNoise(UNITY_MATRIX_M._m03_m23 / 50.0 + 7.1), 0.65); }

        // Where the vertex is at a given time: the plants stand still, only wind and flutter move them.
        float3 LeafPositionAt(LeafAttributes input, float time)
        {
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float3 root = UNITY_MATRIX_M._m03_m13_m23;
            float phase = frac(input.color.g + LeafPlantHash());
            float weight = input.color.r * _WindScale;
            float3 offset = EnvWindOffsetAt(positionWS, weight, phase, time);
            // The meadow's gust fronts (EnvGrassGust) lean the whole plant as they pass its root, so grass and trees
            // move in one wave.
            float2 downwind = normalize(_EnvWind.xy + float2(1e-5, 0));
            offset.xz += downwind * EnvGrassGust(root.xz, time) * 0.6 * _EnvWind.z * weight;
            // Bend, don't shear: a leaning crown also drops, keeping the plant's length.
            offset.y -= dot(offset.xz, offset.xz) * 0.5 / max(positionWS.y - root.y, 1.0);
            positionWS += offset;
            // Leaves shiver along their normal, out of step with the branch sway.
            float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            float t = time * 7.3 + phase * 40.0 + dot(positionWS, float3(1.7, 2.3, 1.1));
            positionWS += normalWS * (sin(t) * 0.6 + sin(t * 2.7) * 0.4) * _Flutter * input.color.b * input.color.r * _EnvWind.z * 8.0;
            return positionWS;
        }

        float3 LeafPositionWS(LeafAttributes input) { return LeafPositionAt(input, _Time.y); }

        // Set per draw by FloraScatter while plants change LOD, like unity_LODFade.x: above 0 the share of pixels kept
        // while fading in, below 0 fading out with the complementary dither, 0 for a plant drawn whole.
        float _FloraFade;

        void LeafFade(float4 positionCS)
        {
            if (_FloraFade == 0) return;
            half d = SAMPLE_TEXTURE2D(_DitheringTexture, sampler_PointRepeat, positionCS.xy * _DitheringTextureInvSize).a;
            clip(_FloraFade - CopySign(d, _FloraFade));
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
            AlphaToMask [_AlphaToMask]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ROCK_TOPBLEND
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        #if defined(_ROCK_TOPBLEND)
            // The meadow's layers, set globally by EnvironmentGround for the backdrop's near rings (EnvTerrain.hlsl).
            TEXTURE2D(_EnvFarGrass); SAMPLER(sampler_EnvFarGrass);
            TEXTURE2D(_EnvFarSoil);
            half4 _EnvFarGrassTint, _EnvFarSoilTint;

            // A rock that belongs to the ground: moss and grass settle on its upward faces, thickest in the hollows of
            // its texture and broken into patches, and its foot fades into the ground it is sunk in, so there is no
            // hard line where it meets the sward. Changes albedo, normal and smoothness in place.
            void RockTopBlend(float3 positionWS, float3 geometricNormal, inout half3 albedo, inout float3 normalWS, inout half smoothness)
            {
                float3 root = UNITY_MATRIX_M._m03_m13_m23;
                float scale = length(UNITY_MATRIX_M._m00_m10_m20);
                half patches = EnvFbm(positionWS.xz / 1.7 + root.xz * 0.13) - 0.5;
                half hollow = 0.5 - dot(albedo, half3(0.2126, 0.7152, 0.0722));
                half moss = smoothstep(0.55, 0.85, geometricNormal.y + patches * 0.5 + hollow * 0.4) * _RockMoss;
                half3 grass = SAMPLE_TEXTURE2D(_EnvFarGrass, sampler_EnvFarGrass, positionWS.xz * 0.6).rgb * _EnvFarGrassTint.rgb;
                half dry = smoothstep(0.45, 0.68, EnvFbm(positionWS.xz / 46.0 + 13.7));   // EnvMeadowFields' dry alone
                half3 green = grass * EnvMeadowTint(EnvMeadowDryness(dry, float3(0, 1, 0), root.y)) * lerp(0.62, 0.9, EnvValueNoise(positionWS.xz * 2.3));
                // The foot: within about 0.4 m of the ground at its root (more for big rocks) the rock takes the ground's
                // colour, darker and damp, the line wandering.
                half3 soil = SAMPLE_TEXTURE2D(_EnvFarSoil, sampler_EnvFarGrass, positionWS.xz * 0.5).rgb * _EnvFarSoilTint.rgb;
                float above = positionWS.y - (root.y + _RockSink * scale);
                half foot = 1.0 - smoothstep(0.0, 0.25 + 0.08 * scale, above + patches * 0.3);
                half3 ground = lerp(soil, green, 0.55) * 0.8;
                albedo = lerp(albedo, green, moss);
                albedo = lerp(albedo, ground, foot * 0.85);
                // Moss fills the rock's fine relief and is matt.
                normalWS = normalize(lerp(normalWS, geometricNormal, max(moss, foot) * 0.6));
                smoothness *= 1.0 - max(moss, foot) * 0.8;
            }
        #endif

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
                output.tint = LeafTint();
                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            ) : SV_Target0
            {
                UNITY_SETUP_INSTANCE_ID(input);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
                LeafFade(input.positionCS);
                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half coverage = 1;
            #if defined(_ALPHATEST_ON)
                if (_AlphaToMask > 0.5)
                    // Cut-out edge sharpened to about a pixel; MSAA turns it into partial coverage.
                    coverage = saturate((texel.a - _Cutoff) / max(fwidth(texel.a), 1e-4) + 0.5);
                else
                    clip(texel.a - _Cutoff);
            #endif
                // Leaves are drawn two-sided: their back faces are lit with the normal turned to face the viewer.
                float3 normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
            #if defined(_NORMALMAP)
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                // The bitangent comes from the unflipped normal, so on back faces the map's relief mirrors through the leaf.
                float3 bitangent = input.tangentWS.w * cross(normalize(input.normalWS), input.tangentWS.xyz);
                normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, normalWS)));
            #endif
                // Plants differ a little: some yellower, some darker.
                half shift = (input.tint - 0.5) * 2.0 * _TintVariation;
                half3 albedo = texel.rgb * _BaseColor.rgb * half3(1.0 + shift * 0.6, 1.0 + shift * 0.25, 1.0 - shift * 0.5) * (1.0 - abs(shift) * 0.4);
                albedo = lerp(dot(albedo, half3(0.2126, 0.7152, 0.0722)).xxx, albedo, _Saturation);
                half occlusion = lerp(1.0, input.color.a, _Occlusion);
                half smoothness = _Smoothness;
            #if defined(_ROCK_TOPBLEND)
                RockTopBlend(input.positionWS, normalize(input.normalWS), albedo, normalWS, smoothness);
            #endif

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
                surface.smoothness = smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = occlusion;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);

                // Light through the leaves: they glow when the view looks towards the light, the light bent by the
                // leaf's normal so the glow follows each leaf's shape; shadowed leaves keep a quarter of it, and the sky
                // shining through adds a little everywhere.
                Light mainLight = GetMainLight(inputData.shadowCoord);
                half3 through = normalize(mainLight.direction + normalWS * 0.5);
                half towardsSun = pow(saturate(dot(inputData.viewDirectionWS, -through)), 2.0);
                half lit = lerp(1.0, mainLight.shadowAttenuation, 0.75);
                color.rgb += albedo * (mainLight.color * lit * towardsSun * 1.4 + SampleSH(-normalWS) * 0.15) *
                    _Translucency * input.color.b * occlusion;

                return half4(EnvApplyHaze(color.rgb, input.positionWS), coverage);
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

            half4 ShadowFrag(ShadowVaryings input) : SV_Target { LeafFade(input.positionCS); LeafClip(input.uv); return 0; }
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

            half DepthFrag(DepthVaryings input) : SV_Target { LeafFade(input.positionCS); LeafClip(input.uv); return input.positionCS.z; }
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

            half4 NormalsFrag(NormalsVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            ) : SV_Target0
            {
                UNITY_SETUP_INSTANCE_ID(input);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
                LeafFade(input.positionCS);
                LeafClip(input.uv);
                return half4(NormalizeNormalPerPixel(input.normalWS * IS_FRONT_VFACE(face, 1.0, -1.0)), 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MotionVert
            #pragma fragment MotionFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"

            struct MotionVaryings
            {
                float4 positionCS : SV_POSITION;
                float4 currentCS : TEXCOORD0;
                float4 previousCS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // The plant itself is static, so last frame's pose is this frame's matrix with last frame's wind.
            MotionVaryings MotionVert(LeafAttributes input)
            {
                MotionVaryings output = (MotionVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 now = LeafPositionAt(input, _Time.y);
                float3 before = LeafPositionAt(input, _Time.y - unity_DeltaTime.x);
                output.positionCS = TransformWorldToHClip(now);
                output.currentCS = mul(_NonJitteredViewProjMatrix, float4(now, 1));
                output.previousCS = mul(_PrevViewProjMatrix, float4(before, 1));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            float4 MotionFrag(MotionVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                LeafFade(input.positionCS);
                LeafClip(input.uv);
                return float4(CalcNdcMotionVectorFromCsPositions(input.currentCS, input.previousCS), 0, 0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
