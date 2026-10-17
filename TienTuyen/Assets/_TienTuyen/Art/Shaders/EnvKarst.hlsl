#ifndef TIEN_TUYEN_ENV_KARST_INCLUDED
#define TIEN_TUYEN_ENV_KARST_INCLUDED

#include "EnvKarstCloud.hlsl"

// Surface and air of the far landscape (EnvMountains): limestone towers and forested ranges. Needs EnvCommon.hlsl and
// the material properties below. Kept to the HLSL subset that also compiles as GLSL, so the offline preview
// (output/graphics/backdrop/_tools/preview.py) draws with this same code.
//   _Rock, _RockWarm, _RockDark   limestone: grey, cream-stained, and the black water streaks
//   _CanopyDark, _CanopyLight     forest
//   _Canopy                       x: crown relief (m), y: tone in the gaps between crowns
//   _CliffSlope                   x: bare rock from slope (1 - normal.y), y: its feather
//   _Flutes                       x: groove depth (m), y: groove width (m), z: groove length (m)
//   _Strata                       x: bed thickness (m), y: overhang depth (m), z: warp (m), w: forest on the benches
//   _RockTex                      x: tile of the rock texture (m), y: its relief (m), z: its contrast; the includer
//                                 defines KARST_ROCK(uv) to sample it (rgb albedo, a height; Mountain Lake's tiled cliff)
//   _MistColor, _MistHeight, _MistStrength, _AerialTint, _Aerial (x: distance m, y: strength, z: near-layer darkening)
//   _CloudSea                     x: valley cloud thickness above the nearer ridge (deg), y: its billow, z: distance
//                                 it starts at (m), w: strength
//   _Tower                        1 on the tower meshes (KarstAir spares the near towers the near-layer darkening)
//   _KarstSkyParams (global)      x: u offset, y: exposure, z: weight of the sky's photo (EnvSky's _Panorama*); the
//                                 includer defines KARST_SKY(uv) to sample its blurred copy (EnvironmentBackdrop)
//   _Light                        x: sun gain, y: shadow coolness, z: sunward rim; _Rim, _Translucency against the light

// Vertical relief the meshes are too coarse for: water-cut grooves and buttresses running down the walls, projected
// on the two vertical planes. Returns a height in metres.
float KarstFluteHeight(float3 p, float3 normal)
{
    float2 w = abs(normal.xz) / max(abs(normal.x) + abs(normal.z), 1e-3);
    float2 scale = float2(1.0 / _Flutes.y, 1.0 / _Flutes.z);
    float a = EnvFbm(float2(p.z, p.y) * scale);
    float b = EnvFbm(float2(p.x, p.y) * scale + 31.7);
    float groove = a * w.x + b * w.y;
    return (1.0 - abs(groove * 2.0 - 1.0)) * _Flutes.x;
}

// Limestone beds: each bulges out as it rises (an overhang, shadowed beneath) and the next is set back from it (a
// bench). Warped so no bed runs level for long. phase: 0 at the foot of a bed .. 1 at its lip.
float KarstStrataHeight(float3 p, out float phase)
{
    float y = p.y + (EnvFbm(float2(p.x + p.z, p.x - p.z) / 520.0) - 0.5) * 2.0 * _Strata.z + (EnvValueNoise(p.xz / 90.0) - 0.5) * _Strata.z * 0.4;
    phase = frac(y / _Strata.x);
    return (smoothstep(0.0, 0.84, phase) - smoothstep(0.84, 1.0, phase)) * _Strata.y;
}

// The rock texture on the walls, projected on the two vertical planes at two scales.
half4 KarstRockDetail(float3 p, float3 normal)
{
    float2 w = abs(normal.xz) / max(abs(normal.x) + abs(normal.z), 1e-3);
    float2 a = float2(p.z, p.y) / _RockTex.x, b = float2(p.x, p.y) / _RockTex.x;
    half4 coarse = KARST_ROCK(a) * w.x + KARST_ROCK(b) * w.y;
    half4 fine = KARST_ROCK(a * 3.7 + 0.31) * w.x + KARST_ROCK(b * 3.7 + 0.57) * w.y;
    return coarse * 0.6 + fine * 0.4;
}

