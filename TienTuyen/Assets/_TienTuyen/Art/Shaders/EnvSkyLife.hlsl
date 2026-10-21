#ifndef TIEN_TUYEN_ENV_SKY_LIFE_INCLUDED
#define TIEN_TUYEN_ENV_SKY_LIFE_INCLUDED

// Life in the far air (EnvironmentSkyLife): shafts of sun falling through the cloud onto the valleys, and birds
// circling the towers and gliding over the river. Both are shaped in the vertex stage from the sun and the clock, so
// they follow the preset without a rebuild. Needs EnvCommon.hlsl.

// A shaft: a strip from its anchor on the ground up towards the sun to the cloud, turned to face the camera and
// widening as it climbs. corner: x across (-1..1), y along (0 ground .. 1 cloud); shape: half width at the ground (m),
// height of the cloud above the anchor (m), seed.
float3 ShaftPosition(float3 anchor, float2 corner, float3 shape, float3 camera)
{
    float3 axis = normalize(float3(_EnvSunDir.x, max(_EnvSunDir.y, 0.2), _EnvSunDir.z));
    float3 centre = anchor + axis * (shape.y / axis.y * corner.y);
    float3 side = normalize(cross(axis, centre - camera));
    return centre + side * (shape.x * (1.0 + 0.8 * corner.y) * corner.x);
}

// How much light the shaft scatters towards the eye: a loose bundle of rays (drifting dust splits it across), brightest
// under the cloud it falls from and fading out before the ground, strongest looking into the sun. Nothing within
// 500 m: the near air's light is the volumetric pass's.
half ShaftAmount(float2 corner, float seed, float3 viewDir, float distance, float time)
{
    half across = 1.0 - corner.x * corner.x;
    across *= sqrt(across);
    half rays = smoothstep(0.25, 0.8, EnvValueNoise(float2(corner.x * 4.5 + seed * 7.0 + time * 0.02, seed * 3.0)));
    half along = smoothstep(0.0, 0.45, corner.y) * (1.0 - smoothstep(0.8, 1.0, corner.y)) * (0.35 + 0.65 * corner.y);
    half forward = 0.45 + 1.4 * pow(saturate(dot(viewDir, _EnvSunDir.xyz)), 6.0);
    return across * (0.25 + 0.75 * rays) * along * forward * smoothstep(500.0, 900.0, distance);
}

// How much of a shaft shows in front of what lies behind it on the same pixel (virtual distance, m): over the sky and
// the far hazed ranges, never across the face of a tower or a range within reach of the eye.
half ShaftBackdrop(float behind)
{
    return smoothstep(9000.0, 14000.0, behind);
}

// A bird: a body and two wings (local x: -1 left tip .. 1 right tip, z: forward), on a circle round centre.
// motion: radius (m), angular speed (rad/s, the sign the way round), phase, half span (m).
// Wings beat in bursts, then the bird glides on them held a little up; it banks into the turn.
float3 BirdPosition(float3 centre, float3 local, float4 motion, float time, out half beat)
{
    float a = motion.z + time * motion.y;
    float3 position = centre + float3(sin(a), 0.0, cos(a)) * motion.x;
    position.y += sin(a * 2.0 + motion.z * 3.1) * motion.x * 0.06;
    float turn = motion.y >= 0.0 ? 1.0 : -1.0;
    float3 forward = float3(cos(a), 0.0, -sin(a)) * turn;
    float3 right = float3(forward.z, 0.0, -forward.x);
    float3 up = normalize(float3(0.0, 1.0, 0.0) + right * 0.35 * turn);
    half burst = smoothstep(0.2, 0.7, sin(time * 0.37 + motion.z * 5.3));
    beat = burst;
    float flap = sin(time * 8.0 + motion.z * 13.0) * 0.9 * burst + 0.22 * (1.0 - burst);
    return position + (right * local.x + forward * local.z) * motion.w + up * (abs(local.x) * flap * motion.w * 0.7 + local.y * motion.w);
}

#endif
