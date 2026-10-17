// The river: R.A.M 2019's river water (NatureManufacture, "Water River Offset Vertex Color Flow", Big Green) ported from
// its Shader Graph to one forward pass, on our ribbon (EnvironmentGround.BuildRiver). Its look: three layers of a
// choppy ripple normal (a slow flow layer and two finer ones), lit as a glossy specular surface (smoothness 0.92), so
// the sun breaks into a path of sharp highlights on the ripples, and the sky probe mirrors in it; underneath, the bed
// seen through it (refracted from the opaque texture), tinted and then hidden by the water's colour as it deepens;
// ripple crests light up from within. Ours on top: everything drifts downstream at the current's speed (fastest
// mid-channel), a broken fringe of foam at the shore and a trail of it below each fall, and the scenery haze.
// Dropped from the original: its cascades (the river has none), vertex displacement, Unity fog and the shadow,
// depth, meta and deferred passes.
Shader "TienTuyen/Environment/River"
{
    Properties
    {
        _RiverMap ("Ripples (R.A.M T_RAM_River_Normalmap xy in RG), crest height (B), foam (A)", 2D) = "black" {}
        _DeepColor ("Deep colour", Color) = (0.027, 0.094, 0.034, 1)
        _ShallowColor ("Shallow colour", Color) = (0.259, 0.282, 0.263, 1)
        _RiverBanks ("Half width of the ribbon (m), how far it runs under the banks (m)", Vector) = (17, 3, 0, 0)
        _RiverFalls ("Arc length (m) of the main fall's and the twin's plunge, mid-channel speed (m/s)", Vector) = (0, 0, 1.1, 0)
        _RippleTiles ("Ripple tiles (m): slow, micro, macro; flow refresh (1/s)", Vector) = (42.5, 3, 10, 0.15)
        _RippleScales ("Ripple normal scales: slow, micro, macro; far scale", Vector) = (0.3, 0.12, 0.12, 0.5)
        _Specular ("Specular: close, far, threshold; smoothness", Vector) = (0.19, 0.255, 0.35, 0.92)
        _Depths ("Shallow falloff x, power; opacity x, power (per metre of depth along the view)", Vector) = (1.65, 1.73, 0.66, 1.39)
        _Edges ("Edge alpha x, power; clean edge x, power", Vector) = (5.19, 1.46, 2.31, 0.2)
        _Crests ("Crest translucency: hardness, power, strength, fade distance (m)", Vector) = (1.41, 2.32, 0.1, 30)
        _Distortion ("Refraction", Range(0, 1)) = 0.163
        _ClearColor ("What the clear water passes of the bed (jade tint)", Color) = (0.72, 0.88, 0.8, 1)
    }

    SubShader
    {
        // Before the falls' sheets and the mist, which are drawn over it (and depth-tested against it).
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            // No box projection: the one probe sits in the clearing 200 m off, and projected onto its box the river mirrored
            // the clearing's ground as a brown sheet.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #define _SPECULAR_SETUP 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "EnvCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RiverMap_ST;
                half4 _DeepColor;
                half4 _ShallowColor;
                float4 _RiverBanks;
                float4 _RiverFalls;
                float4 _RippleTiles;
                half4 _RippleScales;
                half4 _Specular;
                half4 _Depths;
                half4 _Edges;
                half4 _Crests;
                half _Distortion;
                half4 _ClearColor;
            CBUFFER_END

            TEXTURE2D(_RiverMap); SAMPLER(sampler_RiverMap);

            // uv0 = (arc length along the river (m), -1 south bank .. 1 north bank), uv1 = its xz direction downstream.
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 along : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 river : TEXCOORD1; float2 along : TEXCOORD2; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.river = input.uv;
                output.along = input.along;
                return output;
            }

            // How fast the current runs (m/s) at s metres from the centreline: fastest mid-channel, slow at the banks and
            // turning back a little right under them, so the edges eddy.
            float RiverSpeed(float s, float halfWidth, float bank)
            {
                return _RiverFalls.z * sqrt(saturate(1.0 - (s / halfWidth) * (s / halfWidth))) + 0.08 - 0.25 * (1.0 - smoothstep(0.0, 2.5, bank));
            }

            // One ripple layer: the normal's slope scaled as Shader Graph's Normal Strength does, its crest height and foam.
            half3 Ripple(float2 uv, half scale, out half height, out half foam)
            {
                half4 texel = SAMPLE_TEXTURE2D(_RiverMap, sampler_RiverMap, uv);
                half2 xy = texel.rg * 2.0 - 1.0;
                half z = sqrt(saturate(1.0 - dot(xy, xy)));
                height = texel.b;
                foam = texel.a;
                return half3(xy * scale, lerp(1.0, z, saturate(scale)));
            }

            // Whiteout blend, as Shader Graph's Normal Blend.
            half3 BlendNormals(half3 a, half3 b) { return normalize(half3(a.xy + b.xy, a.z * b.z)); }

            // Eye depth of the opaque scene behind the water at a screen uv, less the water's own.
            float BehindWater(float2 uv, float waterEye)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams) - waterEye;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 positionWS = input.positionWS;
                float arc = input.river.x, halfWidth = _RiverBanks.x - _RiverBanks.y;
                float s = input.river.y * _RiverBanks.x;   // metres from the centreline, north positive
                half bank = _RiverBanks.x * (1.0 - abs(input.river.y)) - _RiverBanks.y;   // metres from the nearer bank
                float2 along = normalize(input.along), side = float2(-along.y, along.x);
                float time = fmod(_Time.y, 600.0);
                float speed = RiverSpeed(s, halfWidth, bank);
                float cameraDistance = distance(positionWS, GetCameraPositionWS());

                // R.A.M's flow: each layer read in two phases that restart in turn and crossfade, so it drifts without
                // stretching. Its slow layer drifts with the current; the finer two (R.A.M: wind ripples nearly fixed in
                // place) ride it too, so the current reads in a still and close up.
                float period = 1.0 / _RippleTiles.w;
                half3 slow = 0.0, fine = 0.0;
                half crest = 0.0, foam = 0.0;
                [unroll] for (int phase = 0; phase < 2; phase++)
                {
                    float k = frac(time / period + 1.0 - phase * 0.5);
                    half w = phase == 0 ? 1.0 - abs(2.0 * k - 1.0) : abs(2.0 * frac(time / period + 1.0) - 1.0);
                    float2 p = float2(s, arc - speed * k * period);
                    half hSlow, hMacro, hMicro, fSlow, fMacro, fMicro;
                    half3 slowN = Ripple(p / _RippleTiles.x, _RippleScales.x, hSlow, fSlow);
                    // R.A.M nudges the fine layers by the slow one's slope.
                    float2 nudge = slowN.xy * 0.05;
                    half3 micro = Ripple(p / _RippleTiles.y + nudge, _RippleScales.y, hMicro, fMicro);
                    half3 macro = Ripple(p / _RippleTiles.z + nudge, _RippleScales.z, hMacro, fMacro);
                    slow += w * slowN;
                    fine += w * BlendNormals(micro, macro);
                    crest += w * (hSlow + hMacro);
                    foam += w * fMicro;
                }
                half3 normalTS = BlendNormals(slow, fine);
                // Past 20 m the ripples flatten, so the far river does not sparkle into noise.
                normalTS = normalize(lerp(normalTS, normalTS * half3(_RippleScales.w, _RippleScales.w, 1.0), saturate(pow(cameraDistance / 20.0, 10.0))));
                // x across (north), y downstream, z up.
                float3 normalWS = normalize(float3(side.x * normalTS.x + along.x * normalTS.y, normalTS.z, side.y * normalTS.x + along.y * normalTS.y));

                // The bed through the water: refracted by the ripples, less with distance; where the offset lands on
                // something in front of the water, straight through instead.
                float waterEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float2 bentUV = screenUV + normalTS.xy * float2(1.0, _ScreenParams.x / _ScreenParams.y) * (_Distortion * 0.3) / waterEye;
                if (BehindWater(bentUV, waterEye) < 0.0) bentUV = screenUV;
                float depth = BehindWater(screenUV, waterEye), bentDepth = BehindWater(bentUV, waterEye);
                half3 below = SampleSceneColor(bentUV);

                // R.A.M's water colour: shallow near the bed, deep past it; crests lit through towards the shallow
                // colour close up; the bed tinted by it, then hidden as the water deepens; the very edge clear.
                half shallow = saturate(pow(abs(bentDepth * _Depths.x), -_Depths.y));
                half3 water = lerp(_DeepColor.rgb, _ShallowColor.rgb, shallow);
                half crestLight = saturate(lerp(pow(abs(crest * _Crests.x), _Crests.y) * _Crests.z, 0.0, cameraDistance / _Crests.w));
                water = lerp(water, _ShallowColor.rgb, crestLight);
                // Unlike R.A.M, the bed seen through is not lit a second time (in shade it turned into a flat brown sheet):
                // the water's body is lit by its share, the bed shows through the rest, tinted jade, clear at the edge.
                half body = pow(saturate(depth * _Depths.z), _Depths.w);
                half3 passed = below * lerp(half3(1, 1, 1), _ClearColor.rgb * lerp(half3(1, 1, 1), water / max(_ShallowColor.rgb, 0.01), 0.3), pow(saturate(bentDepth * _Edges.z), _Edges.w)) * (1.0 - body);
                half3 albedo = water * body;
                half alpha = pow(saturate(depth * _Edges.x), _Edges.y);

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = screenUV;
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.specular = lerp(_Specular.y, _Specular.x, pow(shallow, _Specular.z)).xxx;
                surface.smoothness = _Specular.w;
                surface.normalTS = normalTS;
                surface.occlusion = 1.0;
                surface.alpha = alpha;
                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb += passed * (1.0 - surface.specular.x);

                // Foam: a thin, broken fringe where the water meets the bank (from the depth), and a trail drifting
                // downstream from each plunge along the south bank, spreading and thinning.
                float vertical = depth * max(inputData.viewDirectionWS.y, 0.05);
                half edge = (1.0 - smoothstep(0.02, 0.14, vertical)) * (1.0 - smoothstep(1.0, 3.0, bank)) * smoothstep(0.55, 0.85, foam) * 0.7;
                half trail = 0.0;
                [unroll] for (int fall = 0; fall < 2; fall++)
                {
                    float down = arc - (fall == 0 ? _RiverFalls.x : _RiverFalls.y), past = max(down, 0.0);
                    float centre = -(halfWidth - 3.0) + 0.05 * past, spread = (fall == 0 ? 4.0 : 2.5) + 0.06 * past;
                    float off = (s - centre) / spread;
                    trail = max(trail, exp(-past / (fall == 0 ? 30.0 : 18.0)) * step(-8.0, down) * exp(-off * off) * (fall == 0 ? 1.0 : 0.6));
                }
                half foamy = saturate(max(edge, trail * smoothstep(0.5, 0.85, foam)));
                Light mainLight = GetMainLight(inputData.shadowCoord);
                half3 white = half3(0.78, 0.8, 0.76) * (inputData.bakedGI + mainLight.color * mainLight.shadowAttenuation * 0.35);
                color.rgb = lerp(color.rgb, white, foamy * 0.85);
                color.a = max(color.a, foamy);
                return half4(EnvApplyHaze(color.rgb, positionWS), color.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