// Normal bent by a height defined over the surface (surface gradient from screen derivatives).
float3 KarstBump(float3 normal, float3 p, float height)
{
    float3 dpdx = ddx(p);
    float3 dpdy = ddy(p);
    float3 r1 = cross(dpdy, normal);
    float3 r2 = cross(normal, dpdx);
    float det = dot(dpdx, r1);
    float3 gradient = (ddx(height) * r1 + ddy(height) * r2) * sign(det);
    return normalize(abs(det) * normal - gradient);
}

// Tree crowns: one jittered cell of about 10 m per crown with a dome of its own size over it, the crowns gathered in
// clumps with open dark gaps between them. Returns the crown height (0 in a gap .. 1 on top of a crown); tree is a
// random number per crown, for its tint.
half KarstCrowns(float2 xz, float distance, out half tree)
{
    float2 q = xz / 10.0 + (EnvValueNoise(xz / 23.0) - 0.5) * 0.8;   // a little warp, so no row of crowns lines up
    float2 cell = floor(q);
    half best = 0.0;
    tree = 0.5;
    [unroll] for (int j = -1; j <= 1; j++)
    {
        [unroll] for (int i = -1; i <= 1; i++)
        {
            float2 c = cell + float2(i, j);
            float h = EnvHash21(c);   // one hash per crown, its other numbers drawn from it
            float3 more = frac(h * float3(17.13, 31.71, 57.37));
            float2 d = q - (c + 0.15 + 0.7 * float2(h, more.x));
            half r = 0.45 + 0.5 * more.y;
            half dome = saturate(1.0 - dot(d, d) / (r * r)) * (0.6 + 0.4 * r);
            tree = dome > best ? more.z : tree;
            best = max(best, dome);
        }
    }
    half clump = smoothstep(0.32, 0.55, EnvFbm(xz / 85.0 + 3.3));
    // Leaf clumps break each dome up where it is close enough to show them.
    half near = 1.0 - smoothstep(250.0, 900.0, distance);   // past this a leaf clump is under a pixel
    half leaves = 1.0 - near * (0.3 - 0.3 * EnvValueNoise(xz / 2.7) - 0.15 * (EnvValueNoise(xz / 1.1) - 0.5));
    return pow(best, 0.6) * clump * leaves;
}

