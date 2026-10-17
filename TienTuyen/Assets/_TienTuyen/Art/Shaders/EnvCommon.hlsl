#ifndef TIEN_TUYEN_ENV_COMMON_INCLUDED
#define TIEN_TUYEN_ENV_COMMON_INCLUDED

// Globals published once by CombatEnvironment so sky, water and haze agree.
float4 _EnvWind;        // xy: direction, z: strength (m), w: speed
float4 _EnvSunDir;      // xyz: direction towards the sun
half4 _EnvSkyZenith;
half4 _EnvSkyHorizon;
half4 _EnvSkyGlow;      // warm scatter around the sun
float4 _EnvHaze;        // x: forest haze amount, y: its distance (m), z: atmosphere distance (m), w: forest haze height (m)

half3 EnvSkyColor(float3 direction)
{
    float up = saturate(direction.y);
    half3 sky = lerp(_EnvSkyHorizon.rgb, _EnvSkyZenith.rgb, pow(up, 0.5));
    float sun = saturate(dot(direction, _EnvSunDir.xyz));
    // Broad golden scatter low around the sun, tighter halo near it.
    float lowness = 1.0 - up * 0.6;
    sky += _EnvSkyGlow.rgb * (pow(sun, 6.0) * 0.55 * lowness + pow(sun, 48.0) * 0.9);
    // Below the horizon the view meets valley haze, never a black void.
    sky = lerp(sky, _EnvSkyHorizon.rgb * 0.92, saturate(-direction.y * 5.0));
    return sky;
}

// Environment haze, used by scenery shaders instead of the global fog so the soldiers keep their contrast.
// Two parts: humid forest air that hugs the ground, clear across the yard and greying the forest wall from
// twenty metres on, and the atmosphere that swallows the far ranges. The colour is the sky just above the horizon in that direction.
half EnvHazeAmount(float distance, float height)
{
    // max() guards keep the scene view sane before CombatEnvironment publishes the globals.
    float depth = distance / max(_EnvHaze.y, 1.0);
    half forest = _EnvHaze.x * (1.0 - exp(-depth * depth)) * exp(-max(height, 0.0) / max(_EnvHaze.w, 1.0));
    half atmosphere = exp(-pow(distance / max(_EnvHaze.z, 1.0), 1.6));
    return 1.0 - (1.0 - forest) * atmosphere;
}

half3 EnvHazeColor(float3 direction)
{
    return EnvSkyColor(normalize(float3(direction.x, max(direction.y, 0.02), direction.z)));
}

// positionWS is where the surface really is (for the mountains: before their distance compression).
half3 EnvApplyHaze(half3 color, float3 positionWS)
{
    float3 view = positionWS - GetCameraPositionWS();
    float distance = length(view);
    return lerp(color, EnvHazeColor(view / max(distance, 1e-3)), EnvHazeAmount(distance, positionWS.y));
}

// Wind sway for batched world-space foliage. Weight grows with height above the
// plant's base (baked into vertex colour), phase decorrelates neighbours.
// The ...At form takes the time, so motion vectors can rebuild last frame's pose.
float3 EnvWindOffsetAt(float3 positionWS, float weight, float phase, float time)
{
    float t = time * _EnvWind.w;
    float p = dot(positionWS.xz, float2(0.13, 0.09)) + phase * 6.2831;
    float gust = sin(t * 0.31 + positionWS.x * 0.021 + positionWS.z * 0.013) * 0.5 + 0.5;
    float sway = sin(t + p) * 0.55 + sin(t * 2.27 + p * 1.7) * 0.22;
    float3 offset = float3(_EnvWind.x, 0.0, _EnvWind.y) * (sway + gust * 0.75) * _EnvWind.z * weight;
    // Flutter: small fast vertical motion on leaf tips.
    offset.y = sin(t * 5.3 + p * 3.1) * 0.035 * weight * _EnvWind.z * 4.0;
    return offset;
}

float3 EnvWindOffset(float3 positionWS, float weight, float phase)
{
    return EnvWindOffsetAt(positionWS, weight, phase, _Time.y);
}

float EnvHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float EnvValueNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = EnvHash21(i), b = EnvHash21(i + float2(1, 0)), c = EnvHash21(i + float2(0, 1)), d = EnvHash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float EnvFbm(float2 p)
{
    float sum = 0.0, amplitude = 0.5;
    for (int i = 0; i < 4; i++)
    {
        sum += EnvValueNoise(p) * amplitude;
        p = p * 2.03 + 17.1;
        amplitude *= 0.5;
    }
    return sum / 0.9375;
}

#endif
