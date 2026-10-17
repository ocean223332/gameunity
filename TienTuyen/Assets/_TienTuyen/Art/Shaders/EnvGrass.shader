// Meadow grass and wildflowers, drawn by EnvironmentGrass with RenderMeshPrimitives: one clump mesh, one instance
// per cell of a world grid around the camera (rings of coarser cells further out). Where a clump stands, how tall,
// how dry: the grass field baked once from the ground (pass "Field"). No transforms or instance data on the CPU.
//
// Clump mesh (any mesh can replace the procedural one): object space, base at y = 0, about 1 m tall and 1 m
// across; the height above the base drives wind and shading, so any UV layout works; vertex colour RGB multiplies the albedo,
// A = 1 marks a flower head (coloured by _FlowerColors instead of the grass).
Shader "TienTuyen/Environment/Grass"
{
    Properties
    {
        _BaseMap ("Albedo (A: cut-out, white = none)", 2D) = "white" {}
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha clip", Float) = 0
        _Cutoff ("Cut-out threshold", Range(0, 1)) = 0.4
        _GrassColor ("Grass colour (times the ground's grass layer average)", Color) = (1, 1, 1, 1)
        _Height ("Height (m): lush, dry", Vector) = (0.55, 0.32, 0, 0)
        _WindScale ("Wind response", Range(0, 3)) = 1.2
        _Translucency ("Backlit translucency", Range(0, 3)) = 0.9
        _Gust ("Gust waves: extra lean, blade sheen", Vector) = (0.8, 0.55, 0, 0)
        _FlowerColorA ("Flower colour A", Color) = (0.92, 0.9, 0.82, 1)
        _FlowerColorB ("Flower colour B", Color) = (0.95, 0.72, 0.12, 1)
        _FlowerColorC ("Flower colour C", Color) = (0.62, 0.32, 0.72, 1)
    }

    SubShader
    {
        // The clumps never move as objects; wind moves their vertices, so always draw motion vectors.
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" "AlwaysRenderMotionVectors" = "true" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "EnvCommon.hlsl"
        #include "EnvTerrain.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half _Cutoff;
            half4 _GrassColor;
            float4 _Height;
            half _WindScale;
            half _Translucency;
            float4 _Gust;
            half4 _FlowerColorA, _FlowerColorB, _FlowerColorC;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        // The ground's grass layer and tint, published by EnvironmentGround: blades match the sward they grow from.
        TEXTURE2D(_EnvFarGrass); SAMPLER(sampler_EnvFarGrass);
        half4 _EnvFarGrassTint;
        // RGBA: ground height (m), cover 0..1, dryness 0..1, unused. Bilinear, clamped.
        TEXTURE2D(_GrassField); SAMPLER(sampler_GrassField);
        float4 _GrassFieldRect;     // min x, min z, 1/size x, 1/size z
        // Per ring (property block): cell size (m), cells per chunk side, inner and outer half-extent (m): the ring
        // draws the square band between them around the camera.
        float4 _GrassRing;
        float _GrassScale;          // per ring: clump scale
        // Per ring: thinning distances (m) start, end, the cover kept at the end, and 1 for the outermost ring
        // (fades out into the ground instead of handing over to a coarser ring).
        float4 _GrassFade;
        // Per ring: camera position the ring was laid out around (xz), 0 grass / 1 flowers, flower share.
        float4 _GrassCenter;
        // Origin (xz) of each visible chunk; its cells are consecutive instances. A uniform array, not a buffer: a
        // pass drawn without the property block reads zeros instead of an unbound buffer.
        float4 _GrassChunks[64];

        struct GrassAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
        };

        struct Clump
        {
            bool alive;
            float3 root;        // world position of the clump's base
            float yaw, scale, height, phase, dryness, hue, fade;
        };

        half4 SampleField(float2 xz) { return SAMPLE_TEXTURE2D_LOD(_GrassField, sampler_GrassField, (xz - _GrassFieldRect.xy) * _GrassFieldRect.zw, 0); }

        // The clump of one grid cell: everything follows from the cell's world index, so a clump stays put while the
        // grid slides with the camera.
        Clump GrassClump(uint instance)
        {
            Clump c = (Clump)0;
            uint side = (uint)_GrassRing.y;
            if (side == 0) return c;
            uint chunk = instance / (side * side), local = instance % (side * side);
            float cell = _GrassRing.x;
            float2 origin = _GrassChunks[min(chunk, 63u)].xy;
            float2 index = round(origin / cell) + float2(local % side, local / side);
            float h0 = EnvHash21(index * 0.731 + 17.3), h1 = EnvHash21(index * 1.37 - 4.1), h2 = EnvHash21(index * 0.53 + 91.7);
            float2 xz = (index + 0.5 + (float2(h0, h1) - 0.5) * 0.9) * cell;
            float2 fromCamera = xz - _GrassCenter.xy;
            // The inner square belongs to the finer ring.
            float square = max(abs(fromCamera.x), abs(fromCamera.y));
            if (square < _GrassRing.z || square >= _GrassRing.w) return c;
            float range = length(fromCamera);
            half4 field = SampleField(xz);
            // Thin out towards the ring's edge, down to the coarser ring's density.
            half thin = smoothstep(_GrassFade.x, _GrassFade.y, range);
            half keep = field.g * lerp(1.0, _GrassFade.z, thin);
            // Flowers: a small share of the lush cells, a different pick from the grass.
            if (_GrassCenter.z > 0.5) keep = field.g * _GrassCenter.w * (1.0 - field.b) * lerp(1.0, _GrassFade.z, thin);
            if (h2 >= keep) return c;
            c.fade = _GrassFade.w > 0.5 ? thin : 0.0;
            c.alive = c.fade < 0.999;
            c.root = float3(xz.x, field.r, xz.y);
            c.yaw = EnvHash21(index * 2.11 + 5.3) * 6.2832;
            c.hue = EnvHash21(index * 0.37 - 13.9);
            c.phase = EnvHash21(index * 3.07 + 1.1);
            c.dryness = field.b;
            // Taller where lush; clumps at the edge of their cover (by the arena, roads, worn ground) stay low.
            c.height = lerp(_Height.x, _Height.y, field.b) * lerp(0.7, 1.3, c.hue) * saturate(field.g * 1.6 + 0.2) * (1.0 - c.fade);
            c.scale = _GrassScale * lerp(0.85, 1.15, h0);
            return c;
        }

        // A clump vertex at a given time: the clump stands still, the wind bends its blades by their height and a gust
        // passing over lays them further down.
        float3 GrassVertexAt(GrassAttributes input, Clump c, float time)
        {
            float s, k;
            sincos(c.yaw, s, k);
            float3 p = input.positionOS.xyz * float3(c.scale, c.height, c.scale);
            p.xz = float2(p.x * k - p.z * s, p.x * s + p.z * k);
            float3 positionWS = c.root + p;
            float bend = saturate(input.positionOS.y) * saturate(input.positionOS.y) * c.height;
            float3 wind = EnvWindOffsetAt(c.root, bend * _WindScale, c.phase, time);
            wind.xz += normalize(_EnvWind.xy + float2(1e-5, 0)) * EnvGrassGust(c.root.xz, time) * bend * _Gust.x;
            // Bending keeps the blade's length: the tip sinks as it swings over.
            wind.y -= dot(wind.xz, wind.xz) / max(2.0 * c.height, 0.05);
            return positionWS + wind;
        }

        // Off-screen for the rasteriser: a clump that is not drawn collapses outside the clip volume.
        float4 Culled() { return float4(2, 2, 0.5, 1); }

        void GrassClip(float2 uv)
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
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half3 albedo : TEXCOORD3;
                half tip : TEXCOORD4;
                half gust : TEXCOORD5;
            };

            Varyings Vert(GrassAttributes input, uint instance : SV_InstanceID)
            {
                Varyings output = (Varyings)0;
                Clump c = GrassClump(instance);
                if (!c.alive) { output.positionCS = Culled(); return output; }
                output.positionWS = GrassVertexAt(input, c, _Time.y);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                // Blades are lit mostly like the sward they stand in, so a clump never shows a dark back face.
                float s, k;
                sincos(c.yaw, s, k);
                float3 normalWS = float3(input.normalOS.x * k - input.normalOS.z * s, input.normalOS.y, input.normalOS.x * s + input.normalOS.z * k);
                output.normalWS = normalize(lerp(normalWS, float3(0, 1, 0), 0.65));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                // Colour: the ground's grass, lush or dry like the sward under it, each clump a little different;
                // flower heads take one of three colours.
                half3 grass = SAMPLE_TEXTURE2D_LOD(_EnvFarGrass, sampler_EnvFarGrass, 0.5, 12).rgb * _EnvFarGrassTint.rgb * _GrassColor.rgb
                    * EnvMeadowTint(c.dryness);
                half shift = (c.hue - 0.5) * 0.36;
                grass *= half3(1.0 + shift * 0.6, 1.0 + shift * 0.2, 1.0 - shift * 0.5) * (1.0 - abs(shift) * 0.5);
                half3 flower = c.hue < 0.45 ? _FlowerColorA.rgb : (c.hue < 0.75 ? _FlowerColorB.rgb : _FlowerColorC.rgb);
                output.albedo = input.color.a > 0.5 ? flower : grass * input.color.rgb;
                output.tip = saturate(input.positionOS.y);
                output.gust = EnvGrassGust(c.root.xz, _Time.y);
                return output;
            }

            half4 Frag(Varyings input
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            ) : SV_Target0
            {
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
            #if defined(_ALPHATEST_ON)
                clip(texel.a - _Cutoff);
            #endif
                // Darker down in the clump where the blades shade each other, paler at the sunlit tips.
                half3 albedo = input.albedo * texel.rgb * lerp(0.42, 1.12, input.tip);
                // Blades laid over by a gust turn their paler, glossier sides up: the wave reads from afar.
                albedo *= 1.0 + input.gust * (0.35 + 0.65 * input.tip) * _Gust.y;
                half occlusion = lerp(0.45, 1.0, input.tip);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.smoothness = 0.18 + input.gust * input.tip * 0.15;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = occlusion;
                surface.alpha = 1;
                half4 color = UniversalFragmentPBR(inputData, surface);

                // Low sun through the blades.
                Light mainLight = GetMainLight(inputData.shadowCoord);
                half towardsSun = pow(saturate(dot(-inputData.viewDirectionWS, mainLight.direction)), 4.0);
                color.rgb += albedo * mainLight.color * mainLight.shadowAttenuation * (towardsSun * 1.2 + 0.08) * _Translucency * input.tip;
                return half4(EnvApplyHaze(color.rgb, input.positionWS), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma shader_feature_local _ALPHATEST_ON

            struct DepthVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            DepthVaryings DepthVert(GrassAttributes input, uint instance : SV_InstanceID)
            {
                DepthVaryings output = (DepthVaryings)0;
                Clump c = GrassClump(instance);
                output.positionCS = c.alive ? TransformWorldToHClip(GrassVertexAt(input, c, _Time.y)) : Culled();
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target { GrassClip(input.uv); return input.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS
            #pragma shader_feature_local _ALPHATEST_ON

            struct NormalsVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            NormalsVaryings NormalsVert(GrassAttributes input, uint instance : SV_InstanceID)
            {
                NormalsVaryings output = (NormalsVaryings)0;
                Clump c = GrassClump(instance);
                output.positionCS = c.alive ? TransformWorldToHClip(GrassVertexAt(input, c, _Time.y)) : Culled();
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
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
                GrassClip(input.uv);
                // The sward's normal, as lit: ambient occlusion reads the grass as ground cover, not as walls.
                return half4(0, 1, 0, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex MotionVert
            #pragma fragment MotionFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"

            struct MotionVaryings
            {
                float4 positionCS : SV_POSITION;
                float4 currentCS : TEXCOORD0;
                float4 previousCS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            // The clump is static, so last frame's pose is the same clump in last frame's wind.
            MotionVaryings MotionVert(GrassAttributes input, uint instance : SV_InstanceID)
            {
                MotionVaryings output = (MotionVaryings)0;
                Clump c = GrassClump(instance);
                if (!c.alive) { output.positionCS = Culled(); return output; }
                float3 now = GrassVertexAt(input, c, _Time.y);
                float3 before = GrassVertexAt(input, c, _Time.y - unity_DeltaTime.x);
                output.positionCS = TransformWorldToHClip(now);
                output.currentCS = mul(_NonJitteredViewProjMatrix, float4(now, 1));
                output.previousCS = mul(_PrevViewProjMatrix, float4(before, 1));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            float4 MotionFrag(MotionVaryings input) : SV_Target
            {
                GrassClip(input.uv);
                return float4(CalcNdcMotionVectorFromCsPositions(input.currentCS, input.previousCS), 0, 0);
            }
            ENDHLSL
        }

        // The grass field, baked once into a render texture over the whole terrain (a full-screen triangle):
        // R ground height, G grass cover, B meadow dryness. Cover follows the ground's own paint: grass where the
        // ground shows grass, none on soil, ash, mud, needles under the woods, rock and scree; the CPU mask (G of
        // _GrassHeight) clears the arena, the station sites, roads and craters.
        Pass
        {
            Name "Field"
            // A light mode no camera renders: without one, URP would draw this pass with every clump (as SRPDefaultUnlit).
            Tags { "LightMode" = "GrassFieldBake" }
            ZTest Always ZWrite Off Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex FieldVert
            #pragma fragment FieldFrag

            TEXTURE2D(_GrassHeight); SAMPLER(sampler_GrassHeight);   // R height (m), G mask, at 1 m
            float4 _GrassHeightRect;   // min x, min z, 1/size x, 1/size z (texel centres on whole metres)
            TEXTURE2D(_SplatMap); SAMPLER(sampler_SplatMap);
            float4 _SplatRect;
            float4 _RockSlope;
            float4 _Playfield;

            struct FieldVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            FieldVaryings FieldVert(uint vertexID : SV_VertexID)
            {
                FieldVaryings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                output.uv = GetFullScreenTriangleTexCoord(vertexID);
                return output;
            }

            float2 HeightAt(float2 xz) { return SAMPLE_TEXTURE2D_LOD(_GrassHeight, sampler_GrassHeight, (xz - _GrassHeightRect.xy) * _GrassHeightRect.zw, 0).rg; }

            half4 FieldFrag(FieldVaryings input) : SV_Target
            {
                float2 xz = _GrassFieldRect.xy + input.uv / _GrassFieldRect.zw;
                float2 here = HeightAt(xz);
                float hx = HeightAt(xz + float2(1, 0)).r - HeightAt(xz - float2(1, 0)).r;
                float hz = HeightAt(xz + float2(0, 1)).r - HeightAt(xz - float2(0, 1)).r;
                float3 normal = normalize(float3(-hx * 0.5, 1.0, -hz * 0.5));

                float2 splatUV = (xz - _SplatRect.xy) * _SplatRect.zw;
                float2 splatEdge = min(splatUV, 1.0 - splatUV);
                half inSplat = smoothstep(0.0, 0.06, min(splatEdge.x, splatEdge.y));
                half4 splat = SAMPLE_TEXTURE2D_LOD(_SplatMap, sampler_SplatMap, splatUV, 0) * inSplat;
                float outside = length(max(abs(xz) - _Playfield.xy, 0.0));
                half meadow = max(smoothstep(2.0, 10.0, outside), 1.0 - inSplat);

                half dry, woods, bare;
                EnvMeadowFields(xz, dry, woods, bare);
                half worn = smoothstep(0.72, 0.84, EnvFbm(xz / 3.3 + 8.2)) * (1.0 - woods) * 0.7;
                half rock = EnvRockCover(xz, normal.y, _RockSlope.xy);
                half scree = saturate(EnvRockCover(xz, normal.y + 0.12, _RockSlope.xy) - rock);
                half painted = (1.0 - splat.r) * (1.0 - splat.b) * (1.0 - saturate(splat.g * 2.0));
                half station = splat.a * painted;
                half open = (1.0 - woods) * (1.0 - bare * 0.8) * (1.0 - worn) * (1.0 - scree) * painted;
                half cover = lerp(station, open, meadow) * (1.0 - rock) * here.g;
                return half4(here.r, saturate(cover), EnvMeadowDryness(dry, normal, here.r), 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