// Limestone and forest. tower: 1 on the tower meshes, 0 on the ranges; up: height up the tower wall (0 foot .. 1 crown).
// Returns the albedo; normal comes back with the fine relief, vegetation the forest cover (0..1).
half3 KarstSurface(float3 p, inout float3 normal, half cavity, half tower, half up, float distance, out half vegetation)
{
    // The gorge mouth's towers, a kilometre from the game (the farther towers are 2 km and more).
    half close = tower * (1.0 - smoothstep(1600.0, 2800.0, distance));
    // Relief only where a groove spans a few pixels; farther, the surface is the mesh and its colour.
    half detail = 1.0 - smoothstep(_Flutes.y * 700.0, _Flutes.y * 1400.0, distance);
    half wall = smoothstep(0.35, 0.6, 1.0 - normal.y);
    float flute = KarstFluteHeight(p, normal);
    float phase;
    float strata = KarstStrataHeight(p, phase);
    half bedDetail = (1.0 - smoothstep(_Strata.x * 500.0, _Strata.x * 1000.0, distance)) * wall;
    half4 texel = KarstRockDetail(p, normal);
    half texDetail = (1.0 - smoothstep(_RockTex.x * 60.0, _RockTex.x * 140.0, distance)) * wall;
    // Crowns resolve to a few pixels out to about 2 km; farther the canopy is its mean tone.
    half crownDetail = 1.0 - smoothstep(1000.0, 2600.0, distance);
    half clearing = smoothstep(0.62, 0.76, EnvFbm(p.xz / 240.0 + 1.9)) * smoothstep(0.35, 0.15, 1.0 - normal.y) * (1.0 - tower);
    half tree = 0.5, crown = 0.0;
    UNITY_BRANCH if (crownDetail > 0.0)   // where it turns, crownRelief is ~0, so the quad's derivatives do not matter
        crown = KarstCrowns(p.xz, distance, tree) * (1.0 - clearing);
    // The near towers' forest cap hangs on steep ground: its crowns laid out on the wall (round the tower, up it).
    half capCrowns = close * smoothstep(0.62, 0.78, up);
    UNITY_BRANCH if (capCrowns > 0.0)
    {
        half wallTree;
        half wallCrown = KarstCrowns(float2(p.x - p.z, p.y * 1.3), distance, wallTree);
        crown = lerp(crown, wallCrown, wall);
        tree = lerp(tree, wallTree, wall);
    }
    half crownRelief = crown * _Canopy.x * crownDetail * max(1.0 - wall * tower, capCrowns);
    normal = KarstBump(normal, p, flute * detail * wall + strata * bedDetail + texel.a * _RockTex.y * texDetail * (1.0 - 0.75 * close) + crownRelief);   // no branch: derivatives need the quad
    half slope = 1.0 - normal.y;
    half groove = flute / _Flutes.x;                 // 0 in a groove .. 1 on a rib

    // Bare rock on the walls, broken by ledges where the bent normal flattens (trees hold there) and by the forest
    // creeping down the grooves. The ranges only bare their rock when tall and steep.
    half breakup = (EnvFbm(p.xz / 70.0 + p.y / 90.0) - 0.5) * 0.3;
    half bare = smoothstep(_CliffSlope.x - _CliffSlope.y, _CliffSlope.x + _CliffSlope.y, slope + breakup - (1.0 - groove) * 0.12 * tower - 0.16 * (1.0 - tower));
    bare *= lerp(smoothstep(120.0, 220.0, p.y + (EnvValueNoise(p.xz / 90.0) - 0.5) * 80.0), 1.0, tower);
    bare *= 1.0 - tower * smoothstep(0.86, 0.97, up) * 0.75;   // the crown is forest even where it is steep
    // The gorge mouth's towers, a kilometre out, wear a whole forest cap down over their shoulders, its edge ragged.
    bare *= 1.0 - close * smoothstep(0.7, 0.84, up + (EnvFbm(float2((p.x + p.z) / 40.0, p.y / 25.0)) - 0.5) * 0.22);
    half bench = smoothstep(0.84, 0.9, phase) * (1.0 - smoothstep(0.98, 1.0, phase)) * bedDetail * _Strata.w;
    bench *= lerp(1.0, smoothstep(0.45, 0.65, EnvValueNoise(float2((p.x - p.z) / 45.0, p.y / 30.0))), close);   // close up, in clumps, not a stripe
    UNITY_BRANCH if (tower > 0.5)   // a material constant: towers or ranges
    {
        // On the towers, scrub and trees cling to the walls in broken patches along the bedding and down the grooves,
        // leaving the sheerest faces bare: about half of each wall is green, as on Ha Long and Phong Nha.
        half cling = EnvFbm(float2((p.x + p.z) / 110.0, p.y / 32.0 + EnvFbm(p.xz / 160.0) * 2.5) + 9.1);
        cling += (1.0 - groove) * 0.18 - smoothstep(0.92, 1.0, slope) * 0.1;
        // Ledges: every bedding step and flattening of the wall holds a fringe of trees, thickest on the near towers.
        half ledgeBand = smoothstep(0.55, 0.75, EnvValueNoise(float2((p.x - p.z) / 140.0, p.y / 16.0 + EnvFbm(p.xz / 120.0) * 2.0)));
        half ledge = max(max(smoothstep(0.32, 0.12, slope), ledgeBand * smoothstep(0.3, 0.5, cling)), bench);
        half nearTower = 1.0 - smoothstep(3500.0, 8000.0, distance);
        half lush = 0.06 * nearTower + 0.06 * close;   // nearer, the scrub holds on steeper rock
        bare *= 1.0 - max(smoothstep(0.52 - lush, 0.62 - lush, cling), ledge * (0.6 + 0.4 * nearTower));
    }
    else
        bare *= 1.0 - bench;
    vegetation = 1.0 - bare;

    // Forest: everywhere it can hold, as on the karst of Ha Giang and Phong Nha. Kilometre patches of lighter and
    // darker canopy, crowns, darker wet hollows, a sunlit yellow-green on the crowns of the towers.
    half3 canopy = half3(0.0, 0.0, 0.0);
    UNITY_BRANCH if (bare < 0.999)
    {
        half patches = EnvFbm(p.xz / 420.0);
        half crowns = EnvValueNoise(p.xz / 31.0) * 0.6 + EnvValueNoise(p.xz / 9.0) * 0.4;
        canopy = lerp(_CanopyDark.rgb, _CanopyLight.rgb, saturate(patches * 1.4 - 0.2));
        // Stands of darker, bluer conifers among the broadleaf; lit crowns over dark gaps; grass and scrub clearings on
        // the gentle ground, a little laterite earth breaking through on the steeper slopes of the ranges.
        canopy = lerp(canopy, canopy * half3(0.6, 0.78, 0.92), smoothstep(0.42, 0.62, EnvFbm(p.xz / 380.0 + 7.7)));
        half3 treeTint = lerp(half3(0.82, 0.9, 1.0), half3(1.18, 1.1, 0.82), tree);   // bluer and yellower crowns
        canopy *= lerp(half3(0.85, 0.85, 0.85), lerp(_Canopy.y, 1.15, crown) * treeTint, crownDetail);
        canopy *= 0.85 + 0.3 * crowns;
        half3 meadow = lerp(half3(0.34, 0.4, 0.13), half3(0.44, 0.38, 0.2), EnvValueNoise(p.xz / 60.0));
        canopy = lerp(canopy, meadow, clearing);
        half earth = smoothstep(0.58, 0.74, EnvFbm(p.xz / 120.0 + 4.4)) * smoothstep(0.25, 0.45, slope) * (1.0 - tower);
        canopy = lerp(canopy, half3(0.55, 0.29, 0.17), earth * 0.85);
        canopy *= 1.0 + 0.4 * clamp(cavity, -1.0, 0.5);
        canopy = lerp(canopy, canopy * half3(1.15, 1.12, 0.8), tower * smoothstep(0.75, 1.0, up));
        canopy = lerp(canopy, canopy * half3(1.55, 1.6, 1.05) + half3(0.015, 0.025, 0.0), close);   // a fresh, near green
    }

    // Limestone: pale grey with broad cream and blue-grey washes, black streaks running down from every ledge and
    // groove, iron stains in places, and a faint warped bedding (never an even stripe).
    half3 rock = half3(0.0, 0.0, 0.0);
    UNITY_BRANCH if (bare > 0.001)
    {
        float2 w = abs(normal.xz) / max(abs(normal.x) + abs(normal.z), 1e-3);
        half wash = EnvFbm(float2(p.x + p.z, p.y * 1.7) / 380.0);
        rock = lerp(_Rock.rgb, _RockWarm.rgb, saturate(smoothstep(0.45, 0.75, wash) + 0.35 * close));
        rock *= 0.86 + 0.28 * EnvValueNoise(float2(p.x - p.z, p.y) / 47.0);
        rock *= lerp(1.0, pow(max(dot(texel.rgb, half3(0.333, 0.333, 0.333)) / 0.453, 0.0), _RockTex.z), texDetail);
        half streak = EnvFbm(float2(p.z / 7.0, p.y / 120.0)) * w.x + EnvFbm(float2(p.x / 7.0, p.y / 120.0) + 11.3) * w.y;
        half dark = smoothstep(0.42, 0.7, streak + (0.5 - groove) * 0.35);
        half iron = smoothstep(0.62, 0.8, EnvFbm(float2(p.x + p.z, p.y * 0.3) / 160.0 + 5.7)) * (1.0 - dark);
        rock = lerp(rock, rock * half3(1.05, 0.8, 0.6), iron * 0.55);
        // Close up, ochre run-off down the walls, the warm streaks the far ranges show as a cream-orange cast.
        half ochre = smoothstep(0.5, 0.72, EnvFbm(float2((p.x - p.z) / 11.0, p.y / 170.0) + 3.7)) * (1.0 - dark);
        rock = lerp(rock, lerp(rock, _RockWarm.rgb, 0.6) * half3(1.08, 0.84, 0.64), ochre * 0.7 * close);
        rock = lerp(rock, _RockDark.rgb, dark * 0.4);
        half bedding = EnvValueNoise(float2((p.x + p.z) / 260.0, p.y / 23.0 + EnvFbm(p.xz / 190.0) * 3.0));
        rock *= 0.93 + 0.1 * bedding;
        // Under every overhang the rock is wet and dark; the lip above catches the light.
        rock *= 1.0 - 0.25 * (1.0 - smoothstep(0.0, 0.3, phase)) * bedDetail;
        rock *= 1.0 + 0.15 * smoothstep(0.6, 0.84, phase) * (1.0 - smoothstep(0.84, 0.9, phase)) * bedDetail;
    }
    return lerp(canopy, rock, bare);
}

