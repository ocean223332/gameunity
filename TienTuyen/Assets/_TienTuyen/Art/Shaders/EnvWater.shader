// Water: still pools in bomb craters (dark tannin body, the banks dimming the sky they mirror) and the
// river (_RiverBanks.x > 0): jade karst water as on the backdrop's gorge, deeper towards the middle, the banks
// mirrored dark along both edges, ripples, streaks and foam lines drifting with a current that runs fastest
// mid-channel, foam along the banks and trailing from the falls, and in sun a glitter path. Analytic sky reflection.
Shader "TienTuyen/Environment/Water"
{
    Properties
    {
        _DeepColor ("Deep colour", Color) = (0.06, 0.07, 0.05, 1)
        _RippleScale ("Ripple scale", Float) = 1.6
        _RippleStrength ("Ripple strength", Range(0, 0.3)) = 0.06
        _SkyReflection ("Sky reflection (how open the sky above is)", Range(0, 1)) = 0.5
        _Flow ("Ripple drift, xz (m/s)", Vector) = (0, 0, 0, 0)
        _RippleNormal ("Ripple normal map (Mountain Lake's shallow water)", 2D) = "bump" {}
        _RippleMap ("Ripple normal map: tile (m), strength (0: noise ripples only)", Vector) = (9, 0, 0, 0)
        _RiverBanks ("River: half width of the ribbon (m, 0 = a pool), how far it runs under the banks (m)", Vector) = (0, 0, 0, 0)
        _RiverFalls ("River: arc length (m) of the main fall's and the twin's plunge, mid-channel speed (m/s)", Vector) = (0, 0, 1.1, 0)
        _FlowNormal ("River flow normal (R.A.M 2019 T_water_02_N: ripples stretched along V)", 2D) = "bump" {}
        _FlowFoam ("River foam (R), the flow normal's height (G), noise (B) (R.A.M 2019)", 2D) = "black" {}
        _FlowMap ("River flow textures: tile across (m), tile along (m), normal strength, foam tile (m)", Vector) = (9, 28, 0.15, 8)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "EnvCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                float _RippleScale;
                half _RippleStrength;
                half _SkyReflection;
                float4 _Flow;
                float4 _RippleMap;
                float4 _RiverBanks;
                float4 _RiverFalls;
                float4 _FlowMap;
            CBUFFER_END

            TEXTURE2D(_RippleNormal); SAMPLER(sampler_RippleNormal);
            TEXTURE2D(_FlowNormal); SAMPLER(sampler_FlowNormal);
            TEXTURE2D(_FlowFoam); SAMPLER(sampler_FlowFoam);

            // The river's ribbon: uv0 = (arc length along the river (m), -1 south bank .. 1 north bank), uv1 = its xz direction downstream.
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

            // Sun glitter: every cell of the surface holds one wavelet facet with a random slope (normal spread sigma) that
            // flashes while it mirrors the sun. Cells are about 3 px wide at two blended sizes; cells too small to resolve
            // show their average instead, so the path is one band of sparks, dense where the mean surface nearly mirrors
            // the sun and rare to its sides, with no blob and no uniform speckle. Lanes along the river drift rigidly at
            // their own speed, so cells never shear.
            half Glitter(float arc, float s, float2 along, float3 view, float3 sun, float halfWidth, float time)
            {
                const float sigma = 0.1, tol = 0.05;
                float3 h = normalize(view + sun);
                float2 need = -h.xz / max(h.y, 0.05);   // the slope a facet needs to mirror the sun
                half mean = exp(-dot(need, need) / (2.0 * sigma * sigma)) * (tol / sigma) * (tol / sigma) * 0.5;
                float pixel = max(max(fwidth(arc), fwidth(s)), 1e-4);   // metres per pixel on the water
                float lod = log2(clamp(pixel * 3.0, 0.06, 4.0)), l0 = floor(lod), fr = lod - l0;
                half glint = 0.0;
                [unroll] for (int level = 0; level < 2; level++)
                {
                    float size = exp2(l0 + level), weight = level == 0 ? 1.0 - fr : fr;
                    float lane = floor(s / size), laneS = (lane + 0.5) * size;
                    float drift = RiverSpeed(laneS, halfWidth, halfWidth - abs(laneS)) * time;
                    float2 g = float2((arc - drift) / size, s / size), cell = float2(floor(g.x), lane);
                    half seed = EnvHash21(cell + 5.1);
                    float life = time * 2.2 + seed * 7.0, epoch = floor(life);
                    half h1 = EnvHash21(cell + float2(epoch * 131.0, 11.3)), h2 = EnvHash21(cell + float2(17.9, epoch * 71.0));
                    float2 slope = sqrt(-2.0 * log(max(h1, 1e-4))) * float2(cos(6.2832 * h2), sin(6.2832 * h2)) * sigma;
                    slope = float2(slope.x * along.x - slope.y * along.y, slope.x * along.y + slope.y * along.x);
                    float2 miss = (slope - need) / tol;
                    half twinkle = saturate(frac(life) / 0.1) * (1.0 - saturate((frac(life) - 0.45) / 0.3));
                    float2 spot = frac(g) - (0.25 + 0.5 * float2(EnvHash21(cell + 23.7), EnvHash21(cell + 41.3)));
                    half sharp = exp(-dot(miss, miss)) * twinkle * pow(saturate(1.0 - length(spot) / 0.28), 1.5);
                    half resolved = saturate(size / pixel - 1.0);
                    // Capillary facets far below a pixel always add a faint haze, so the sparks read as one band.
                    glint += weight * (sharp * resolved + mean * (1.0 - resolved) + mean * 0.35 * resolved);
                }
                return glint;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = (input.positionWS.xz - _Flow.xy * _Time.y) * _RippleScale;
                float t = _Time.y * 0.35;
                float2 slope = float2(EnvValueNoise(p + t) - EnvValueNoise(p + float2(0.37, 0) + t),
                                      EnvValueNoise(p * 1.3 - t) - EnvValueNoise(p * 1.3 + float2(0, 0.37) - t));
                float3 normal = normalize(float3(slope.x * _RippleStrength * 10.0, 1.0, slope.y * _RippleStrength * 10.0));
                // The river: two layers of a photographed ripple normal map drifting across each other downstream.
                if (_RippleMap.y > 0.0)
                {
                    float2 uv = (input.positionWS.xz - _Flow.xy * _Time.y) / _RippleMap.x;
                    half3 a = UnpackNormal(SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, uv));
                    half3 b = UnpackNormal(SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, uv * 0.43 + float2(0.31, 0.17) - _Flow.yx * _Time.y * 0.05));
                    half2 tilt = (a.xy + b.xy) * _RippleMap.y;
                    normal = normalize(normal + float3(tilt.x, 0.0, tilt.y));
                }
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                if (_RiverBanks.x > 0.0)
                {
                    float arc = input.river.x, halfWidth = _RiverBanks.x - _RiverBanks.y;
                    float s = input.river.y * _RiverBanks.x;   // metres from the centreline, north positive
                    // Metres from the nearer bank (the ribbon runs _RiverBanks.y under each bank).
                    half bank = _RiverBanks.x * (1.0 - abs(input.river.y)) - _RiverBanks.y;
                    // The paddies' ripple slopes only close up: farther off, under the river's bright sky, they mottle it.
                    normal = normalize(lerp(float3(0.0, 1.0, 0.0), normal, 0.35 * (1.0 - saturate(distance(input.positionWS, GetCameraPositionWS()) / 30.0))));
                    float2 along = normalize(input.along);
                    float time = fmod(_Time.y, 600.0);
                    float speed = RiverSpeed(s, halfWidth, bank);
                    // Sun or not: the presets' haze stands in for the cloud over the sun (Sunny 0.3, Overcast 0.45, Misty 0.6).
                    half sunny = 1.0 - smoothstep(0.33, 0.42, _EnvHaze.x);
                    // The current: long thin streaks and foam dashes (they read as moving even in a still), R.A.M 2019's
                    // flow-stretched ripples close up, its ripple height and its foam, all carried along the river at the
                    // local speed in two phases crossfaded so nothing stretches; the textures' V runs along the river.
                    half streak = 0.0, dashes = 0.0, rafts = 0.0;
                    half3 flowNormal = 0.0;
                    half3 flow = 0.0;   // r foam, g ripple height, b large soft patches
                    // Each octave fades to its mean once it gets too fine for the pixel, or far off it aliases into a mottle.
                    float footprint = max(fwidth(s), fwidth(arc));
                    half aaWide = 1.0 - smoothstep(0.08, 0.25, footprint), aaFine = 1.0 - smoothstep(0.04, 0.12, footprint);
                    [unroll] for (int phase = 0; phase < 2; phase++)
                    {
                        float k = frac(time / 4.0 + phase * 0.5), w = 1.0 - abs(2.0 * k - 1.0);
                        float a2 = arc - speed * k * 4.0;
                        streak += w * (0.5 + (EnvValueNoise(float2(a2 / 7.0 + phase * 6.5, s / 0.55 + 3.1)) - 0.5) * 0.6 * aaWide + (EnvValueNoise(float2(a2 / 2.5 - phase * 3.5, s / 0.25)) - 0.5) * 0.4 * aaFine);
                        dashes += w * EnvValueNoise(float2(a2 / 3.0 + 4.4 + phase * 2.5, s / 0.25 - 1.7));
                        rafts += w * EnvValueNoise(float2(a2 / 14.0 + 4.4 + phase * 2.5, s / 3.5 - 1.7));
                        float2 rippleUV = float2(s / _FlowMap.x + phase * 0.37, a2 / _FlowMap.y + phase * 0.61);
                        flowNormal += w * UnpackNormal(SAMPLE_TEXTURE2D(_FlowNormal, sampler_FlowNormal, rippleUV));
                        flow += w * half3(SAMPLE_TEXTURE2D(_FlowFoam, sampler_FlowFoam, float2(s / _FlowMap.w - phase * 0.29, a2 / (_FlowMap.w * 2.5) + phase * 0.43)).r,
                                          SAMPLE_TEXTURE2D(_FlowFoam, sampler_FlowFoam, rippleUV).g,
                                          SAMPLE_TEXTURE2D(_FlowFoam, sampler_FlowFoam, float2(s / 40.0 + phase * 0.13, a2 / 120.0 + phase * 0.71)).b);
                    }
                    // Foam rafts: a few large soft patches stretched along the current; most of the surface stays clean.
                    half raft = smoothstep(0.62, 0.8, flow.b) * smoothstep(0.5, 0.8, rafts);
                    float near = 1.0 - saturate(distance(input.positionWS, GetCameraPositionWS()) / 45.0);
                    float2 tilt = flowNormal.xy * _FlowMap.z * saturate(near * 1.1);   // x across (north positive), y downstream
                    normal = normalize(normal + float3(-along.y, 0.0, along.x) * (streak - 0.5) * 0.18
                                       + float3(-along.y * tilt.x + along.x * tilt.y, 0.0, along.x * tilt.x + along.y * tilt.y));
                    // How deep the water is (m): from the depth prepass, which holds the bed and the rocks but not the water
                    // (no depth pass of its own); where it shows the water itself, from the ribbon's distance to its bank.
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS)), _ZBufferParams);
                    float thick = sceneEye - LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                    half depth = thick > 0.03 ? thick * max(view.y, 0.05) : max(bank, 0.0) * 0.35;
                    float3 r = reflect(-view, normal);
                    half3 sky = EnvSkyColor(normalize(float3(r.x, max(r.y, 0.02), r.z))) * _SkyReflection * half3(0.62, 0.82, 0.76);   // seen through green water
                    sky *= lerp(1.35, 1.0, sunny);   // in grey light the sky's sheen carries the river, so it never reads as ink
                    // Fresnel off a calmer normal: steep ripples seen edge-on would turn the whole river to sky.
                    float3 calm = normalize(lerp(float3(0, 1, 0), normal, 0.3));
                    half edgeFresnel = 0.12 + 0.5 * pow(1.0 - saturate(dot(view, calm)), 5.0);
                    half3 ambient = SampleSH(float3(0, 1, 0));
                    half3 jade = ambient * _DeepColor.rgb * lerp(1.25, 0.7, smoothstep(1.0, 9.0, bank)) + _DeepColor.rgb * mainLight.color * 0.08;
                    // Shallows: lighter, siltier jade where the bed comes up (R.A.M's shallow colour by depth).
                    jade = lerp(jade, jade * half3(1.5, 1.45, 1.2) + half3(0.02, 0.02, 0.01) * ambient, (1.0 - smoothstep(0.3, 2.5, depth)) * 0.6);
                    // Ripple crests catch the light through them near the camera (R.A.M's wave translucency).
                    jade += _DeepColor.rgb * ambient * pow(saturate(flow.g * 1.15), 3.4) * 0.9 * near;
                    half3 banks = jade * 0.35;   // the wooded, earthen banks mirrored dark along the edges
                    half3 mirror = lerp(banks, sky, smoothstep(1.5, 7.0, bank));
                    half3 river = lerp(jade, mirror, edgeFresnel) * (0.93 + 0.14 * streak);
                    // Riffles: lighter, choppier patches in the shallows near the banks.
                    half riffle = saturate((EnvValueNoise(float2(arc / 18.0 + 2.0, s / 6.0)) - 0.55) * 4.0) * (1.0 - smoothstep(2.0, 6.0, bank));
                    river += riffle * half3(0.05, 0.08, 0.07) * (0.6 + 0.8 * streak) * ambient;
                    // Foam: where the water meets the bank and the rocks (from the depth), lines riding the current, and a
                    // trail drifting downstream from each plunge along the south bank, spreading and thinning.
                    half trail = 0.0;
                    [unroll] for (int fall = 0; fall < 2; fall++)
                    {
                        float down = arc - (fall == 0 ? _RiverFalls.x : _RiverFalls.y), past = max(down, 0.0);
                        float centre = -(halfWidth - 3.0) + 0.05 * past, spread = (fall == 0 ? 4.0 : 2.5) + 0.06 * past;
                        float off = (s - centre) / spread;
                        trail = max(trail, exp(-past / (fall == 0 ? 30.0 : 18.0)) * step(-8.0, down) * exp(-off * off) * (fall == 0 ? 1.0 : 0.6));
                    }
                    // At the shore a thin, broken fringe of R.A.M's bubbly foam, never a hard white band. Only near the
                    // bank: mid-river the depth read is too noisy to trust a few centimetres.
                    half edge = (1.0 - smoothstep(0.05, 0.35, depth)) * (1.0 - smoothstep(1.0, 3.0, bank)) * smoothstep(0.45, 0.8, flow.r);
                    half lines = max(raft * 0.4, saturate((dashes - 0.76) * 6.0) * 0.4);
                    half foamy = saturate(max(edge, max(lines, trail * smoothstep(0.5, 0.85, flow.r))));
                    river = lerp(river, half3(0.78, 0.8, 0.76) * (ambient + mainLight.color * (0.15 + 0.25 * sunny)), foamy * 0.85);
                    // No sun disc: off the rippled surface it reads as a white blob. Only the glitter path, and only in sun.
                    half glint = Glitter(arc, s, along, view, mainLight.direction, halfWidth, time) * (1.0 - foamy);
                    river += mainLight.color * glint * 4.0 * sunny * mainLight.shadowAttenuation;
                    return half4(EnvApplyHaze(river, input.positionWS), 1);
                }
                half fresnel = 0.04 + 0.96 * pow(1.0 - saturate(dot(normal, view)), 5.0);
                // Crater banks fill most of what a pool mirrors, so its sky shows dimmed; the river lies open to the sky.
                half3 reflection = EnvSkyColor(reflect(-view, normal)) * _SkyReflection;
                half3 body = _DeepColor.rgb * (SampleSH(float3(0, 1, 0)) + mainLight.color * 0.15);
                half3 color = lerp(body, reflection, saturate(fresnel * 0.8 + 0.06));
                float3 halfVector = normalize(mainLight.direction + view);
                color += mainLight.color * pow(saturate(dot(normal, halfVector)), 380.0) * 6.0 * mainLight.shadowAttenuation;
                color = EnvApplyHaze(color, input.positionWS);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
