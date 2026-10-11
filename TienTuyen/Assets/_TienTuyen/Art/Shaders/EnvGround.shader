// Blended ground for the combat station: four scanned PBR layers (laterite, wet mud, leaf litter, grass)
// mixed by a world-space splat map (R laterite, G wetness/standing water, B ash, A grass; litter is the
// rest) with height-aware transitions, tinted by a soil tone map (R dry dust, G damp, B fresh red). A relief
// map (slope, height, compaction) and a road distance field shade hollows, path beds and wheel ruts the flat
// mesh does not carry; water stands at the bottom of them with analytic sky reflections. Pebbles, twigs and
// leaves are scattered procedurally near the camera, and steep banks show raw laterite.
// Each layer: albedo (A height), normal map, mask (R occlusion, G smoothness).
Shader "TienTuyen/Environment/Ground"
{
    Properties
    {
        _LateriteMap ("Laterite (A height)", 2D) = "white" {}
        _LateriteNormal ("Laterite normal", 2D) = "bump" {}
        _LateriteMask ("Laterite mask (R AO, G smoothness)", 2D) = "white" {}
        _MudMap ("Mud (A height)", 2D) = "white" {}
        _MudNormal ("Mud normal", 2D) = "bump" {}
        _MudMask ("Mud mask", 2D) = "white" {}
        _LitterMap ("Leaf litter (A height)", 2D) = "white" {}
        _LitterNormal ("Leaf litter normal", 2D) = "bump" {}
        _LitterMask ("Leaf litter mask", 2D) = "white" {}
        _GrassMap ("Grass (A height)", 2D) = "white" {}
        _GrassNormal ("Grass normal", 2D) = "bump" {}
        _GrassMask ("Grass mask", 2D) = "white" {}
        _SplatMap ("Splat", 2D) = "black" {}
        _ToneMap ("Soil tone (R dust, G damp, B red, A broad value)", 2D) = "black" {}
        _ReliefMap ("Relief (RG slope, B height, A compaction)", 2D) = "grey" {}
        _RoadField ("Road field (R signed distance, G rut strength, BA gradient)", 2D) = "black" {}
        _SplatRect ("Splat rect (min x, min z, 1/size x, 1/size z)", Vector) = (-48, -48, 0.0104167, 0.0104167)
        _Relief ("Relief: normal strength, height range (m), rut depth (m), debris reach (m)", Vector) = (1, 0.12, 0.035, 26)
        _Tiling ("Tiling per metre (laterite, mud, litter, grass)", Vector) = (0.22, 0.3, 0.16, 0.22)
        _LateriteTint ("Laterite tint", Color) = (1.5, 1.6, 1.3, 1)
        _LateriteSaturation ("Laterite saturation", Range(0, 1.5)) = 0.7
        _DustColor ("Dry compacted dust", Color) = (0.6, 0.49, 0.38, 1)
        _DampColor ("Damp soil multiplier", Color) = (0.68, 0.62, 0.58, 1)
        _RedTint ("Fresh laterite multiplier", Color) = (1.08, 0.96, 0.86, 1)
        _ValueRange ("Broad value (dark, light)", Vector) = (0.84, 1.16, 0, 0)
        _MudColor ("Wet darkening", Color) = (0.5, 0.42, 0.36, 1)
        _LitterTint ("Leaf litter tint", Color) = (0.66, 0.66, 0.52, 1)
        _GrassTint ("Grass tint", Color) = (0.9, 1, 0.8, 1)
        _AshColor ("Ash", Color) = (0.17, 0.15, 0.13, 1)
        _WaterColor ("Water", Color) = (0.17, 0.13, 0.09, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _SplatRect;
            float4 _Relief;
            float4 _Tiling;
            half4 _LateriteTint;
            half _LateriteSaturation;
            half4 _DustColor;
            half4 _DampColor;
            half4 _RedTint;
            float4 _ValueRange;
            half4 _MudColor;
            half4 _LitterTint;
            half4 _GrassTint;
            half4 _AshColor;
            half4 _WaterColor;
        CBUFFER_END

        struct GroundAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
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

            TEXTURE2D(_LateriteMap); SAMPLER(sampler_LateriteMap);
            TEXTURE2D(_LateriteNormal); TEXTURE2D(_LateriteMask);
            TEXTURE2D(_MudMap); SAMPLER(sampler_MudMap);
            TEXTURE2D(_MudNormal); TEXTURE2D(_MudMask);
            TEXTURE2D(_LitterMap); SAMPLER(sampler_LitterMap);
            TEXTURE2D(_LitterNormal); TEXTURE2D(_LitterMask);
            TEXTURE2D(_GrassMap); SAMPLER(sampler_GrassMap);
            TEXTURE2D(_GrassNormal); TEXTURE2D(_GrassMask);
            TEXTURE2D(_SplatMap); SAMPLER(sampler_SplatMap);
            TEXTURE2D(_ToneMap);
            TEXTURE2D(_ReliefMap);
            TEXTURE2D(_RoadField);

            struct Layer
            {
                half3 albedo;
                half height;
                half3 normalTS;
                half occlusion;
                half smoothness;
            };

            Layer MakeLayer(half4 albedoHeight, half4 normal, half2 mask)
            {
                Layer layer;
                layer.albedo = albedoHeight.rgb;
                layer.height = albedoHeight.a;
                layer.normalTS = UnpackNormal(normal);
                layer.occlusion = mask.r;
                layer.smoothness = mask.g;
                return layer;
            }

            // A second, rotated and rescaled albedo sample hides the tile repeat of the large layers.
            float2 AntiTileUV(float2 uv) { return float2(uv.y, -uv.x) * 0.43 + 0.37; }

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
                        // Fallen leaf: a flat ellipse with a darker midrib, dry brown to ochre.
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

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
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
                half macro = SAMPLE_TEXTURE2D(_LitterMap, sampler_LitterMap, xz * 0.019).a;
                half anti = 0.25 + 0.5 * macro;
                float cameraDistance = distance(positionWS, GetCameraPositionWS());

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
                float3 geometricNormal = normalize(input.normalWS);
                half sloped = saturate((0.996 - geometricNormal.y) * 14.0) * saturate((70.0 - cameraDistance) / 20.0);
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

                float2 uvL = xz * _Tiling.x, uvM = xz * _Tiling.y, uvF = xz * _Tiling.z, uvG = xz * _Tiling.w;
                Layer laterite = MakeLayer(
                    lerp(SAMPLE_TEXTURE2D(_LateriteMap, sampler_LateriteMap, uvL), SAMPLE_TEXTURE2D(_LateriteMap, sampler_LateriteMap, AntiTileUV(uvL)), anti),
                    SAMPLE_TEXTURE2D(_LateriteNormal, sampler_LateriteMap, uvL), SAMPLE_TEXTURE2D(_LateriteMask, sampler_LateriteMap, uvL).rg);
                Layer mud = MakeLayer(SAMPLE_TEXTURE2D(_MudMap, sampler_MudMap, uvM),
                    SAMPLE_TEXTURE2D(_MudNormal, sampler_MudMap, uvM), SAMPLE_TEXTURE2D(_MudMask, sampler_MudMap, uvM).rg);
                Layer litter = MakeLayer(
                    lerp(SAMPLE_TEXTURE2D(_LitterMap, sampler_LitterMap, uvF), SAMPLE_TEXTURE2D(_LitterMap, sampler_LitterMap, AntiTileUV(uvF)), anti),
                    SAMPLE_TEXTURE2D(_LitterNormal, sampler_LitterMap, uvF), SAMPLE_TEXTURE2D(_LitterMask, sampler_LitterMap, uvF).rg);
                Layer grassLayer = MakeLayer(SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, uvG),
                    SAMPLE_TEXTURE2D(_GrassNormal, sampler_GrassMap, uvG), SAMPLE_TEXTURE2D(_GrassMask, sampler_GrassMap, uvG).rg);

                // The scan is a deep wine red: pulled towards a dusty orange-red, a little less saturated.
                laterite.albedo = lerp(dot(laterite.albedo, half3(0.2126, 0.7152, 0.0722)).xxx, laterite.albedo, _LateriteSaturation) * _LateriteTint.rgb;
                half3 rawLaterite = laterite.albedo * _RedTint.rgb;
                // Broad soil tone on the laterite: raw red patches, pale trodden dust.
                laterite.albedo = lerp(laterite.albedo, rawLaterite, tone.b);
                laterite.albedo = lerp(laterite.albedo, _DustColor.rgb * (0.82 + 0.36 * laterite.height), tone.r * 0.8);
                // Trodden and compacted ground is smoother: its grain is pressed flat.
                laterite.normalTS = normalize(lerp(laterite.normalTS, half3(0, 0, 1), saturate(tone.r * 0.5 + compaction * 0.45)));
                litter.albedo *= _LitterTint.rgb;
                grassLayer.albedo *= _GrassTint.rgb;

                // Damp soil seeps into the low ground: hollows, ruts and the low grain of the soil, so its edge follows
                // the surface instead of drawing a soft stain over it.
                half dampness = saturate(tone.g * 1.5 - 0.3 + (0.5 - laterite.height) * 0.7 - microHeight * 6.0 + groove * 0.15);

                // Layer weights from the splat, then a height blend: the highest surface in a mix wins,
                // so mud fills the low spots between stones and grass pokes through the litter.
                half wetMud = saturate(splat.g * 1.8 + dampness * splat.r * 0.3);
                half4 weight = half4(splat.r * (1.0 - wetMud * 0.6), wetMud, saturate(1.0 - splat.r - splat.a), splat.a);
                half4 heights = half4(laterite.height, 1.0 - mud.height, litter.height, grassLayer.height);
                half4 blend = (weight + heights * 0.45) * step(0.02, weight);
                blend = max(blend - (max(max(blend.x, blend.y), max(blend.z, blend.w)) - 0.22), 0.0);
                blend /= max(dot(blend, half4(1, 1, 1, 1)), 1e-3);

                half3 albedo = laterite.albedo * blend.x + mud.albedo * blend.y + litter.albedo * blend.z + grassLayer.albedo * blend.w;
                half3 normalTS = normalize(laterite.normalTS * blend.x + mud.normalTS * blend.y + litter.normalTS * blend.z + grassLayer.normalTS * blend.w);
                half height = dot(heights, blend);
                half occlusion = dot(half4(laterite.occlusion, mud.occlusion, litter.occlusion, grassLayer.occlusion), blend);
                half smoothness = dot(half4(laterite.smoothness, mud.smoothness, litter.smoothness, grassLayer.smoothness), blend);

                albedo *= lerp(half3(1, 1, 1), _DampColor.rgb, dampness);
                albedo *= lerp(0.86, 1.08, macro) * lerp(_ValueRange.x, _ValueRange.y, tone.a);
                // Hollows gather shade; tyres press the soil darker and flatten its grain.
                occlusion *= 1.0 - saturate(-microHeight * 4.0) * 0.3;
                albedo *= 1.0 - groove * 0.16;
                normalTS = normalize(lerp(normalTS, half3(0, 0, 1), groove * 0.55));

                // Steep banks (the road cutting, mound flanks) shed their litter: raw laterite streaked by runoff.
                half bank = saturate((0.985 - geometricNormal.y) * 5.0) * saturate((70.0 - length(xz)) / 15.0);
                if (bank > 0.001)
                {
                    float2 contour = normalize(float2(-geometricNormal.z, geometricNormal.x) + 1e-5);
                    half streak = EnvValueNoise(float2(dot(xz, contour) * 2.6, positionWS.y * 0.35)) * 0.7 + EnvValueNoise(xz * 0.9) * 0.3;
                    half exposed = saturate(bank * 1.3 - 0.15 + (streak - 0.5) * 0.9);
                    albedo = lerp(albedo, rawLaterite * (0.62 + 0.45 * streak) * lerp(1.0, 0.8, tone.g), exposed);
                    normalTS = normalize(lerp(normalTS, laterite.normalTS, exposed));
                    height = lerp(height, laterite.height, exposed);
                }

                // Debris near the camera: pebbles on bare earth, twigs and leaves where litter lies.
                half debrisFade = saturate((_Relief.w - cameraDistance) / 8.0);
                float footprint = max(length(fwidth(xz)), 1e-4);
                half3 density = half3(blend.x * (0.22 + 0.16 * (1.0 - compaction)), blend.z * 0.22 + blend.x * 0.03, blend.z * 0.3 + blend.x * 0.05 + blend.w * 0.04)
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

                // Tangent along world x for the top-down projection; meshes with faces looking straight along z
                // (a mound's steep side) fall back to world y so the frame never degenerates.
                float3 tangent = cross(geometricNormal, float3(0, 0, 1));
                tangent = normalize(dot(tangent, tangent) > 1e-4 ? tangent : cross(geometricNormal, float3(0, 1, 0)));
                float3 bitangent = cross(tangent, geometricNormal);
                float3 normalWS = normalize(normalTS.x * tangent + normalTS.y * bitangent + normalTS.z * geometricNormal);

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
                // The canopy around the yard fills most of what a puddle reflects: the sky shows dim and tinted by silt.
                half3 reflection = EnvSkyColor(reflected) * lerp(0.1, 0.2, mainLight.shadowAttenuation) * lerp(half3(1, 1, 1), half3(0.84, 0.5, 0.32), 0.45);
                color.rgb = lerp(color.rgb, reflection, water * (0.02 + 0.12 * fresnel));

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