// Light: hard sun from the preset with a cool shadow side, warm crests and rims where the walls turn to the sun, and
// against the light the forest glows through and the ridge lines catch a bright rim. ambient is the sky light (SH).
half3 KarstLight(half3 albedo, float3 normal, float3 viewDir, half3 sunColor, half3 ambient, half sunVisible, half skyVisible,
                 half cavity, half vegetation, half nearField)
{
    float3 sunDir = _EnvSunDir.xyz;
    half ndl = dot(normal, sunDir);
    half lambert = saturate((ndl + 0.12) / 1.12);
    half lit = lambert * lambert * sunVisible;
    half3 warm = sunColor * half3(1.1, 1.0, 0.84);
    half3 direct = warm * lit * _Light.x;
    half3 cool = lerp(half3(1.0, 1.0, 1.0), half3(0.74, 0.87, 1.18), (1.0 - lit) * _Light.y);
    half3 sky = ambient * cool * skyVisible * (0.65 + 0.35 * saturate(cavity + 0.6)) * (1.45 - 0.75 * vegetation);   // pale limestone glows in the sky light; under a canopy the shade is deep
    half3 color = albedo * (direct + sky);
    half fresnel = 1.0 - saturate(dot(normal, -viewDir));
    half edge = (1.0 - nearField) * saturate(cavity * 3.0 + 0.4);
    // The sun's side of every silhouette: a warm rim.
    color += warm * pow(fresnel, 4.0) * saturate(ndl + 0.25) * sunVisible * _Light.z * edge * 0.25;
    half towards = pow(saturate(dot(viewDir, sunDir)), 4.0);
    color += albedo * sunColor * vegetation * towards * _Translucency * sunVisible;
    color += _EnvSkyGlow.rgb * pow(fresnel, 6.0) * towards * _Rim * (0.35 + 0.65 * sunVisible) * edge;
    return color;
}

