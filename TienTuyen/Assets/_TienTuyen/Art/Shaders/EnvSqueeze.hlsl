#ifndef TIEN_TUYEN_ENV_SQUEEZE_INCLUDED
#define TIEN_TUYEN_ENV_SQUEEZE_INCLUDED

// Where the backdrop (EnvironmentBackdrop, EnvironmentSkyLife) draws a virtual point: in the same direction from the
// camera, its distance squeezed so the ranges kilometres out fit inside the gameplay camera's far plane.
// squeeze: x earliest start (m), y log scale (m), z where 30 km lands (m), w half size of the terrain mesh (m).
// Along each view ray the squeeze starts where that ray leaves the terrain mesh (no earlier than x, no later than
// x + 100 m), so the backdrop is never drawn in front of terrain on the same ray: a tower's foot cannot show over the
// far ground. Past the start, distances up to 30 km map logarithmically onto the rest of the way to z.
float EnvSqueezeStart(float3 dir, float3 camera, float4 squeeze)
{
    float2 away = abs(dir.xz);
    float2 exits = (squeeze.w - camera.xz * sign(dir.xz)) / max(away, 1e-4);
    return clamp(min(exits.x, exits.y), squeeze.x, squeeze.x + 100.0);
}

float3 EnvSqueeze(float3 virtualWS, float3 camera, float4 squeeze)
{
    float3 view = virtualWS - camera;
    float distance = max(length(view), 1e-3);
    float3 dir = view / distance;
    float start = EnvSqueezeStart(dir, camera, squeeze);
    float squeezed = distance <= start ? distance
        : start + (squeeze.z - start) * log(1.0 + (distance - start) / squeeze.y) / log(1.0 + (30000.0 - start) / squeeze.y);
    return camera + dir * squeezed;
}

// The other way: the virtual distance of what is drawn squeezed distance along dir (past 30 km for the sky).
float EnvUnsqueeze(float squeezed, float3 dir, float3 camera, float4 squeeze)
{
    float start = EnvSqueezeStart(dir, camera, squeeze);
    return squeezed <= start ? squeezed
        : start + squeeze.y * (exp((squeezed - start) / (squeeze.z - start) * log(1.0 + (30000.0 - start) / squeeze.y)) - 1.0);
}

#endif
