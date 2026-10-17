// Upland ground: five scanned PBR layers (soil, mud, pine needles, grass, rock), each given as a Unity TerrainLayer
// (EnvironmentGround feeds diffuse, normal, mask R metal/G AO/B height/A smoothness, tile size, tint, normal scale),
// and two more mixed in patches: Grass2 (a flowering sward) into the grass, Moss into the needles.
// Around the station a baked world-space splat paints them (R soil, G wetness/standing water, B ash, A grass; needles
// are the rest) with height-aware transitions, a soil tone map (R dry dust, G damp, B fresh red) and a relief map
// (slope, height, compaction); a road distance field cuts wheel ruts and water stands at the bottom of hollows with
// analytic sky reflections. Out on the hills the meadow is procedural: dry and lush grass, pine-wood floor, worn soil,
// and rock on the steeps sampled triplanar so slopes never stretch. Far away the layers give way to their average
// colours under the shared meadow fields (EnvTerrain.hlsl), which the backdrop's near rings continue.
// Pebbles, twigs and leaves are scattered procedurally near the camera.
Shader "TienTuyen/Environment/Ground"
{
    Properties
    {
        _SoilDiffuse ("Soil", 2D) = "white" {}
        _SoilNormal ("Soil normal", 2D) = "bump" {}
        _SoilMask ("Soil mask (R metal, G AO, B height, A smoothness)", 2D) = "grey" {}
        _MudDiffuse ("Mud", 2D) = "white" {}
        _MudNormal ("Mud normal", 2D) = "bump" {}
        _MudMask ("Mud mask", 2D) = "grey" {}
        _NeedlesDiffuse ("Pine needles", 2D) = "white" {}
        _NeedlesNormal ("Pine needles normal", 2D) = "bump" {}
        _NeedlesMask ("Pine needles mask", 2D) = "grey" {}
        _GrassDiffuse ("Grass", 2D) = "white" {}
        _GrassNormal ("Grass normal", 2D) = "bump" {}
        _GrassMask ("Grass mask", 2D) = "grey" {}
        _Grass2Diffuse ("Grass patches (lighter, flowering)", 2D) = "white" {}
        _Grass2Normal ("Grass patches normal", 2D) = "bump" {}
        _Grass2Mask ("Grass patches mask", 2D) = "grey" {}
        _MossDiffuse ("Moss patches in the needles", 2D) = "white" {}
        _MossNormal ("Moss patches normal", 2D) = "bump" {}
        _MossMask ("Moss patches mask", 2D) = "grey" {}
        _RockDiffuse ("Rock", 2D) = "white" {}
        _RockNormal ("Rock normal", 2D) = "bump" {}
        _RockMask ("Rock mask", 2D) = "grey" {}
        _Tiling ("Tiling per metre (soil, mud, needles, grass)", Vector) = (0.3, 0.33, 0.38, 0.31)
        _RockTiling ("Rock: tiling per metre, normal scale", Vector) = (0.25, 1, 0, 0)
        _Grass2Tiling ("Grass patches: tiling per metre, normal scale", Vector) = (0.83, 1, 0, 0)
        _MossTiling ("Moss patches: tiling per metre, normal scale", Vector) = (0.4, 1, 0, 0)
        _NormalScale ("Normal scale (soil, mud, needles, grass)", Vector) = (1, 1, 1, 1)
        _SoilTint ("Soil tint", Color) = (1, 1, 1, 1)
        _MudTint ("Mud tint", Color) = (1, 1, 1, 1)
        _NeedlesTint ("Needles tint", Color) = (1, 1, 1, 1)
        _GrassTint ("Grass tint", Color) = (1, 1, 1, 1)
        _RockTint ("Rock tint", Color) = (1, 1, 1, 1)
        _Grass2Tint ("Grass patches tint", Color) = (1, 1, 1, 1)
        _GustSheen ("Gust waves: sward sheen", Range(0, 1)) = 0.25
        _MossTint ("Moss patches tint", Color) = (1, 1, 1, 1)
        _MaskMin0 ("Mask remap min (soil)", Vector) = (0, 0, 0, 0)
        _MaskMax0 ("Mask remap max (soil)", Vector) = (1, 1, 1, 1)
        _MaskMin1 ("Mask remap min (mud)", Vector) = (0, 0, 0, 0)
        _MaskMax1 ("Mask remap max (mud)", Vector) = (1, 1, 1, 1)
        _MaskMin2 ("Mask remap min (needles)", Vector) = (0, 0, 0, 0)
        _MaskMax2 ("Mask remap max (needles)", Vector) = (1, 1, 1, 1)
        _MaskMin3 ("Mask remap min (grass)", Vector) = (0, 0, 0, 0)
        _MaskMax3 ("Mask remap max (grass)", Vector) = (1, 1, 1, 1)
        _MaskMin4 ("Mask remap min (rock)", Vector) = (0, 0, 0, 0)
        _MaskMax4 ("Mask remap max (rock)", Vector) = (1, 1, 1, 1)
        _MaskMin5 ("Mask remap min (grass patches)", Vector) = (0, 0, 0, 0)
        _MaskMax5 ("Mask remap max (grass patches)", Vector) = (1, 1, 1, 1)
        _MaskMin6 ("Mask remap min (moss patches)", Vector) = (0, 0, 0, 0)
        _MaskMax6 ("Mask remap max (moss patches)", Vector) = (1, 1, 1, 1)
        _SplatMap ("Splat", 2D) = "black" {}
        _ToneMap ("Soil tone (R dust, G damp, B red, A broad value)", 2D) = "black" {}
        _ReliefMap ("Relief (RG slope, B height, A compaction)", 2D) = "grey" {}
        _RoadField ("Road field (R signed distance, G rut strength, BA gradient)", 2D) = "black" {}
        _RoadFar ("Road field over the whole terrain (as _RoadField)", 2D) = "black" {}
        _RoadFarRect ("Far road rect (min x, min z, 1/size x, 1/size z)", Vector) = (-208, -208, 0.0024, 0.0024)
        _RiverField ("River field (R signed distance to the river, north positive)", 2D) = "black" {}
        _RiverRect ("River field rect (min x, min z, 1/size x, 1/size z)", Vector) = (-348, -348, 0.0014, 0.0014)
        _River ("River: water level, half width, north plain, south plain (m)", Vector) = (-29.7, 10, 60, 30)
        _Paddy ("Paddies: cell length, cell width, dike width (m), share flooded", Vector) = (16, 10, 1.0, 0.18)
        _Karst ("Karst wall: west x, east x, main fall x, twin fall x", Vector) = (-300, -30, -108, -190)
        _KarstRock ("Karst wall rock (rgb albedo, a height; Mountain Lake's tiled cliff, as on the far towers)", 2D) = "grey" {}
        _KarstTex ("Karst wall rock: tile (m), contrast, moss in the low ground, wet in the low ground", Vector) = (14, 1.3, 1.2, 0.5)
        _Terrace ("Terraces: step height (m), riser share, share flooded, steepest slope (1 - normal.y)", Vector) = (1.6, 0.22, 0.35, 0.3)
        _SplatRect ("Splat rect (min x, min z, 1/size x, 1/size z)", Vector) = (-64, -64, 0.0078125, 0.0078125)
        _Playfield ("Playfield (half x, half z, flat margin)", Vector) = (16.55, 9.55, 3.5, 0)
        _Relief ("Relief: normal strength, height range (m), rut depth (m), debris reach (m)", Vector) = (1, 0.12, 0.035, 26)
        _SoilSaturation ("Soil saturation", Range(0, 1.5)) = 0.75
        _SkinAO ("Karst skin: 1 reads the occlusion baked into uv0.x (EnvironmentRiverlands' face mesh); the terrain leaves it 0", Float) = 0
        _DustColor ("Dry compacted dust", Color) = (0.6, 0.49, 0.38, 1)
        _DampColor ("Damp soil multiplier", Color) = (0.68, 0.62, 0.58, 1)
        _RedTint ("Fresh laterite multiplier", Color) = (1.08, 0.96, 0.86, 1)
        _ValueRange ("Broad value (dark, light)", Vector) = (0.84, 1.16, 0, 0)
        _MudColor ("Wet darkening", Color) = (0.5, 0.42, 0.36, 1)
        _AshColor ("Ash", Color) = (0.17, 0.15, 0.13, 1)
        _WaterColor ("Water", Color) = (0.17, 0.13, 0.09, 1)
        _Far ("Far: detail fades from x to y (m); hill relief strength z", Vector) = (45, 150, 0.9, 0)
        _RockSlope ("Rock from slope (1 - normal.y): start, full", Vector) = (0.22, 0.44, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Tiling;
            float4 _RockTiling;
            float4 _Grass2Tiling, _MossTiling;
            float4 _NormalScale;
            half4 _SoilTint;
            half4 _MudTint;
            half4 _NeedlesTint;
            half4 _GrassTint;
            half4 _RockTint;
            half4 _Grass2Tint, _MossTint;
            half _GustSheen;
            float4 _MaskMin5, _MaskMax5, _MaskMin6, _MaskMax6;
            float4 _MaskMin0, _MaskMax0, _MaskMin1, _MaskMax1, _MaskMin2, _MaskMax2, _MaskMin3, _MaskMax3, _MaskMin4, _MaskMax4;
            float4 _SplatRect;
            float4 _RoadFarRect;
            float4 _RiverRect, _River, _Paddy, _Terrace, _Karst, _KarstTex;
            float4 _Playfield;
            float4 _Relief;
            half _SoilSaturation;
            half _SkinAO;
            half4 _DustColor;
            half4 _DampColor;
            half4 _RedTint;
            float4 _ValueRange;
            half4 _MudColor;
            half4 _AshColor;
            half4 _WaterColor;
            float4 _Far;
            float4 _RockSlope;
        CBUFFER_END

        struct GroundAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "EnvCommon.hlsl"
            #include "EnvTerrain.hlsl"

            // One sampler (repeat, trilinear, anisotropic) for every layer map; one clamped sampler for the baked maps.
            TEXTURE2D(_SoilDiffuse); SAMPLER(sampler_SoilDiffuse);
            TEXTURE2D(_SoilNormal); TEXTURE2D(_SoilMask);
            TEXTURE2D(_MudDiffuse); TEXTURE2D(_MudNormal); TEXTURE2D(_MudMask);
            TEXTURE2D(_NeedlesDiffuse); TEXTURE2D(_NeedlesNormal); TEXTURE2D(_NeedlesMask);
            TEXTURE2D(_GrassDiffuse); TEXTURE2D(_GrassNormal); TEXTURE2D(_GrassMask);
            TEXTURE2D(_Grass2Diffuse); TEXTURE2D(_Grass2Normal); TEXTURE2D(_Grass2Mask);
            TEXTURE2D(_MossDiffuse); TEXTURE2D(_MossNormal); TEXTURE2D(_MossMask);
            TEXTURE2D(_RockDiffuse); TEXTURE2D(_RockNormal); TEXTURE2D(_RockMask);
            TEXTURE2D(_SplatMap); SAMPLER(sampler_SplatMap);
            TEXTURE2D(_ToneMap);
            TEXTURE2D(_ReliefMap);
            TEXTURE2D(_RoadField);
            TEXTURE2D(_RoadFar);
            TEXTURE2D(_RiverField);
            TEXTURE2D(_KarstRock);
            #define LAYER_SAMPLER sampler_SoilDiffuse

            struct Layer
            {
                half3 albedo;
                half height;
                half3 normalTS;   // in the ground's top-down frame
                half occlusion;
                half smoothness;
            };

            // A second sample, turned 37 degrees and coarser, breaks the tile repeat. Turned by an odd angle so the two
            // lattices never line up into stripes; it takes over with distance and in patches.
            static const float2x2 AntiTileTurn = float2x2(0.8, -0.6, 0.6, 0.8);
            float2 AntiTileUV(float2 uv) { return mul(AntiTileTurn, uv) * 0.43 + 0.37; }

            half4 RemapMask(half4 mask, float4 low, float4 high) { return low + mask * (high - low); }

            // A layer projected straight down. Gradients are passed in, so the call may sit inside a branch.
            Layer SampleTop(TEXTURE2D_PARAM(diffuse, unusedSampler), TEXTURE2D(normalMap), TEXTURE2D(maskMap), float2 uv, float2 dx, float2 dy,
                half anti, half normalScale, half3 tint, float4 maskLow, float4 maskHigh)
            {
                Layer layer;
                float2 uvB = AntiTileUV(uv), dxB = mul(AntiTileTurn, dx) * 0.43, dyB = mul(AntiTileTurn, dy) * 0.43;
                half3 a = SAMPLE_TEXTURE2D_GRAD(diffuse, LAYER_SAMPLER, uv, dx, dy).rgb;
                half3 b = SAMPLE_TEXTURE2D_GRAD(diffuse, LAYER_SAMPLER, uvB, dxB, dyB).rgb;
                // Patches a few tiles across where the turned sample shows through, so no straight repeat survives.
                half mix = saturate(anti * 0.6 + (EnvValueNoise(uv * 0.21 + 3.7) - 0.5) * 1.2);
                layer.albedo = lerp(a, b, mix);
                layer.albedo *= tint;
                half3 na = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(normalMap, LAYER_SAMPLER, uv, dx, dy), normalScale);
                half3 nb = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(normalMap, LAYER_SAMPLER, uvB, dxB, dyB), normalScale);
                // The turned sample's slopes turned back into the ground's frame.
                nb.xy = mul(nb.xy, AntiTileTurn);
                layer.normalTS = normalize(lerp(na, nb, mix));
                half4 mask = RemapMask(SAMPLE_TEXTURE2D_GRAD(maskMap, LAYER_SAMPLER, uv, dx, dy), maskLow, maskHigh);
                layer.occlusion = mask.g;
                layer.height = mask.b;
                layer.smoothness = mask.a;
                return layer;
            }

            // Patches of a second surface over a layer (a flowering sward in the grass, moss in the needles): the
            // second set turned the other way from the anti-tile sample so no two lattices line up, and at the patch
            // edge the taller of the two surfaces wins, so they interlock instead of fading.
            void MixPatches(inout Layer layer, TEXTURE2D_PARAM(diffuse, unusedSampler), TEXTURE2D(normalMap), TEXTURE2D(maskMap), float2 xz,
                float2 dx, float2 dy, float4 tiling, half3 tint, float4 maskLow, float4 maskHigh, half anti, half patch)
            {
                float2 uv = mul(xz, AntiTileTurn) * tiling.x;
                float2 du = mul(dx, AntiTileTurn) * tiling.x, dv = mul(dy, AntiTileTurn) * tiling.x;
                Layer patches = SampleTop(TEXTURE2D_ARGS(diffuse, unusedSampler), normalMap, maskMap, uv, du, dv, anti, tiling.y, tint, maskLow, maskHigh);
                patches.normalTS.xy = mul(AntiTileTurn, patches.normalTS.xy);
                half take = saturate(patch + (patches.height - layer.height) * 2.4 * patch * (1.0 - patch));
                layer.albedo = lerp(layer.albedo, patches.albedo, take);
                layer.normalTS = normalize(lerp(layer.normalTS, patches.normalTS, take));
                layer.height = lerp(layer.height, patches.height, take);
                layer.occlusion = lerp(layer.occlusion, patches.occlusion, take);
                layer.smoothness = lerp(layer.smoothness, patches.smoothness, take);
            }

            // Triplanar for steep ground: three projections blended by the surface normal, normals whiteout-blended
            // in world space and returned in the ground's frame (tangent, bitangent, normal).
            Layer SampleTriplanar(TEXTURE2D_PARAM(diffuse, unusedSampler), TEXTURE2D(normalMap), TEXTURE2D(maskMap), float3 p, float3 dpx, float3 dpy,
                float3 n, float tiling, half normalScale, half3 tint, float4 maskLow, float4 maskHigh, float3 tangent, float3 bitangent)
            {
                float3 w = pow(abs(n), 4.0);
                w /= dot(w, float3(1, 1, 1));
                float3 q = p * tiling;
                float3 dqx = dpx * tiling, dqy = dpy * tiling;
                half3 albedo = 0;
                half4 mask = 0;
                float3 nx = 0, ny = 0, nz = 0;
                UNITY_BRANCH if (w.x > 0.02)
                {
                    albedo += SAMPLE_TEXTURE2D_GRAD(diffuse, LAYER_SAMPLER, q.zy, dqx.zy, dqy.zy).rgb * w.x;
                    mask += SAMPLE_TEXTURE2D_GRAD(maskMap, LAYER_SAMPLER, q.zy, dqx.zy, dqy.zy) * w.x;
                    half3 t = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(normalMap, LAYER_SAMPLER, q.zy, dqx.zy, dqy.zy), normalScale);
                    t = half3(t.xy + n.zy, abs(t.z) * n.x);
                    nx = t.zyx * w.x;
                }
                UNITY_BRANCH if (w.y > 0.02)
                {
                    albedo += SAMPLE_TEXTURE2D_GRAD(diffuse, LAYER_SAMPLER, q.xz, dqx.xz, dqy.xz).rgb * w.y;
                    mask += SAMPLE_TEXTURE2D_GRAD(maskMap, LAYER_SAMPLER, q.xz, dqx.xz, dqy.xz) * w.y;
                    half3 t = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(normalMap, LAYER_SAMPLER, q.xz, dqx.xz, dqy.xz), normalScale);
                    t = half3(t.xy + n.xz, abs(t.z) * n.y);
                    ny = t.xzy * w.y;
                }
                UNITY_BRANCH if (w.z > 0.02)
                {
                    albedo += SAMPLE_TEXTURE2D_GRAD(diffuse, LAYER_SAMPLER, q.xy, dqx.xy, dqy.xy).rgb * w.z;
                    mask += SAMPLE_TEXTURE2D_GRAD(maskMap, LAYER_SAMPLER, q.xy, dqx.xy, dqy.xy) * w.z;
                    half3 t = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(normalMap, LAYER_SAMPLER, q.xy, dqx.xy, dqy.xy), normalScale);
                    t = half3(t.xy + n.xy, abs(t.z) * n.z);
                    nz = t.xyz * w.z;
                }
                float3 worldNormal = normalize(nx + ny + nz + n * 1e-3);
                mask = RemapMask(mask, maskLow, maskHigh);
                Layer layer;
                layer.albedo = albedo * tint;
                layer.normalTS = half3(dot(worldNormal, tangent), dot(worldNormal, bitangent), dot(worldNormal, n));
                layer.occlusion = mask.g;
                layer.height = mask.b;
                layer.smoothness = mask.a;
                return layer;
            }

            Layer EmptyLayer()
            {
                Layer layer;
                layer.albedo = 0.5;
                layer.height = 0.5;
                layer.normalTS = half3(0, 0, 1);
                layer.occlusion = 1;
                layer.smoothness = 0.1;
                return layer;
            }

            // Mirrors EnvironmentLayout.Bombed: the open, shelled south valley (its west flank has grown back).
            half Bombed(float2 xz)
            {
                half valley = smoothstep(-11.5, -17.0, xz.y) * (1.0 - smoothstep(42.0, 88.0, abs(xz.x))) * (1.0 - smoothstep(-110.0, -150.0, xz.y));
                return valley * (1.0 - smoothstep(-26.0, -46.0, xz.x + (xz.y + 20.0) * 0.35));
            }

            // The river valley, from the signed distance s to the river (north positive) and the world height y:
            // mud and sand on the banks, a quilt of paddies on the north floodplain, flights of terraces up the
            // south bank. Broad colour fields, read from 150 m and more. Returns the albedo blend weight; mirror
            // is how much of the sky the flooded fields reflect.
            half RiverValleyPaint(float2 xz, float s, float y, float3 normal, inout half3 albedo, inout half smoothness, out half mirror)
            {
                mirror = 0.0;
                float bank = abs(s) - _River.y;
                half onFlat = smoothstep(0.93, 0.985, normal.y);
                half wobble = EnvValueNoise(xz * 0.08) - 0.5;
                half paint = 0.0;
                half3 colour = 0.0;
                half gloss = 0.1;
                // Bank: dark, glistening wet mud up to a wobbling tide line about a metre over the water, then pale
                // sand bars drying out a few metres up.
                half mud = 1.0 - smoothstep(2.5, 6.0, bank + wobble * 3.0);
                half3 sand = lerp(half3(0.2, 0.15, 0.1), half3(0.42, 0.36, 0.26), smoothstep(0.5, 4.0, bank + wobble * 2.0));
                // The soaked band follows the distance from the water (smooth, from the river field), not the height:
                // heights run linear across each 2 m facet, so a band keyed on them stepped in blocks along the bank. A soft
                // cap on the height keeps it off steep faces.
                half soaked = (1.0 - smoothstep(0.8, 2.6, bank + wobble * 1.6 + (EnvValueNoise(xz * 0.6) - 0.5) * 0.5))
                    * (1.0 - smoothstep(1.2, 3.0, y - _River.x));
                paint = max(mud, soaked);
                colour = lerp(sand, half3(0.07, 0.065, 0.045), soaked);
                gloss = max(lerp(0.2, 0.45, 1.0 - smoothstep(0.0, 2.0, bank)), soaked * 0.4);   // satin: a mirror-wet band lights up each 2 m facet

                // Paddies on the flat floodplain north of the river: a grid of dyked fields turned off the axes,
                // each field flooded, young green, or ripening, by its own hash.
                float plainEnd = _River.z + 6.0 * wobble;
                half onPlain = smoothstep(3.0, 6.0, bank) * (1.0 - smoothstep(plainEnd - 6.0, plainEnd, bank)) * step(0.0, s) * onFlat;
                UNITY_BRANCH if (onPlain > 0.001)
                {
                    float2 q = mul(float2x2(0.96, -0.28, 0.28, 0.96), xz) + float2(EnvValueNoise(xz * 0.02) * 6.0, 0.0);
                    float2 field = q / _Paddy.xy;
                    float2 cell = floor(field), inCell = frac(field) * _Paddy.xy;
                    float edge = min(min(inCell.x, _Paddy.x - inCell.x), min(inCell.y, _Paddy.y - inCell.y));
                    half dike = 1.0 - smoothstep(_Paddy.z * 0.5, _Paddy.z, edge);
                    half kind = EnvValueNoise(cell + 0.5 * cell.yx + 17.0);
                    half flooded = step(kind, _Paddy.w);
                    half grown = smoothstep(_Paddy.w, 1.0, kind);
                    // Rice from vivid young green to yellowing ripe, each field its own shade, rows faintly combed along it.
                    half rows = 0.92 + 0.08 * sin(inCell.y * 9.0);
                    half3 rice = lerp(half3(0.17, 0.4, 0.05), half3(0.5, 0.46, 0.11), grown * grown)
                        * lerp(0.85, 1.12, EnvValueNoise(xz * 0.7)) * lerp(0.9, 1.1, frac(kind * 7.3)) * rows;
                    // Clumps: the rice stands in tufts, lit tops over shaded hollows, so a field is never one flat green.
                    rice *= 0.78 + 0.4 * EnvValueNoise(q * 2.3) * (0.7 + 0.3 * EnvValueNoise(q * 0.45 + 9.1));
                    half3 water = half3(0.05, 0.11, 0.08);
                    half3 fieldColour = lerp(rice, water, flooded);
                    // Wet, dark soil showing along the foot of each bund.
                    half bundFoot = (1.0 - smoothstep(_Paddy.z, _Paddy.z + 0.7, edge)) * (1.0 - flooded);
                    fieldColour = lerp(fieldColour, half3(0.1, 0.09, 0.06), bundFoot * 0.65);
                    // A little rice pricks through the flooded fields near their dikes.
                    fieldColour = lerp(fieldColour, half3(0.17, 0.4, 0.05), flooded * (1.0 - smoothstep(0.6, 2.2, edge)) * 0.5);
                    // Bunds: a grassy crest on a pale earth path, lighter than the fields so the quilt reads from afar.
                    half3 dikeColour = lerp(half3(0.4, 0.34, 0.2), half3(0.3, 0.36, 0.13), smoothstep(0.3, 0.7, EnvValueNoise(xz * 0.9)));
                    colour = lerp(colour, lerp(fieldColour, dikeColour, dike), onPlain);
                    gloss = lerp(gloss, lerp(lerp(0.15 + 0.35 * bundFoot, 0.9, flooded), 0.15, dike), onPlain);
                    mirror = flooded * (1.0 - dike) * onPlain;
                    paint = max(paint, onPlain);
                }

                // Terraces on the south bank: strips along the height contours, a riser of grass and bare earth below
                // each one; some strips hold water.
                // Only on a real slope: on near-flat ground one contour band would spread into a field-sized strip.
                half tanSlope = sqrt(saturate(1.0 - normal.y * normal.y)) / max(normal.y, 0.5);
                half slopeOk = (1.0 - smoothstep(_Terrace.w - 0.08, _Terrace.w, 1.0 - normal.y)) * smoothstep(0.035, 0.08, tanSlope);
                half terraced = smoothstep(4.0, 12.0, -s - _River.y - _River.w) * (1.0 - smoothstep(170.0, 230.0, -s)) * slopeOk
                    * smoothstep(0.3, 0.45, EnvValueNoise(xz * 0.012 + 3.7));
                UNITY_BRANCH if (terraced > 0.001)
                {
                    float level = (y + wobble * 0.5) / _Terrace.x;
                    float step0 = floor(level), t = frac(level);
                    half riser = 1.0 - smoothstep(_Terrace.y * 0.6, _Terrace.y, t);
                    half kind = EnvValueNoise(float2(step0 * 3.1, EnvValueNoise(xz * 0.025) * 3.0));
                    half flooded = step(kind, _Terrace.z);
                    half3 strip = lerp(lerp(half3(0.2, 0.33, 0.06), half3(0.36, 0.38, 0.1), smoothstep(0.55, 0.95, kind)), half3(0.1, 0.11, 0.08), flooded);
                    half3 riserColour = lerp(half3(0.22, 0.17, 0.1), half3(0.15, 0.2, 0.07), EnvValueNoise(xz * 0.3));
                    colour = lerp(colour, lerp(strip, riserColour, riser), terraced);
                    gloss = lerp(gloss, lerp(lerp(0.15, 0.85, flooded), 0.1, riser), terraced);
                    mirror = max(mirror, flooded * (1.0 - riser) * terraced);
                    paint = max(paint, terraced);
                }
                albedo = lerp(albedo, colour, paint);
                smoothness = lerp(smoothness, gloss, paint);
                return paint;
            }

            // Wheel tracks across a road at signed distance s from its centreline (m): a shallow, pressed-down
            // band under each tyre with soft shoulders, soil pushed up a little beside it. Tyres 0.925 m either side of the centreline (the ZIL-157 at the depot).
            float RutHeight(float s, float depth, float width)
            {
                float g = abs(abs(s) - 0.925);
                float track = 1.0 - smoothstep(width * 0.35, width, g);
                float lip = (g - width * 1.15) / 0.1;
                return depth * (-track + 0.06 * exp(-lip * lip));
            }

            // Loose debris on the open ground, at most one piece per 22 cm cell: laterite pisolites and pebbles,
            // twigs, fallen leaves. density = (pebbles, twigs, leaves), each 0..~0.4. Returns the piece's albedo
            // and coverage (a), its surface slope in tilt and a contact shadow factor in shade.
            half4 Debris(float2 xz, half3 density, float footprint, out half2 tilt, out half shade)
            {
                const float cell = 0.22;
                float2 base = floor(xz / cell);
                half4 piece = 0;
                tilt = 0;
                shade = 1;
                half total = density.x + density.y + density.z;
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    float2 c = base + float2(i, j);
                    float h1 = EnvHash21(c), h2 = EnvHash21(c + 17.31), h3 = EnvHash21(c + 41.77), h4 = EnvHash21(c + 63.13);
                    if (h3 >= total) continue;
                    float2 d = xz - (c + 0.2 + 0.6 * float2(h1, h2)) * cell;
                    float angle = h1 * 6.2832;
                    float2 axis = float2(cos(angle), sin(angle));
                    float2 q = float2(dot(d, axis), axis.x * d.y - axis.y * d.x);   // piece space
                    float reach, size;
                    half3 albedo;
                    float2 slope;
                    if (h3 < density.x)
                    {
                        // Pebble: mostly dark iron-oxide nodules, now and then pale quartz.
                        size = 0.012 + 0.03 * h4 * h4;
                        float2 e = q / (size * float2(1.0, 0.65 + 0.35 * h2));
                        reach = length(e);
                        albedo = h2 > 0.82 ? half3(0.5, 0.46, 0.38) : lerp(half3(0.16, 0.08, 0.045), half3(0.3, 0.17, 0.09), h4);
                        slope = e * 1.1;
                    }
                    else if (h3 < density.x + density.y)
                    {
                        // Twig: a thin round stick.
                        float half_length = 0.03 + 0.1 * h4;
                        size = 0.005 + 0.006 * h2;
                        float along = clamp(q.x, -half_length, half_length);
                        float2 across = float2(q.x - along, q.y) / size;
                        reach = length(across);
                        albedo = lerp(half3(0.2, 0.16, 0.12), half3(0.36, 0.3, 0.23), h2) * (0.8 + 0.4 * h4);
                        slope = float2(0, across.y) * 1.2;
                    }
                    else
                    {
                        // Fallen leaf of the young broadleaf trees: a flat ellipse with a darker midrib, dry brown to ochre.
                        size = 0.022 + 0.03 * h4;
                        float2 e = q / (size * float2(1.0, 0.42));
                        reach = length(e);
                        albedo = lerp(half3(0.27, 0.15, 0.07), half3(0.5, 0.32, 0.11), h2) * (abs(e.y) < 0.12 ? 0.8 : 1.0);
                        if (h2 > 0.9) albedo = half3(0.42, 0.4, 0.16);
                        slope = float2(0, e.y) * 0.35;
                    }
                    float aa = footprint / size;
                    half cover = 1.0 - smoothstep(1.0 - aa, 1.0 + aa, reach);
                    // Contact shadow just outside the piece.
                    shade = min(shade, 1.0 - 0.3 * smoothstep(1.7, 1.05, reach) * (1.0 - cover));
                    if (cover > piece.a)
                    {
                        piece = half4(albedo, cover);
                        // Back to world axes.
                        tilt = float2(slope.x * axis.x - slope.y * axis.y, slope.x * axis.y + slope.y * axis.x);
                    }
                }
                return piece;
            }

            // Rounded stones at one size: in each cell of a jittered grid (a share cover of them) an elliptic dome of its
            // own size, turn and colour. Returns the nearest stone's cover, soft over the pixel; its colour, its slope
            // (dh/dx, dh/dz) and the contact shade around it.
            half Stones(float2 xz, float cell, half cover, float footprint, out half3 colour, out float2 slope, out half shade)
            {
                float2 base = floor(xz / cell);
                half best = 0.0;
                colour = 0.0;
                slope = 0.0;
                shade = 1.0;
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    float2 c = base + float2(i, j);
                    float h1 = EnvHash21(c + 3.1), h2 = EnvHash21(c + 27.4), h3 = EnvHash21(c + 51.9), h4 = EnvHash21(c + 77.7);
                    if (h3 >= cover) continue;
                    float2 d = xz - (c + 0.25 + 0.5 * float2(h1, h2)) * cell;
                    float angle = h4 * 6.2832;
                    float2 axis = float2(cos(angle), sin(angle));
                    float2 q = float2(dot(d, axis), axis.x * d.y - axis.y * d.x);
                    float size = cell * (0.3 + 0.22 * h2);
                    float2 e = q / (size * float2(1.0, 0.6 + 0.35 * h1));
                    float reach = length(e);
                    float aa = footprint / size;
                    half on = 1.0 - smoothstep(0.92 - aa, 1.0 + aa, reach);
                    shade = min(shade, 1.0 - 0.35 * smoothstep(1.6, 1.0, reach) * (1.0 - on));
                    if (on > best)
                    {
                        best = on;
                        // River stones: grey limestone and brown sandstone, now and then a dark one or a pale one.
                        colour = h4 < 0.5 ? lerp(half3(0.36, 0.35, 0.32), half3(0.52, 0.5, 0.45), h1) : lerp(half3(0.34, 0.27, 0.19), half3(0.48, 0.39, 0.27), h1);
                        colour = h2 > 0.9 ? half3(0.17, 0.16, 0.14) : (h2 < 0.06 ? half3(0.64, 0.62, 0.56) : colour);
                        float2 dome = e * 0.9 / max(1.0, aa * 2.0);
                        slope = float2(dome.x * axis.x - dome.y * axis.y, dome.x * axis.y + dome.y * axis.x);
                    }
                }
                return best;
            }

            // The river's bed, depth metres under its surface: wet sand, gravel and rounded cobbles gathered in bands, a few
            // big mossy stones, dark weed in the deeper water, and a film of green algae thickening with the depth.
            void RiverBed(float2 xz, float depth, float footprint, float time, inout half3 albedo, inout half3 normalTS, inout half smoothness, inout half occlusion)
            {
                half grain = EnvValueNoise(xz * 3.1);
                half3 bed = lerp(half3(0.36, 0.31, 0.23), half3(0.47, 0.42, 0.32), EnvValueNoise(xz * 0.7)) * (0.85 + 0.3 * grain);
                float2 slope = float2(grain - EnvValueNoise(xz * 3.1 + float2(0.05, 0)), grain - EnvValueNoise(xz * 3.1 + float2(0, 0.05))) * 2.0;
                half rough = smoothstep(0.35, 0.7, EnvValueNoise(xz * float2(0.25, 0.6) + 3.3));
                half3 colour;
                float2 dome;
                half shade, cover, shadeAll = 1.0;
                // Pebbles first (they fade where finer than the pixel), then cobbles, then the big stones over all.
                cover = Stones(xz + 2.3, 0.11, lerp(0.35, 0.8, rough), footprint, colour, dome, shade) * (1.0 - smoothstep(0.02, 0.05, footprint));
                bed = lerp(bed, colour, cover);
                slope = lerp(slope, dome, cover);
                shadeAll *= lerp(1.0, shade, 0.6);
                cover = Stones(xz, 0.34, lerp(0.2, 0.85, rough), footprint, colour, dome, shade);
                bed = lerp(bed, colour, cover);
                slope = lerp(slope, dome, cover);
                shadeAll *= shade;
                half big = Stones(xz + 7.1, 1.8, 0.16, footprint, colour, dome, shade);
                colour = lerp(colour * 0.85, half3(0.17, 0.23, 0.1), smoothstep(0.35, 0.8, EnvValueNoise(xz * 1.7)) * 0.65);
                bed = lerp(bed, colour, big);
                slope = lerp(slope, dome * 1.4, big);
                shadeAll *= shade;
                // Weed: dark green tresses combed out along the current (the river runs west to east), swaying in it.
                float sway = sin(time * 1.3 + xz.y * 2.1) * 0.12;
                half weed = smoothstep(0.58, 0.78, EnvValueNoise(float2(xz.x * 0.35 + sway, xz.y * 1.6))) * smoothstep(0.6, 1.2, depth) * (1.0 - big);
                bed = lerp(bed, half3(0.06, 0.1, 0.04), weed * 0.85);
                // Algae, and everything darker for being wet.
                bed *= lerp(half3(1, 1, 1), half3(0.7, 0.82, 0.55), saturate(depth / 1.6) * 0.8) * 0.8;
                half under = smoothstep(0.0, 0.08, depth);
                albedo = lerp(albedo, bed, under);
                normalTS = normalize(lerp(normalTS, normalize(half3(slope, 1.0)), under));
                smoothness = lerp(smoothness, 0.35, under);
                occlusion = lerp(occlusion, occlusion * shadeAll, under);
            }

            // Caustics: the ripples focus the sun into a moving net of bright lines on the bed. Two drifting layers of
            // cells whose points wander; bright along the cells' edges (where two points are nearly as close). Where the
            // net is finer than the pixel, its average.
            half Caustics(float2 xz, float time, float footprint)
            {
                half net = 0.0;
                [unroll] for (int layer = 0; layer < 2; layer++)
                {
                    float size = layer == 0 ? 0.9 : 0.6;
                    float2 p = xz / size + float2(time * 0.3, time * 0.04) * (layer == 0 ? 1.0 : -0.8) + layer * 13.7;
                    float2 base = floor(p);
                    float f1 = 9.0, f2 = 9.0;
                    for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        float2 c = base + float2(i, j);
                        float h1 = EnvHash21(c + layer * 7.3), h2 = EnvHash21(c + 19.1 + layer * 7.3);
                        float2 point2 = c + 0.5 + 0.38 * sin(time * (0.7 + 0.6 * h1) + 6.2832 * float2(h1, h2));
                        float d = length(p - point2);
                        f2 = d < f1 ? f1 : min(f2, d);
                        f1 = min(f1, d);
                    }
                    half edgeLine = 1.0 - smoothstep(0.0, 0.16, f2 - f1);
                    net += edgeLine * edgeLine * 0.6;
                }
                return lerp(net, 0.2, smoothstep(0.03, 0.12, footprint));
            }

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float skinAO : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(GroundAttributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                // A select, not a lerp: the terrain has no uv0, so whatever it reads there must not reach the colour.
                output.skinAO = _SkinAO > 0.5 ? input.uv.x : 1.0;
                return output;
            }

            half4 Frag(Varyings input
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
                ) : SV_Target
            {
            #ifdef _WRITE_RENDERING_LAYERS
                // Decal layers: screen-space decals read the ground's rendering layers from here.
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = input.positionWS;
                float2 xz = positionWS.xz;
                float2 splatUV = (xz - _SplatRect.xy) * _SplatRect.zw;
                half4 splat = SAMPLE_TEXTURE2D(_SplatMap, sampler_SplatMap, splatUV);
                half4 tone = SAMPLE_TEXTURE2D(_ToneMap, sampler_SplatMap, splatUV);
                half4 relief = SAMPLE_TEXTURE2D(_ReliefMap, sampler_SplatMap, splatUV);
                float4 road = SAMPLE_TEXTURE2D(_RoadField, sampler_SplatMap, splatUV);
                float cameraDistance = distance(positionWS, GetCameraPositionWS());
                float3 geometricNormal = normalize(input.normalWS);

                // Where the baked paint holds (the splat, faded before its clamped edge) and where the open meadow
                // takes over: past the playfield's edge, except over the shelled valley.
                float2 splatEdge = min(splatUV, 1.0 - splatUV);
                half inSplat = smoothstep(0.0, 0.06, min(splatEdge.x, splatEdge.y));
                float2 outside2 = max(abs(xz) - _Playfield.xy, 0.0);
                float outside = length(outside2);
                half bombed = Bombed(xz) * smoothstep(-14.0, -20.0, xz.y) * inSplat;
                half meadow = max(smoothstep(2.0, 10.0, outside) * (1.0 - bombed), 1.0 - inSplat);
                // Past the splat the road runs on from the far field: a worn earth bed whose ruts soften and which
                // fades into the grass before the terrain's edge.
                half roadSoil = 0.0;
                UNITY_BRANCH if (inSplat < 0.999)
                {
                    float4 roadFar = SAMPLE_TEXTURE2D(_RoadFar, sampler_SplatMap, (xz - _RoadFarRect.xy) * _RoadFarRect.zw);
                    half roadFade = 1.0 - smoothstep(130.0, 195.0, length(xz));
                    roadFar.g *= roadFade * (1.0 - smoothstep(64.0, 120.0, length(xz)) * 0.6);
                    road = lerp(roadFar, road, inSplat);
                    roadSoil = (1.0 - smoothstep(1.7, 2.9, abs(roadFar.r) + (EnvValueNoise(xz * 0.4) - 0.5) * 0.9)) * roadFade * (1.0 - inSplat);
                }
                half dry, woods, bare;
                EnvMeadowFields(xz, dry, woods, bare);
                half farFade = smoothstep(_Far.x, _Far.y, cameraDistance);

                // Hill-scale relief on the slopes past the station, then the frame for the top-down projection.
                // Faces looking straight along z (a mound's steep side) fall back to world y so it never degenerates.
                float3 surfaceNormal = EnvHillNormal(xz, geometricNormal, _Far.z * meadow * smoothstep(4.0, 20.0, outside));
                float3 tangent = cross(surfaceNormal, float3(0, 0, 1));
                tangent = normalize(dot(tangent, tangent) > 1e-4 ? tangent : cross(surfaceNormal, float3(0, 1, 0)));
                float3 bitangent = cross(tangent, surfaceNormal);

                // Micro-relief: baked hollows, path beds, potholes and blast pits, plus the wheel ruts cut from the
                // road's signed distance. Height in metres relative to the mesh, slope as dh/dx, dh/dz.
                // Width and depth wander along the road, so the tracks fade in and out rather than run like rails.
                float rutWidth = lerp(0.18, 0.28, EnvValueNoise(xz * 0.45 + 7.3));
                float rutDepth = _Relief.z * road.g * lerp(0.25, 1.0, EnvValueNoise(xz * 0.3)) * (0.85 + 0.3 * EnvValueNoise(xz * 2.3));
                float rut = RutHeight(road.r, rutDepth, rutWidth);
                float rutSlope = (RutHeight(road.r + 0.01, rutDepth, rutWidth) - RutHeight(road.r - 0.01, rutDepth, rutWidth)) / 0.02;
                float microHeight = (relief.b - 0.5) * 2.0 * _Relief.y + rut;
                float2 microSlope = (relief.rg - 0.5) + rutSlope * road.ba;
                // Rain cuts rills down every slope steeper than a few degrees: narrow branching grooves running
                // downhill, so a bank reads as eroded earth rather than a smooth clay dome.
                half sloped = saturate((0.996 - geometricNormal.y) * 14.0) * saturate((70.0 - cameraDistance) / 20.0) * (1.0 - meadow * 0.7);
                if (sloped > 0.001)
                {
                    float2 downhill = normalize(geometricNormal.xz + 1e-5), across = float2(-downhill.y, downhill.x);
                    float2 rillUV = float2(dot(xz, across) * 1.7 + EnvValueNoise(xz * 0.6) * 1.5, dot(xz, downhill) * 0.22);
                    float n0 = EnvValueNoise(rillUV), n1 = EnvValueNoise(rillUV + float2(0.06, 0));
                    float r0 = smoothstep(0.6, 0.85, n0), r1 = smoothstep(0.6, 0.85, n1);
                    float rillDepth = 0.07 * sloped;
                    microHeight -= r0 * rillDepth;
                    microSlope -= across * (r1 - r0) / (0.06 / 1.7) * rillDepth;
                }
                // 0..1 under a tyre track: the soil there is pressed darker and smoother, not dug out.
                half groove = saturate(-rut / max(_Relief.z * 0.5, 1e-3)) * saturate(road.g * 1.5);
                half compaction = relief.a;

                // Layer weights. Station: the splat; meadow: grass, pine floor under the woods, worn soil. Roads, crater
                // ejecta and wet ground stay painted in both. Rock takes the steeps everywhere.
                half dampness0 = saturate(tone.g * 1.5 - 0.3 - microHeight * 6.0 + groove * 0.15);
                half wetMud = saturate(splat.g * 1.8 + dampness0 * splat.r * 0.3);
                half4 station = half4(splat.r * (1.0 - wetMud * 0.6), wetMud, saturate(1.0 - splat.r - splat.a), splat.a);
                // Worn patches in the open sward (game trails, sheep scrapes) and scree: loose stones and soil on the
                // band of slope just below the rock, so an outcrop sheds into the grass rather than ending on a line.
                half worn = smoothstep(0.72, 0.84, EnvFbm(xz / 3.3 + 8.2)) * (1.0 - woods) * 0.7;
                half scree = saturate(EnvRockCover(xz, geometricNormal.y + 0.12, _RockSlope.xy) - EnvRockCover(xz, geometricNormal.y, _RockSlope.xy))
                    * smoothstep(0.35, 0.65, EnvValueNoise(xz * 0.7 + 4.1));
                half meadowSoil = max(max(max(bare * 0.8, splat.r), max(worn, scree)), roadSoil);
                half4 open = half4(meadowSoil, wetMud, woods * (1.0 - meadowSoil), (1.0 - woods) * (1.0 - meadowSoil));
                half4 weight4 = lerp(station, open, meadow);
                half rockCover = EnvRockCover(xz, geometricNormal.y, _RockSlope.xy) * (1.0 - wetMud);
                weight4 *= 1.0 - rockCover;

                // Height blend: the highest surface in a mix wins, so mud fills the low spots between stones and grass
                // pokes through the needles. The mask heights decide, sampled before the rest of each layer.
                half anti = lerp(0.25 + 0.5 * EnvValueNoise(xz / 23.0), 0.85, smoothstep(8.0, 40.0, cameraDistance));
                float2 uvS = xz * _Tiling.x, uvM = xz * _Tiling.y, uvN = xz * _Tiling.z, uvG = xz * _Tiling.w;
                float3 dpx = ddx(positionWS), dpy = ddy(positionWS);
                float2 dxz = dpx.xz, dyz = dpy.xz;
                bool steep = geometricNormal.y < 0.92;
                Layer soil = EmptyLayer(), mud = EmptyLayer(), needles = EmptyLayer(), grass = EmptyLayer(), rock = EmptyLayer();
                UNITY_BRANCH if (weight4.x > 0.003)
                {
                    if (steep)
                        soil = SampleTriplanar(TEXTURE2D_ARGS(_SoilDiffuse, sampler_SoilDiffuse), _SoilNormal, _SoilMask, positionWS, dpx, dpy, surfaceNormal, _Tiling.x,
                            _NormalScale.x, _SoilTint.rgb, _MaskMin0, _MaskMax0, tangent, bitangent);
                    else
                        soil = SampleTop(TEXTURE2D_ARGS(_SoilDiffuse, sampler_SoilDiffuse), _SoilNormal, _SoilMask, uvS, dxz * _Tiling.x, dyz * _Tiling.x,
                            anti, _NormalScale.x, _SoilTint.rgb, _MaskMin0, _MaskMax0);
                }
                UNITY_BRANCH if (weight4.y > 0.003)
                    mud = SampleTop(TEXTURE2D_ARGS(_MudDiffuse, sampler_SoilDiffuse), _MudNormal, _MudMask, uvM, dxz * _Tiling.y, dyz * _Tiling.y,
                        anti * 0.5, _NormalScale.y, _MudTint.rgb, _MaskMin1, _MaskMax1);
                UNITY_BRANCH if (weight4.z > 0.003)
                {
                    needles = SampleTop(TEXTURE2D_ARGS(_NeedlesDiffuse, sampler_SoilDiffuse), _NeedlesNormal, _NeedlesMask, uvN, dxz * _Tiling.z, dyz * _Tiling.z,
                        anti, _NormalScale.z, _NeedlesTint.rgb, _MaskMin2, _MaskMax2);
                    // Moss where the shade holds the damp: in hollows and damp ground, in patches.
                    half patch = smoothstep(0.45, 0.62, EnvValueNoise(xz / 3.1 - 6.2) * 0.75 + EnvValueNoise(xz / 1.3 + 4.4) * 0.25
                        + dampness0 * 0.3 + saturate(-microHeight * 3.0) * 0.2);
                    UNITY_BRANCH if (patch > 0.003)
                        MixPatches(needles, TEXTURE2D_ARGS(_MossDiffuse, sampler_SoilDiffuse), _MossNormal, _MossMask, xz, dxz, dyz, _MossTiling,
                            _MossTint.rgb, _MaskMin6, _MaskMax6, anti, patch);
                }
                UNITY_BRANCH if (weight4.w > 0.003)
                {
                    grass = SampleTop(TEXTURE2D_ARGS(_GrassDiffuse, sampler_SoilDiffuse), _GrassNormal, _GrassMask, uvG, dxz * _Tiling.w, dyz * _Tiling.w,
                        anti, _NormalScale.w, _GrassTint.rgb, _MaskMin3, _MaskMax3);
                    // Patches a few metres across of a lighter, flowering sward.
                    half patch = smoothstep(0.5, 0.66, EnvValueNoise(xz / 4.3 + 11.9) * 0.7 + EnvValueNoise(xz / 1.7 - 2.3) * 0.3);
                    UNITY_BRANCH if (patch > 0.003)
                        MixPatches(grass, TEXTURE2D_ARGS(_Grass2Diffuse, sampler_SoilDiffuse), _Grass2Normal, _Grass2Mask, xz, dxz, dyz, _Grass2Tiling,
                            _Grass2Tint.rgb, _MaskMin5, _MaskMax5, anti, patch);
                }
                UNITY_BRANCH if (rockCover > 0.003)
                    rock = SampleTriplanar(TEXTURE2D_ARGS(_RockDiffuse, sampler_SoilDiffuse), _RockNormal, _RockMask, positionWS, dpx, dpy, surfaceNormal, _RockTiling.x,
                        _RockTiling.y, _RockTint.rgb, _MaskMin4, _MaskMax4, tangent, bitangent);

                // The scan is a deep wine red: pulled towards a dusty red-brown, a little less saturated.
                soil.albedo = lerp(dot(soil.albedo, half3(0.2126, 0.7152, 0.0722)).xxx, soil.albedo, _SoilSaturation);
                half3 rawSoil = soil.albedo * _RedTint.rgb;
                // Broad soil tone: raw red patches, pale trodden dust.
                soil.albedo = lerp(soil.albedo, rawSoil, tone.b);
                soil.albedo = lerp(soil.albedo, _DustColor.rgb * (0.82 + 0.36 * soil.height), tone.r * 0.8);
                // Trodden and compacted ground is smoother: its grain is pressed flat.
                soil.normalTS = normalize(lerp(soil.normalTS, half3(0, 0, 1), saturate(tone.r * 0.5 + compaction * 0.45)));
                // The meadow: lush in the hollows, straw on the crests and sun-facing slopes, mottled tussock by tussock;
                // each tussock a low mound in the normal, so the sward never reads as a mown lawn.
                // Value noise sits on a square lattice: rotated and warped so no grid shows through the tussocks.
                float2 tuftUV = mul(float2x2(0.8, -0.6, 0.6, 0.8), xz) * 1.7 + EnvValueNoise(xz * 0.9 + 1.3) * 1.2;
                half tussock = EnvValueNoise(tuftUV);
                grass.albedo *= EnvMeadowTint(EnvMeadowDryness(dry, geometricNormal, positionWS.y)) * lerp(0.74, 1.12, EnvFbm(xz / 5.5 + 3.1))
                    * lerp(0.84, 1.08, tussock);
                UNITY_BRANCH if (weight4.w > 0.003 && cameraDistance < 45.0)
                {
                    float tx = EnvValueNoise(tuftUV + float2(0.12, 0)), tz = EnvValueNoise(tuftUV + float2(0, 0.12));
                    half2 tuft = half2(tx - tussock, tz - tussock) / 0.12 * 0.05 * saturate((45.0 - cameraDistance) / 15.0);
                    grass.normalTS = normalize(half3(grass.normalTS.xy - tuft, grass.normalTS.z));
                }

                half dampness = saturate(dampness0 + (0.5 - soil.height) * 0.7 * weight4.x);
                half wSum = dot(weight4, half4(1, 1, 1, 1)) + rockCover;
                half4 heights4 = half4(soil.height, 1.0 - mud.height, needles.height, grass.height);
                half4 blend4 = (weight4 + heights4 * 0.45) * step(0.02, weight4);
                half blendR = (rockCover + rock.height * 0.45) * step(0.02, rockCover);
                half top = max(max(max(blend4.x, blend4.y), max(blend4.z, blend4.w)), blendR);
                blend4 = max(blend4 - (top - 0.22), 0.0);
                blendR = max(blendR - (top - 0.22), 0.0);
                half norm = max(dot(blend4, half4(1, 1, 1, 1)) + blendR, 1e-3);
                blend4 /= norm;
                blendR /= norm;

                half3 albedo = soil.albedo * blend4.x + mud.albedo * blend4.y + needles.albedo * blend4.z + grass.albedo * blend4.w + rock.albedo * blendR;
                // The sward under a gust front brightens like the blades over it (EnvGrass), so the wave reads across the meadow.
                UNITY_BRANCH if (blend4.w * _GustSheen > 0.01) albedo *= 1.0 + EnvGrassGust(xz, _Time.y) * blend4.w * _GustSheen;
                half3 normalTS = normalize(soil.normalTS * blend4.x + mud.normalTS * blend4.y + needles.normalTS * blend4.z + grass.normalTS * blend4.w + rock.normalTS * blendR);
                half height = dot(heights4, blend4) + rock.height * blendR;
                half occlusion = dot(half4(soil.occlusion, mud.occlusion, needles.occlusion, grass.occlusion), blend4) + rock.occlusion * blendR;
                half smoothness = dot(half4(soil.smoothness, mud.smoothness, needles.smoothness, grass.smoothness), blend4) + rock.smoothness * blendR;

                albedo *= lerp(half3(1, 1, 1), _DampColor.rgb, dampness * (1.0 - meadow * 0.6));
                albedo *= lerp(0.88, 1.08, EnvValueNoise(xz / 52.0)) * lerp(_ValueRange.x, _ValueRange.y, lerp(tone.a, 0.5, meadow));
                // Hollows gather shade; tyres press the soil darker and flatten its grain.
                occlusion *= 1.0 - saturate(-microHeight * 4.0) * 0.3;
                albedo *= 1.0 - groove * 0.16;
                normalTS = normalize(lerp(normalTS, half3(0, 0, 1), groove * 0.55));

                // Steep banks near the station (the road cutting, mound flanks) shed their cover: raw laterite streaked by runoff.
                half bank = saturate((0.985 - geometricNormal.y) * 5.0) * saturate((70.0 - length(xz)) / 15.0) * (1.0 - meadow) * (1.0 - rockCover);
                if (bank > 0.001)
                {
                    float2 contour = normalize(float2(-geometricNormal.z, geometricNormal.x) + 1e-5);
                    half streak = EnvValueNoise(float2(dot(xz, contour) * 2.6, positionWS.y * 0.35)) * 0.7 + EnvValueNoise(xz * 0.9) * 0.3;
                    half exposed = saturate(bank * 1.3 - 0.15 + (streak - 0.5) * 0.9);
                    half3 bankSoil = weight4.x > 0.003 ? rawSoil : _DustColor.rgb * 0.8;
                    albedo = lerp(albedo, bankSoil * (0.62 + 0.45 * streak) * lerp(1.0, 0.8, tone.g), exposed);
                    normalTS = normalize(lerp(normalTS, soil.normalTS, exposed));
                    height = lerp(height, soil.height, exposed);
                }

                // Debris near the camera: pebbles on bare earth, twigs and leaves where needles lie.
                half debrisFade = saturate((_Relief.w - cameraDistance) / 8.0);
                float footprint = max(length(fwidth(xz)), 1e-4);
                half3 density = half3(blend4.x * (0.22 + 0.16 * (1.0 - compaction)), blend4.z * 0.2 + blend4.x * 0.03, blend4.z * 0.12 + blend4.x * 0.04 + blend4.w * 0.03)
                    + half3(blend4.w * 0.06 * meadow + scree * 0.3, 0, 0)
                    * debrisFade * (1.0 - saturate(splat.g * 2.0 - 0.6)) * (1.0 - splat.b);
                if (density.x + density.y + density.z > 0.01)
                {
                    half2 tilt;
                    half shade;
                    half4 piece = Debris(xz, density, footprint, tilt, shade);
                    albedo = lerp(albedo, piece.rgb, piece.a);
                    normalTS = normalize(lerp(normalTS, normalize(half3(tilt, 1)), piece.a));
                    occlusion *= shade;
                    height = lerp(height, 1.0, piece.a);
                    smoothness = lerp(smoothness, 0.25, piece.a);
                }

                // Ash and scorch.
                albedo = lerp(albedo, _AshColor.rgb * (0.75 + height * 0.5), splat.b);

                // Wet ground darkens and glosses; deep wet pools into standing water at the bottom of hollows and ruts.
                half wet = splat.g;
                albedo = lerp(albedo, albedo * _MudColor.rgb, saturate(wet * 1.4));
                // Wet mud is satin, not mirror-like: a sharp sun glint along a rut reads as a bright line.
                smoothness = saturate(smoothness * 0.6 + wet * 0.22 + dampness * 0.05 + compaction * 0.04 + groove * 0.08);
                // Water only stands where the ground is soaked and low; the edge of a pool is soft, wet mud.
                half water = saturate((wet - 0.64 + (0.5 - height) * 0.4 - microHeight * 1.5) * 3.0);
                // Relief and grain shade the surface; standing water lies flat over them.
                half3 reliefTS = normalize(half3(-microSlope * _Relief.x, 1.0));
                normalTS = normalize(half3(normalTS.xy + reliefTS.xy, normalTS.z * reliefTS.z));
                normalTS = normalize(lerp(normalTS, half3(0, 0, 1), water * 0.6));
                albedo = lerp(albedo, _WaterColor.rgb, water);
                // Shallow, muddy water: a soft glint, a faint mirror of the sky (below), no sheen or black hole.
                smoothness = lerp(smoothness, 0.42, water);
                occlusion = lerp(occlusion, 0.5, water);

                // Far away the texture grain is below a pixel: the meadow reads by its broad colour fields instead, the
                // same as the backdrop's near rings. Each layer's average is its last mip.
                UNITY_BRANCH if (farFade > 0.001)
                {
                    half3 avgGrass = SAMPLE_TEXTURE2D_LOD(_GrassDiffuse, LAYER_SAMPLER, 0.5, 12).rgb * _GrassTint.rgb;
                    half3 avgNeedles = SAMPLE_TEXTURE2D_LOD(_NeedlesDiffuse, LAYER_SAMPLER, 0.5, 12).rgb * _NeedlesTint.rgb;
                    half3 avgSoil = SAMPLE_TEXTURE2D_LOD(_SoilDiffuse, LAYER_SAMPLER, 0.5, 12).rgb * _SoilTint.rgb;
                    half3 avgRock = SAMPLE_TEXTURE2D_LOD(_RockDiffuse, LAYER_SAMPLER, 0.5, 12).rgb * _RockTint.rgb;
                    half3 farAlbedo = EnvFarGround(positionWS, geometricNormal, avgGrass, avgNeedles, avgSoil, avgRock, _RockSlope.xy);
                    farAlbedo = lerp(farAlbedo, avgSoil * 0.9, roadSoil * 0.85);
                    half farMix = farFade * meadow * (1.0 - water);
                    albedo = lerp(albedo, farAlbedo, farMix);
                    normalTS = normalize(lerp(normalTS, half3(0, 0, 1), farFade * 0.6));
                    smoothness = lerp(smoothness, 0.12, farMix);
                    occlusion = lerp(occlusion, 0.9, farMix);
                }

                // The river valley (past the splat only): banks, paddies and terraces paint over the meadow.
                half mirror = 0.0;
                UNITY_BRANCH if (inSplat < 0.999 && positionWS.y < _River.x + 40.0)
                {
                    float riverSide = SAMPLE_TEXTURE2D_LOD(_RiverField, sampler_SplatMap, (xz - _RiverRect.xy) * _RiverRect.zw, 0).r;
                    half valleyPaint = RiverValleyPaint(xz, riverSide, positionWS.y, geometricNormal, albedo, smoothness, mirror);
                    normalTS = normalize(lerp(normalTS, half3(0, 0, 1), valleyPaint * 0.7));
                    occlusion = lerp(occlusion, 1.0, valleyPaint * 0.5);
                }
                // Under the river (its water is clear, EnvRiver): the bed, and the caustics on it in sun.
                half caustic = 0.0;
                UNITY_BRANCH if (positionWS.y < _River.x + 0.05)
                {
                    float riverSide = SAMPLE_TEXTURE2D_LOD(_RiverField, sampler_SplatMap, (xz - _RiverRect.xy) * _RiverRect.zw, 0).r;
                    UNITY_BRANCH if (abs(riverSide) < _River.y + 4.0)
                    {
                        float depthUnder = _River.x - positionWS.y;
                        float bedFootprint = max(length(fwidth(xz)), 1e-4);
                        float time = fmod(_Time.y, 600.0);
                        RiverBed(xz, depthUnder, bedFootprint, time, albedo, normalTS, smoothness, occlusion);
                        half sunny = 1.0 - smoothstep(0.33, 0.42, _EnvHaze.x);
                        caustic = Caustics(xz, time, bedFootprint) * smoothstep(0.03, 0.3, depthUnder) * (1.0 - smoothstep(1.0, 2.6, depthUnder)) * sunny;
                    }
                }
                // The karst wall: Mountain Lake's tiled cliff (the rock of ridge's far towers too) on the two vertical planes
                // at two scales, toned as ridge's far karst: pale cream and grey limestone, warm orange iron washes, grey
                // water streaks down vertical fluting, dark wet stains below the falls, moss on the ledges. Its height
                // decides where moss holds (ledges and the low ground of the rock first) and how far the spray darkens it.
                UNITY_BRANCH if (inSplat < 0.999 && xz.x > _Karst.x && xz.x < _Karst.y && positionWS.y > _River.x)
                {
                    half steep = 1.0 - geometricNormal.y;
                    half wall = smoothstep(0.22, 0.5, steep) * smoothstep(_Karst.x, _Karst.x + 25.0, xz.x) * (1.0 - smoothstep(_Karst.y - 50.0, _Karst.y, xz.x));
                    UNITY_BRANCH if (wall > 0.001)
                    {
                        float2 w = abs(geometricNormal.xz) / max(abs(geometricNormal.x) + abs(geometricNormal.z), 1e-3);
                        float tile = 1.0 / _KarstTex.x;
                        float2 pa = positionWS.zy * tile, pb = positionWS.xy * tile;
                        float2 ax = dpx.zy * tile, ay = dpy.zy * tile, bx = dpx.xy * tile, by = dpy.xy * tile;
                        half4 coarse = SAMPLE_TEXTURE2D_GRAD(_KarstRock, LAYER_SAMPLER, pa, ax, ay) * w.x + SAMPLE_TEXTURE2D_GRAD(_KarstRock, LAYER_SAMPLER, pb, bx, by) * w.y;
                        half4 fine = SAMPLE_TEXTURE2D_GRAD(_KarstRock, LAYER_SAMPLER, pa * 3.7 + 0.31, ax * 3.7, ay * 3.7) * w.x
                            + SAMPLE_TEXTURE2D_GRAD(_KarstRock, LAYER_SAMPLER, pb * 3.7 + 0.57, bx * 3.7, by * 3.7) * w.y;
                        half4 texel = coarse * 0.6 + fine * 0.4;

                        float2 face = float2(xz.x * 0.8 + xz.y * 0.6, positionWS.y);
                        // Fluting: water-cut grooves a few metres apart running down the face, ribs between them.
                        half groove = EnvFbm(float2(face.x / 4.5, face.y / 70.0));
                        half wash = EnvFbm(float2(face.x, face.y * 1.7) / 60.0);
                        half3 lime = lerp(half3(0.78, 0.76, 0.7), half3(0.88, 0.79, 0.64), smoothstep(0.4, 0.7, wash));
                        lime *= 0.86 + 0.28 * EnvValueNoise(float2(face.x, face.y) / 17.0);
                        lime *= pow(max(dot(texel.rgb, half3(0.333, 0.333, 0.333)) / 0.453, 0.0), _KarstTex.y) * (0.8 + 0.4 * texel.a);
                        // Iron: broad warm orange washes bleeding down from the bedding.
                        half iron = smoothstep(0.5, 0.74, EnvFbm(float2(face.x / 40.0 + 5.7, face.y / 110.0)));
                        lime = lerp(lime, lime * half3(1.12, 0.72, 0.44), iron * 0.7);
                        // Grey water streaks down the grooves, from every ledge; grey, not black, as on the far towers.
                        half streak = smoothstep(0.5, 0.75, EnvFbm(float2(face.x / 5.0, face.y / 55.0) + 11.3) + (0.5 - groove) * 0.3);
                        lime = lerp(lime, half3(0.3, 0.3, 0.29), streak * 0.55);
                        lime *= 0.82 + 0.26 * groove;
                        // A faint, warped bedding across the flutes (never an even stripe).
                        lime *= 0.9 + 0.16 * EnvValueNoise(float2(face.x / 260.0, face.y / 9.0 + EnvFbm(float2(face.x / 90.0, 3.1)) * 3.0));

                        float2 nearFall = (xz.xx - _Karst.zw) / float2(16.0, 7.0);
                        // Spray soaks the rock round each plunge, widest at the foot: dark and wet a few tens of metres either side.
                        half plunge = (1.0 - smoothstep(2.0, 16.0, positionWS.y - _River.x)) * saturate(exp(-nearFall.x * nearFall.x * 0.25) + exp(-nearFall.y * nearFall.y * 0.3));
                        half wetness = saturate(exp(-nearFall.x * nearFall.x) + exp(-nearFall.y * nearFall.y) + plunge);
                        // Moss: on the ledges and in the damp beside the falls, filling the rock's low ground before its bosses.
                        half ledge = (1.0 - smoothstep(0.3, 0.55, steep)) * smoothstep(0.35, 0.6, EnvValueNoise(xz * 0.15 + positionWS.y * 0.1));
                        // Moss and small green growth down the damp flutes: long streaks in the grooves, in runs along the
                        // wall, so the stone between the vine curtains reads overgrown while the ribs stay bare.
                        half flute = smoothstep(0.52, 0.7, EnvFbm(float2(face.x / 3.0 + 7.1, face.y / 28.0))) * (1.0 - smoothstep(0.35, 0.6, groove))
                            * smoothstep(0.4, 0.6, EnvFbm(float2(face.x / 30.0 + 2.3, 4.7))) * smoothstep(6.0, 12.0, positionWS.y - _River.x);
                        // And in the shaded recesses under ledge lips (the skin's baked occlusion), not on the overhangs' undersides.
                        half lip = saturate((0.85 - input.skinAO) * 3.0) * (1.0 - smoothstep(0.95, 1.1, steep));
                        half mossReach = saturate(ledge * 1.4 + flute * 0.9 + lip * 0.7 + wetness * 0.45 * smoothstep(0.35, 0.7, EnvValueNoise(face * float2(0.12, 0.06))));
                        half moss = saturate((mossReach - 0.25 + (0.5 - texel.a) * _KarstTex.z) * 2.5) * step(0.02, mossReach);
                        UNITY_BRANCH if (moss > 0.003)
                        {
                            float mt = _MossTiling.x;
                            half3 mossAlbedo = (SAMPLE_TEXTURE2D_GRAD(_MossDiffuse, LAYER_SAMPLER, positionWS.zy * mt, dpx.zy * mt, dpy.zy * mt).rgb * w.x
                                + SAMPLE_TEXTURE2D_GRAD(_MossDiffuse, LAYER_SAMPLER, positionWS.xy * mt, dpx.xy * mt, dpy.xy * mt).rgb * w.y) * _MossTint.rgb;
                            lime = lerp(lime, mossAlbedo * half3(0.72, 1.0, 0.62), moss);   // a fresher green, so the growth reads alive at 60 m
                        }
                        lime *= 1.0 - wetness * (0.3 + _KarstTex.w * (1.0 - texel.a));
                        // Below each fall the rock is stained dark where the water and spray run down it.
                        float2 belowFall = (xz.xx - _Karst.zw) / float2(26.0, 9.0);
                        half stain = saturate(exp(-belowFall.x * belowFall.x) + exp(-belowFall.y * belowFall.y)) * smoothstep(0.3, 0.6, EnvFbm(float2(face.x / 2.5, face.y / 40.0)));
                        lime = lerp(lime, half3(0.1, 0.1, 0.085), stain * 0.75);
                        albedo = lerp(albedo, lime, wall);
                        smoothness = lerp(smoothness, lerp(0.12, 0.62, wetness) * (1.0 - moss * 0.6), wall);
                        occlusion = lerp(occlusion, occlusion * (0.75 + 0.25 * texel.a) * (0.7 + 0.3 * groove), wall);
                    }
                }

                // The karst skin's recesses and the undersides of its ledges: occlusion baked from its relief (the backlit
                // face lives on ambient light, and SSAO's half-metre radius is lost at the shots' 60 m), a little into direct.
                occlusion *= input.skinAO;
                albedo *= lerp(1.0, input.skinAO, 0.35);

                float3 normalWS = normalize(normalTS.x * tangent + normalTS.y * bitangent + normalTS.z * surfaceNormal);

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.smoothness = smoothness;
                surface.normalTS = normalTS;
                surface.occlusion = occlusion;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);

                // Puddles mirror the sky analytically; the scene has no baked probes.
                float3 view = inputData.viewDirectionWS;
                float3 reflected = reflect(-view, normalWS);
                half fresnel = 0.04 + 0.96 * pow(1.0 - saturate(dot(normalWS, view)), 5.0);
                Light mainLight = GetMainLight(inputData.shadowCoord);
                color.rgb += albedo * mainLight.color * (mainLight.shadowAttenuation * caustic * 1.4);
                // Trees and banks around fill much of what a puddle reflects: the sky shows dim and tinted by silt.
                half3 reflection = EnvSkyColor(reflected) * lerp(0.1, 0.2, mainLight.shadowAttenuation) * lerp(half3(1, 1, 1), half3(0.84, 0.5, 0.32), 0.45);
                color.rgb = lerp(color.rgb, reflection, water * (0.02 + 0.12 * fresnel));
                // Flooded paddies lie open to the sky: a bright, still mirror of it.
                // Silty water darkens and greens what it mirrors, so seen from above a field reads as water, not as a grey tile.
                color.rgb = lerp(color.rgb, EnvSkyColor(reflect(-view, geometricNormal)) * half3(0.5, 0.72, 0.62), mirror * (0.08 + 0.62 * fresnel));

                return half4(EnvApplyHaze(color.rgb, positionWS), 1);
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

            float4 ShadowVert(GroundAttributes input) : SV_POSITION
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

            float4 DepthVert(GroundAttributes input) : SV_POSITION
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

            NormalsVaryings NormalsVert(GroundAttributes input)
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