// The river where it runs on past the game's terrain (the corridor mesh, EnvironmentBackdrop): jade karst water under
// a sheen of sky, the banks mirrored dark along both edges, ripples drifting with the flow that break the mirror and
// fade out with distance, and the sun's glint. water: 0..1 cover; bank: metres to the nearer bank.
half3 KarstWater(half3 color, float3 p, float3 viewDir, float distance, half water, half bank, half3 sunColor, half3 ambient, float time)
{
    float2 q = p.xz / 7.0 + float2(time * 0.08, time * 0.03);
    half ripple = 0.14 * (1.0 - smoothstep(600.0, 3000.0, distance));
    float3 n = normalize(float3((EnvValueNoise(q) - 0.5) * ripple, 1.0, (EnvValueNoise(q * 1.7 + 5.3) - 0.5) * ripple));
    float3 r = reflect(viewDir, n);
    half3 sky = EnvSkyColor(normalize(float3(r.x, max(r.y, 0.02), r.z)));
    half fresnel = 0.12 + 0.6 * pow(1.0 - saturate(dot(-viewDir, n)), 5.0);
    half3 body = ambient * half3(0.16, 0.3, 0.24);                   // jade, the colour of the karst rivers
    half3 banks = color * 0.35;                                       // the wooded banks mirrored along the edges
    half3 mirror = lerp(banks, sky, smoothstep(2.0, 8.0, bank));
    half towards = saturate(dot(r, _EnvSunDir.xyz));
    half3 glint = sunColor * (pow(towards, 400.0) * 8.0 + pow(towards, 24.0) * 0.35);
    return lerp(color, lerp(body, mirror, fresnel) + glint, water);
}

