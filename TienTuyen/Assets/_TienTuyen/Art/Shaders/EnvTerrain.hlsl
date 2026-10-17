#ifndef TIEN_TUYEN_ENV_TERRAIN_INCLUDED
#define TIEN_TUYEN_ENV_TERRAIN_INCLUDED

// The far look of the upland ground, shared by EnvGround (the terrain mesh past a few tens of metres) and
// EnvMountains (the backdrop's near rings that continue it), so the two meet at the terrain's edge without a seam.
// Needs EnvCommon.hlsl.

// Broad meadow fields in world XZ (m): dry, yellowed grass; pine woods (needle floor under the clumps, darker);
// bare soil where the sward has worn through.
void EnvMeadowFields(float2 xz, out half dry, out half woods, out half bare)
{
    dry = smoothstep(0.45, 0.68, EnvFbm(xz / 46.0 + 13.7));
    // Woods: clumps tens of metres across, gathered into larger stands.
    half stands = EnvFbm(xz / 210.0 - 7.1);
    woods = smoothstep(0.62, 0.74, EnvFbm(xz / 34.0 + 3.3) * 0.55 + stands * 0.6);
    bare = smoothstep(0.7, 0.82, EnvFbm(xz / 19.0 - 21.4)) * (1.0 - woods);
}

// 0..1 bare rock: steep slopes, broken by a noise so outcrops stand out in ragged bands rather than follow the contour.
half EnvRockCover(float2 xz, float normalY, float2 slopeRange)
{
    half breakup = EnvFbm(xz / 9.0 + 5.1) - 0.5;
    return smoothstep(slopeRange.x, slopeRange.y, 1.0 - normalY + breakup * 0.34);
}

// 0 lush .. 1 dry sward: the dry fields, drier on sun-facing (south) slopes and up on the crests, lush down in the
// hollows where the water gathers. height is the world height (m); the station sits at 0.
half EnvMeadowDryness(half dry, float3 normal, float height)
{
    half south = saturate(-normal.z * 2.5);
    half crest = saturate((height - 12.0) / 30.0);
    half hollow = saturate((3.0 - height) / 6.0);
    return saturate(dry * 0.5 + south * 0.25 + crest * 0.2 - hollow * 0.45);
}

// The sward's colour shift from lush to dry, applied to the grass layer's albedo.
half3 EnvMeadowTint(half dryness) { return lerp(half3(0.9, 1.04, 0.85), half3(1.2, 1.02, 0.74), dryness); }

// Hill-scale relief the 4 m grid cannot carry: soil creep, sheep-track terracing and small gullies, as a normal
// perturbation. strength 0 leaves the normal unchanged.
float3 EnvHillNormal(float2 xz, float3 normal, half strength)
{
    if (strength <= 0.001) return normal;
    const float e = 0.6;
    float2 p = xz / 7.0;
    float h0 = EnvFbm(p), hx = EnvFbm(p + float2(e / 7.0, 0)), hz = EnvFbm(p + float2(0, e / 7.0));
    float2 slope = float2(hx - h0, hz - h0) / e * 2.2;
    // Contour terracing across the fall line.
    float2 downhill = normal.xz / max(length(normal.xz), 1e-4);
    float along = dot(xz, downhill) * 0.9 + EnvValueNoise(xz / 5.0) * 2.0;
    slope += downhill * cos(along * 6.2832) * 0.08 * saturate(length(normal.xz) * 4.0);
    return normalize(normal - float3(slope.x, 0, slope.y) * strength);
}

// Far ground colour from the layers' average colours at a world position: meadow grass, dry or lush, darker under the woods,
// soil patches and rock on the steeps.
half3 EnvFarGround(float3 position, float3 normal, half3 grass, half3 needles, half3 soil, half3 rock, float2 rockSlope)
{
    float2 xz = position.xz;
    half dry, woods, bare;
    EnvMeadowFields(xz, dry, woods, bare);
    half3 sward = grass * EnvMeadowTint(EnvMeadowDryness(dry, normal, position.y));
    sward *= 0.85 + 0.3 * EnvValueNoise(xz / 6.0);
    half3 albedo = lerp(sward, soil, bare * 0.7);
    // Under the woods: needle floor in the shade of the crowns.
    albedo = lerp(albedo, needles * 0.55 + grass * 0.12, woods * 0.85);
    return lerp(albedo, rock, EnvRockCover(xz, normal.y, rockSlope));
}

// Gust waves on the meadow, 0..1: fronts tens of metres across sliding downwind (_EnvWind.xy) at EnvGustSpeed m/s; the
// finer octave drifts a little slower, so a front changes shape as it travels. EnvGrass leans and brightens its blades
// by it and EnvGround brightens the sward under them, so a wave reads over the whole meadow, not only on the blades.
static const float EnvGustSpeed = 4.5;

half EnvGrassGust(float2 xz, float time)
{
    float2 downwind = normalize(_EnvWind.xy + float2(1e-5, 0));
    float along = dot(xz, downwind), across = dot(xz, float2(-downwind.y, downwind.x));
    float n = EnvValueNoise(float2((along - time * EnvGustSpeed) / 11.0, across / 22.0)) * 0.7
        + EnvValueNoise(float2((along - time * EnvGustSpeed * 0.8) / 5.0, across / 8.0) + 5.3) * 0.3;
    return smoothstep(0.45, 0.75, n);
}

#endif
