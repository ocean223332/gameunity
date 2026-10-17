#ifndef TIEN_TUYEN_ENV_KARST_CLOUD_INCLUDED
#define TIEN_TUYEN_ENV_KARST_CLOUD_INCLUDED

// Valley cloud, shared by the far landscape (EnvKarst.hlsl) and its cloud skirt (EnvMountainCloud). Needs
// EnvCommon.hlsl and the material properties _CloudSea, _Aerial, _AerialTint (see EnvKarst.hlsl). Drawn in view
// direction space, never from the surface's position, so the band on a slope and on the skirt behind it match.

// Noise on the azimuth that closes on itself: the lattice wraps every `period` cells along x, so the cloud has no seam
// where the bearing wraps (due south, atan2's +-180).
float KarstWrapNoise(float2 p, float period)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float i0 = i.x - period * floor(i.x / period), i1 = i0 + 1.0 >= period ? 0.0 : i0 + 1.0;
    float a = EnvHash21(float2(i0, i.y)), b = EnvHash21(float2(i1, i.y)), c = EnvHash21(float2(i0, i.y + 1.0)), d = EnvHash21(float2(i1, i.y + 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float KarstWrapFbm(float2 p, float period)
{
    float sum = 0.0, amplitude = 0.5;
    [unroll] for (int i = 0; i < 4; i++)
    {
        sum += KarstWrapNoise(p, period) * amplitude;
        p = p * 2.0 + float2(0.0, 17.1);
        period *= 2.0;
        amplitude *= 0.5;
    }
    return sum / 0.9375;
}

// Valley cloud: a thick bank at the visible base of every layer, just above the ridge in front of it, so each range
// stands out of white over the next and the towers rise out of it. rise (baked): degrees of elevation above the
// level the ridges in front reach. Nearly opaque under a billowed top that drifts slowly; 0..1 cover.
half KarstValleyCloud(float3 viewDir, float rise, float distance, float time, out half inside, out half lumps)
{
    float2 angle = float2(atan2(viewDir.x, viewDir.z), asin(clamp(viewDir.y, -1.0, 1.0))) * 57.29578;
    angle.x += angle.x < 0.0 ? 360.0 : 0.0;
    // The top is a broad curve along the azimuth (so the bank never leaves islands of sky inside it), broken up by
    // billows. Two octaves only: finer ones lift the top column by column, which the feather draws as vertical streaks.
    float drift = angle.x / 1.8 + time * 0.006;
    half swell = (2.0 * KarstWrapNoise(float2(drift, 3.7), 200.0) + KarstWrapNoise(float2(drift * 2.0, 20.8), 400.0)) / 3.0;
    float2 q = float2(angle.x / 1.5, angle.y / 0.7) + float2(time * 0.012, 0.0);
    half billow = KarstWrapFbm(q, 240.0);
    lumps = saturate(0.55 + (billow - KarstWrapFbm(q + float2(0.12, -0.4), 240.0)) * 2.5);
    float top = _CloudSea.x * (0.45 + _CloudSea.y * swell * swell * 1.4);
    top *= 1.0 + 0.8 * smoothstep(0.4, 0.6, _EnvHaze.x);   // a misty preset (thick forest haze) fills the valleys deeper
    float height = rise + (billow - 0.5) * top * 0.8;
    inside = saturate(1.0 - height / max(top, 1e-3));
    half cover = 1.0 - smoothstep(top * 0.4, top * 1.35, height);   // a feathered top, never a cut edge
    return cover * smoothstep(_CloudSea.z, _CloudSea.z * 1.7, distance) * _CloudSea.w;
}

// The valley cloud's colour: the sky's, brighter and warmer on the sunlit billows and glowing against the light,
// cooler and greyer down in the bank; seen through the same air wherever it lies, so the bank on a wall and the bank
// on the skirt behind (EnvMountainCloud) meet without a seam.
half3 KarstCloudColor(float3 viewDir, half inside, half lumps, half3 sunColor)
{
    half3 haze = EnvHazeColor(viewDir);
    half towards = pow(saturate(dot(viewDir, _EnvSunDir.xyz)), 5.0);
    // Lit billow tops, a grey-blue body below them in its own shade; the lumps catch the sun on the upper side.
    half3 top = haze * 1.12 + sunColor * (0.15 + 0.38 * lumps) + _EnvSkyGlow.rgb * towards * 0.6;
    half3 under = haze * half3(0.66, 0.74, 0.86);
    half shade = saturate(smoothstep(0.15, 0.85, inside) - (lumps - 0.5) * 0.4);
    half3 cloud = lerp(top, under, shade * 0.85);
    half aerial = (1.0 - exp(-_CloudSea.z * 2.5 / _Aerial.x)) * _Aerial.y * 0.65;
    half3 air = lerp(dot(haze, half3(0.2126, 0.7152, 0.0722)) * _AerialTint.rgb, haze, aerial * aerial);
    return lerp(cloud, air, aerial);
}

#endif