// The sky right behind a range, as EnvSky draws it: its gradient low down, its photo above, here blurred so no cloud
// shows through the stone. The air thickens towards it, so a range fades into the blue it stands against.
half3 KarstSkyBehind(float3 viewDir)
{
    half3 sky = EnvHazeColor(viewDir);
    half weight = _KarstSkyParams.z * smoothstep(0.0, 0.16, viewDir.y);
    UNITY_BRANCH if (weight > 0.0)
    {
        float2 uv = float2(frac(atan2(viewDir.x, viewDir.z) * 0.15915 + _KarstSkyParams.x), 0.5 + asin(saturate(viewDir.y)) * 0.31831);
        half sun = saturate(dot(viewDir, _EnvSunDir.xyz));
        sky = lerp(sky, KARST_SKY(uv) * _KarstSkyParams.y, weight) + _EnvSkyGlow.rgb * (pow(sun, 6.0) * 0.2 + pow(sun, 48.0) * 0.9) * weight;
    }
    return sky;
}

// Valley mist, aerial perspective over the true distance (near layers dark and rich, each farther one a step paler
// and bluer), the haze, and the valley cloud over them.
half3 KarstAir(half3 color, float3 p, float3 viewDir, float distance, half nearField, float rise, half3 sunColor, float time)
{
    half3 haze = KarstSkyBehind(viewDir);
    // The near-layer darkening sets the ranges' first ridges off; a tower a kilometre out stands in clear air.
    color *= 1.0 - _Aerial.z * (1.0 - smoothstep(1500.0, 9000.0, distance)) * (1.0 - nearField) * lerp(1.0, smoothstep(1200.0, 2600.0, distance), _Tower);
    half mist = exp(-max(p.y + 60.0, 0.0) / _MistHeight) * (1.0 - exp(-distance / 3000.0)) * _MistStrength * (1.0 - nearField);
    color = lerp(color, lerp(_MistColor.rgb, haze, 0.4), saturate(mist));
    half aerial = (1.0 - exp(-distance / _Aerial.x)) * _Aerial.y * (1.0 - nearField);
    half3 air = lerp(dot(haze, half3(0.2126, 0.7152, 0.0722)) * _AerialTint.rgb, haze, aerial * aerial);
    color = lerp(color, air, aerial);
    color = lerp(color, haze, EnvHazeAmount(distance, p.y));
    // Skipped where no billow can reach: nearer than the bank starts, or higher above the ridge in front than the
    // tallest top (KarstValleyCloud: height >= rise - 0.4 top, cover 0 past 1.35 top, top <= x (0.45 + 1.4 y) 1.8).
    UNITY_BRANCH if (distance > _CloudSea.z && rise < _CloudSea.x * (0.45 + 1.4 * _CloudSea.y) * 1.8 * 1.75)
    {
        half inside, lumps;
        half cover = KarstValleyCloud(viewDir, rise, distance, time, inside, lumps);
        // Within reach (under ~10 km) the bank lies in the valleys, a few hundred metres deep: a mountain standing only a
        // few degrees over the ridge in front would otherwise sink whole in it and read as a pale slab of glass.
        float above = max(rise, 0.0) * 0.01745 * distance;
        cover *= lerp(1.0 - smoothstep(220.0, 480.0, above), 1.0, smoothstep(9000.0, 13000.0, distance));
        color = lerp(color, KarstCloudColor(viewDir, inside, lumps, sunColor), cover);
    }
    return color;
}

#endif
