using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The intro's A-frame log shelter (hầm chữ A), built at runtime for filming only: two rows of leaning logs under
/// an earth mound, an earth floor, the VEF-206-style radio on the ammo crate with the letter and the photo, a hanging
/// hurricane lamp and the slogan cloth on a stick over the radio, all at the mouth facing in. Local frame: origin on the floor at the middle of the mouth,
/// +Z into the shelter, the mouth facing -Z. Textures come from <c>Shelter/</c> beside this file (see CREDITS in
/// output/intro); the drawn ones are made by output/shelter/draw_props.py.
/// The film drives it: <see cref="Tuning"/> moves the needle, <see cref="DialGlow"/> lights the dial,
/// <see cref="Impact"/> shakes the set, and <see cref="Tick"/> advances it on the film clock.
/// </summary>
public sealed class ShelterSet
{
    public const float Length = 3.4f, HalfBase = 1.7f, Crossing = 2.2f;
    /// <summary>Dial scale ends and the station "Maybe" plays on, as fractions of the radio face (draw_props.py).</summary>
    public const float DialLeft = .115f, DialRight = .885f, Station = .655f;
    private const float RadioWidth = .30f, RadioHeight = .176f, RadioDepth = .085f;
    private const float DialTop = .07f, DialBottom = .47f, WireLength = .5f;

    public Transform Root { get; private set; }
    /// <summary>Where the hero stands: on the apron just outside the mouth, off the middle, the radio inside beside him.</summary>
    public Vector3 HeroSpot => Root.TransformPoint(new Vector3(-.4f, Apron(-.9f), -.9f));
    /// <summary>Where his rifle leans: its foot on the floor behind the crate, its muzzle on the right-hand logs.</summary>
    public Vector3 RifleFoot => Root.TransformPoint(new Vector3(.74f, 0, 1.02f));
    public Vector3 RifleTop => Root.TransformPoint(new Vector3(1.0f, .84f, 1.08f));
    public Transform Radio { get; private set; }
    /// <summary>The middle of the dial window, on its face.</summary>
    public Vector3 DialCentre => Radio.TransformPoint(new Vector3(0, DialY, -RadioDepth * .5f));
    public Vector3 DialNormal => -Radio.forward;
    public Vector3 LampPosition => flame.position;
    /// <summary>Needle position across the dial, 0 to 1 (the hand on the knob).</summary>
    public float Tuning { get; set; } = Station;
    /// <summary>Dial lamp brightness, 0 to 1.</summary>
    public float DialGlow { get; set; } = .45f;
    /// <summary>Strength of the continuous lamp flicker, 1 by default.</summary>
    public float Flicker { get; set; } = 1f;

    /// <summary>Height of the earth apron that runs down from the mouth (z -.45 to -1.6, falling .9).</summary>
    private static float Apron(float z) => -.9f * Mathf.Clamp01((-.45f - z) / 1.15f);

    private static float DialY => (.5f - (DialTop + DialBottom) * .5f) * RadioHeight;

    private readonly string folder;
    private readonly List<Material> materials = new List<Material>();
    private Transform needle, knob, lampPivot, flame;
    private Light lamp, doorFlash;
    private Material dialMaterial, flameMaterial, glassMaterial;
    private ParticleSystem grains, haze, motes;
    /// <summary>Gaps in the roof where dust shakes loose, in set-local space.</summary>
    private readonly List<Vector3> seams = new List<Vector3>();
    private Vector3 radioRest;
    private float time, needleOffset, needleSpeed, detune, swing, swingSpeed, gutter, hop, hopSpeed, dust, flash;
    private float grainCarry;

    private ShelterSet(string folder) => this.folder = folder;

    /// <summary>
    /// Builds the set under <paramref name="parent"/> at its origin. <paramref name="photoPath"/> is the PNG of the
    /// photo on the crate (null: the placeholder in Shelter/).
    /// </summary>
    public static ShelterSet Build(Transform parent, string photoPath = null)
    {
        var set = new ShelterSet(Path.Combine(Application.dataPath, "_TienTuyen/IntroCinematic/Shelter"));
        set.Root = new GameObject("ShelterSet").transform;
        set.Root.SetParent(parent, false);
        set.BuildShell();
        set.BuildProps(photoPath ?? Path.Combine(set.folder, "photo.png"));
        set.BuildLamp();
        set.BuildDust();
        return set;
    }

    /// <summary>Local points spanning the floor, for the caller to seat the set on uneven ground.</summary>
    public static IEnumerable<Vector3> FootprintPoints()
    {
        for (float z = -.4f; z <= Length + .5f; z += .5f)
        for (float x = -HalfBase - .3f; x <= HalfBase + .3f; x += .4f)
            yield return new Vector3(x, 0, z);
    }

    /// <summary>A bomb lands nearby: the needle jumps, the lamp swings and gutters, dust sifts from the logs.</summary>
    public void Impact(float strength)
    {
        needleSpeed += 2.4f * strength * (needleOffset > 0 ? -1f : 1f);
        detune += .035f * strength;
        swingSpeed += 1.6f * strength;
        gutter = Mathf.Max(gutter, .8f * strength);
        hopSpeed += .35f * strength;
        dust = Mathf.Max(dust, strength);
        flash = Mathf.Max(flash, strength);
        if (haze == null) return;
        for (int i = 0; i < Mathf.RoundToInt(40 * strength); i++)
        {
            var p = Root.TransformPoint(seams[Random.Range(0, seams.Count)] + Random.insideUnitSphere * .25f);
            haze.Emit(new ParticleSystem.EmitParams
            {
                position = p, velocity = new Vector3(Random.Range(-.05f, .05f), -Random.Range(.05f, .2f), Random.Range(-.05f, .05f)),
                startSize = Random.Range(.35f, .8f), startLifetime = Random.Range(4f, 6.5f),
                startColor = new Color(.7f, .55f, .4f, Random.Range(.08f, .16f)), rotation = Random.Range(0f, 360f)
            }, 1);
        }
    }

    /// <summary>Resets the shake state between takes (dust in the air settles on its own).</summary>
    public void Settle()
    {
        needleOffset = needleSpeed = detune = swing = swingSpeed = gutter = hop = hopSpeed = dust = flash = 0;
        grains?.Clear();
        haze?.Clear();
    }

    /// <summary>Advances the needle, lamp and dust by <paramref name="dt"/> seconds of film time.</summary>
    public void Tick(float dt)
    {
        if (dt <= 0) return;
        time += dt;
        // The pointer rides a dial cord: it overshoots and rings after a knock.
        const float needleK = 900f, needleC = 9f;
        needleSpeed += (-needleK * needleOffset - needleC * needleSpeed) * dt;
        needleOffset += needleSpeed * dt;
        float u = Mathf.Clamp(Tuning + detune + needleOffset, 0, 1);
        needle.localPosition = new Vector3((Mathf.Lerp(DialLeft, DialRight, u) - .5f) * RadioWidth, DialY, -RadioDepth * .5f - .0035f);
        knob.localRotation = Quaternion.Euler(Tuning * 900f, 0, 0) * Quaternion.Euler(0, 0, 90f);
        // The radio hops on the crate lid.
        hopSpeed += (-600f * hop - 22f * hopSpeed) * dt;
        hop += hopSpeed * dt;
        Radio.localPosition = radioRest + Vector3.up * Mathf.Max(0, hop * .02f);
        // The lamp is a pendulum on its wire.
        swingSpeed += (-9.81f / WireLength * swing - .5f * swingSpeed) * dt;
        swing += swingSpeed * dt;
        lampPivot.localRotation = Quaternion.Euler(swing * Mathf.Rad2Deg * .3f, 0, swing * Mathf.Rad2Deg);
        gutter = Mathf.MoveTowards(gutter, 0, dt / 1.6f);
        float flicker = 1f + Flicker * ((Mathf.PerlinNoise(time * 2.3f, .7f) - .5f) * .22f + (Mathf.PerlinNoise(time * 11f, 3.1f) - .5f) * .1f);
        float level = flicker * (1f - .75f * gutter * (.6f + .4f * Mathf.PerlinNoise(time * 23f, 9.2f)));
        lamp.intensity = 3.4f * level;
        glassMaterial.SetColor("_EmissionColor", new Color(1f, .6f, .3f) * (.35f * level));
        flame.localScale = new Vector3(.016f, .04f * Mathf.Lerp(.4f, 1.1f, level), .016f);
        flameMaterial.SetColor("_BaseColor", new Color(1f, .62f, .25f) * (3.5f * level));
        dialMaterial.SetColor("_EmissionColor", new Color(1f, .78f, .45f) * (.5f * DialGlow));
        // A far flash in the mouth, falling off fast.
        flash = Mathf.MoveTowards(flash, 0, dt / .35f);
        doorFlash.intensity = 40f * flash * flash;
        // Grains stream from the seams while the dust is shaken loose.
        dust = Mathf.MoveTowards(dust, 0, dt / 2.2f);
        grainCarry += dust * dust * 2600f * dt;
        for (; grainCarry >= 1f; grainCarry--)
        {
            var p = Root.TransformPoint(seams[Random.Range(0, seams.Count)] + new Vector3(Random.Range(-.02f, .02f), 0, Random.Range(-.02f, .02f)));
            bool clod = Random.value < .06f;
            grains.Emit(new ParticleSystem.EmitParams
            {
                position = p, velocity = new Vector3(Random.Range(-.04f, .04f), -Random.Range(.05f, .4f), Random.Range(-.04f, .04f)),
                startSize = clod ? Random.Range(.012f, .022f) : Random.Range(.004f, .009f), startLifetime = 2.2f,
                startColor = new Color(.85f, .66f, .45f, clod ? 1f : Random.Range(.55f, .95f))
            }, 1);
        }
    }

    // ---------------------------------------------------------------- build

    private void BuildShell()
    {
        var bark = Lit("bark", Load("bark_albedo.jpg", false), Load("bark_normal.jpg", true), new Color(.78f, .74f, .7f), .18f);
        var ends = Lit("log_end", Load("log_end.png", false), null, Color.white, .1f);
        var earth = Lit("earth", Load("earth_albedo.jpg", false), Load("earth_normal.jpg", true), new Color(.55f, .47f, .42f), .08f);
        var earthTwoSided = Lit("earth_shell", earth.GetTexture("_BaseMap"), earth.GetTexture("_BumpMap"), new Color(.36f, .31f, .27f), .05f);
        earthTwoSided.SetFloat("_Cull", (float)CullMode.Off);
        var random = new System.Random(1972);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);

        // Leaning log pairs, crossing at the top; the ridge pole lies in the crotch.
        var logs = new MeshBuilder();
        for (float z = .06f; z <= Length; z += .165f)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                float zz = z + (side > 0 ? .014f : 0) + R(-.01f, .01f);
                var foot = new Vector3(side * (HalfBase + .12f + R(-.03f, .03f)), -.12f, zz);
                var top = new Vector3(-side * (.2f + R(0, .08f)), Crossing + .27f + R(-.04f, .04f), zz + R(-.02f, .02f));
                logs.Log(foot, top, R(.066f, .084f), random);
                if (side > 0 && z > .4f && z < Length - .3f && random.NextDouble() < .7)
                    seams.Add(Vector3.Lerp(foot, top, R(.55f, .85f)) + new Vector3(0, -.08f, .08f));
            }
            seams.Add(new Vector3(R(-.12f, .12f), Crossing - .08f, z + .08f));
        }
        // Loose earth over the radio and the lamp, where the camera looks when the bombs land.
        for (int i = 0; i < 10; i++) seams.Add(new Vector3(R(.05f, .45f), Crossing * .74f, R(.3f, .8f)));
        for (int i = 0; i < 5; i++) seams.Add(new Vector3(R(-.3f, .3f), Crossing - .1f, R(.8f, 1.6f)));
        logs.Log(new Vector3(0, Crossing + .1f, -.3f), new Vector3(0, Crossing + .1f, Length + .25f), .075f, random);
        // Back wall: thin upright poles against the earth.
        for (int i = -4; i <= 4; i++)
        {
            float x = i * .3f, h = Crossing * (1f - Mathf.Abs(x) / HalfBase) + .05f;
            if (h > .2f) logs.Log(new Vector3(x, -.05f, Length - .02f), new Vector3(x + R(-.02f, .02f), h, Length - .02f), R(.03f, .04f), random);
        }
        Emit("Logs", logs, bark, ends);

        // Earth: the floor, an apron down from the mouth, the back wall and the mound over the logs.
        var floor = new MeshBuilder();
        floor.Quad(new Vector3(-HalfBase - .4f, 0, -.45f), new Vector3(HalfBase + .4f, 0, -.45f),
            new Vector3(HalfBase + .4f, 0, Length + .2f), new Vector3(-HalfBase - .4f, 0, Length + .2f), 1.2f);
        floor.Quad(new Vector3(-2.4f, -.9f, -1.6f), new Vector3(2.4f, -.9f, -1.6f),
            new Vector3(2.4f, 0, -.45f), new Vector3(-2.4f, 0, -.45f), 1.2f);
        floor.Triangle(new Vector3(-HalfBase - .1f, 0, Length), new Vector3(HalfBase + .1f, 0, Length), new Vector3(0, Crossing + .1f, Length), 1.2f);
        Emit("Floor", floor, earth, null).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.TwoSided;
        var mound = new MeshBuilder();
        // The inner face keeps 0.2 m outside the log axes, so no earth shows between the logs; the outer face is the mound.
        float slope = (HalfBase + .36f) / (Crossing + .39f);
        float Axis(float y) => -(HalfBase + .12f) + (y + .12f) * slope;
        float[] heights = { -.6f, .3f, Crossing * .75f };
        var inner = new Vector2[7];
        var outer = new Vector2[7];
        for (int i = 0; i < 3; i++)
        {
            inner[i] = new Vector2(Axis(heights[i]) - .2f, heights[i]);
            outer[i] = new Vector2(Axis(heights[i]) - 1.1f + .35f * i, heights[i] + .3f * i);
        }
        inner[3] = new Vector2(0, Crossing + .4f);
        outer[3] = new Vector2(0, Crossing + .75f);
        for (int i = 0; i < 3; i++)
        {
            inner[6 - i] = new Vector2(-inner[i].x, inner[i].y);
            outer[6 - i] = new Vector2(-outer[i].x, outer[i].y);
        }
        const float front = -.08f, back = Length + .7f;
        for (int i = 0; i < outer.Length - 1; i++)
        {
            Vector3 P(Vector2 v, float z) => new Vector3(v.x, v.y, z);
            mound.Quad(P(inner[i], front), P(inner[i + 1], front), P(outer[i + 1], front), P(outer[i], front), .5f);
            mound.Quad(P(outer[i], front), P(outer[i + 1], front), P(outer[i + 1], back), P(outer[i], back), .5f);
            mound.Quad(P(inner[i + 1], .06f), P(inner[i], .06f), P(inner[i], back), P(inner[i + 1], back), .5f);
        }
        Emit("Mound", mound, earthTwoSided, null).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.TwoSided;
    }

    private void BuildProps(string photoPath)
    {
        // The ammo crate: the game's supply_crate model, scaled to an ammunition box.
        var cratePrefab = Resources.Load<GameObject>("Models/supply_crate");
        var crate = new GameObject("Crate").transform;
        crate.SetParent(Root, false);
        crate.localPosition = new Vector3(.55f, 0, .55f);
        crate.localRotation = Quaternion.Euler(0, 188f, 0); // its front, and the radio's dial, face into the shelter
        float crateTop = .4f;
        if (cratePrefab != null)
        {
            var model = Object.Instantiate(cratePrefab, crate);
            model.transform.localRotation = Quaternion.Euler(-90f, 0, 0); // Blender Z-up, as CombatArtDirector imports it
            Bounds b = WorldBounds(model);
            model.transform.localScale *= .62f / Mathf.Max(b.size.x, b.size.z);
            b = WorldBounds(model);
            model.transform.position += crate.position - new Vector3(b.center.x, b.min.y, b.center.z);
            crateTop = WorldBounds(model).max.y - crate.position.y;
        }
        else
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.SetParent(crate, false);
            box.transform.localScale = new Vector3(.62f, .4f, .42f);
            box.transform.localPosition = Vector3.up * .2f;
            box.GetComponent<Renderer>().sharedMaterial = Lit("crate", null, null, new Color(.3f, .28f, .18f), .1f);
        }

        // The radio, on the crate's front.
        Radio = new GameObject("Radio").transform;
        Radio.SetParent(crate, false);
        radioRest = new Vector3(.06f, crateTop + RadioHeight * .5f, .02f);
        Radio.localPosition = radioRest;
        Radio.localRotation = Quaternion.Euler(0, -6f, 0);
        var leather = Lit("radio_case", null, null, new Color(.055f, .05f, .045f), .38f);
        var chrome = Lit("radio_trim", null, null, new Color(.62f, .62f, .6f), .55f);
        chrome.SetFloat("_Metallic", .8f);
        Box(Radio, "Case", Vector3.zero, new Vector3(RadioWidth, RadioHeight, RadioDepth), leather);
        var front = Load("radio_front.png", false);
        var grille = Lit("radio_front", front, null, Color.white, .45f);
        dialMaterial = Lit("radio_dial", front, null, Color.white, .6f);
        dialMaterial.EnableKeyword("_EMISSION");
        dialMaterial.SetTexture("_EmissionMap", front);
        dialMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        float split = .5f;
        FaceQuad(Radio, "Dial", new Rect(0, 1 - split, 1, split), dialMaterial);
        FaceQuad(Radio, "Grille", new Rect(0, 0, 1, 1 - split), grille);
        var needleMaterial = Lit("needle", null, null, new Color(.85f, .16f, .08f), .5f);
        needleMaterial.EnableKeyword("_EMISSION");
        needleMaterial.SetColor("_EmissionColor", new Color(.25f, .03f, .01f));
        needle = Box(Radio, "Needle", Vector3.zero, new Vector3(.0022f, (DialBottom - DialTop) * RadioHeight * .94f, .0012f), needleMaterial).transform;
        knob = Cylinder(Radio, "TuningKnob", new Vector3(RadioWidth * .5f + .007f, .03f, 0), new Vector3(.044f, .007f, .044f), chrome).transform;
        Cylinder(Radio, "VolumeKnob", new Vector3(RadioWidth * .5f + .006f, -.035f, 0), Quaternion.Euler(0, 0, 90f), new Vector3(.034f, .006f, .034f), chrome);
        for (int i = 0; i < 5; i++)
            Box(Radio, "Key" + i, new Vector3(-.09f + i * .028f, RadioHeight * .5f + .004f, .01f), new Vector3(.022f, .008f, .03f), i == 1 ? chrome : leather);
        var antenna = Cylinder(Radio, "Antenna", Vector3.zero, Quaternion.identity, new Vector3(.006f, .27f, .006f), chrome).transform;
        antenna.localRotation = Quaternion.Euler(-12f, 0, 34f);
        antenna.localPosition = new Vector3(-RadioWidth * .5f + .02f, RadioHeight * .5f, .03f) + antenna.localRotation * Vector3.up * .27f;
        Box(Radio, "Strap", new Vector3(0, RadioHeight * .5f + .003f, .03f), new Vector3(RadioWidth * .8f, .004f, .022f),
            Lit("strap", null, null, new Color(.16f, .1f, .06f), .2f));

        // The letter and the photo on the lid.
        var paper = Lit("letter", Load("letter.png", false), null, Color.white, .08f);
        FlatQuad(crate, "Letter", new Vector3(-.17f, crateTop + .002f, -.05f), 18f, new Vector2(.15f, .2f), paper);
        var photo = Lit("photo", LoadPath(photoPath, false), null, Color.white, .25f);
        FlatQuad(crate, "Photo", new Vector3(-.05f, crateTop + .004f, -.135f), -9f, new Vector2(.09f, .12f), photo);

        // The slogan cloth over the radio corner, facing in: tied along a stick from the right-hand logs to a pole
        // planted in the floor. Mirrored in x so it reads from inside.
        var cloth = new GameObject("Slogan").transform;
        cloth.SetParent(Root, false);
        var clothMesh = new Mesh { name = "Slogan" };
        const int nx = 24, ny = 4;
        const float width = .58f, height = .109f, top = .95f, shift = .58f, plane = .22f;
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int j = 0; j <= ny; j++)
        for (int i = 0; i <= nx; i++)
        {
            float u = i / (float)nx, v = j / (float)ny;
            float x = (.5f - u) * width + shift, sag = .03f * (1f - 4f * (u - .5f) * (u - .5f)) * (1f - v * .6f);
            float fold = .006f * Mathf.Sin(u * 19f + v * 2f) * (1f - v);
            vertices.Add(new Vector3(x, top - height + v * height - sag * .4f, plane + fold));
            uvs.Add(new Vector2(u, v));
            if (i < nx && j < ny)
            {
                int a = j * (nx + 1) + i;
                triangles.AddRange(new[] { a, a + nx + 1, a + 1, a + 1, a + nx + 1, a + nx + 2 });
            }
        }
        clothMesh.SetVertices(vertices);
        clothMesh.SetUVs(0, uvs);
        clothMesh.SetTriangles(triangles, 0);
        clothMesh.RecalculateNormals();
        cloth.gameObject.AddComponent<MeshFilter>().sharedMesh = clothMesh;
        cloth.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Lit("slogan", Load("slogan.png", false), null, new Color(.9f, .9f, .9f), .05f);
        var peg = Lit("peg", null, null, new Color(.2f, .15f, .1f), .2f);
        float left = shift - width * .5f - .01f, right = shift + width * .5f + .05f;
        Cylinder(Root, "Stick", new Vector3((left + right) * .5f, top + .006f, plane), Quaternion.Euler(0, 0, 90f), new Vector3(.012f, (right - left) * .5f, .012f), peg);
        Cylinder(Root, "Pole", new Vector3(left, (top + .04f) * .5f, plane), Quaternion.identity, new Vector3(.02f, (top + .04f) * .5f, .02f), peg);

        // A far flash for the mouth, out on the clearing.
        doorFlash = new GameObject("DoorFlash").AddComponent<Light>();
        doorFlash.transform.SetParent(Root, false);
        doorFlash.transform.localPosition = new Vector3(1.5f, 2.5f, -5f);
        doorFlash.type = LightType.Point;
        doorFlash.range = 14f;
        doorFlash.color = new Color(1f, .78f, .55f);
        doorFlash.intensity = 0;
    }

    private void BuildLamp()
    {
        lampPivot = new GameObject("LampPivot").transform;
        lampPivot.SetParent(Root, false);
        lampPivot.localPosition = new Vector3(.2f, Crossing - .04f, 1.2f);
        var tin = Lit("lamp_tin", null, null, new Color(.2f, .24f, .16f), .35f);
        tin.SetFloat("_Metallic", .5f);
        var wire = Lit("lamp_wire", null, null, new Color(.15f, .14f, .12f), .3f);
        Cylinder(lampPivot, "Wire", new Vector3(0, -WireLength * .5f, 0), Quaternion.identity, new Vector3(.003f, WireLength * .5f, .003f), wire);
        var lantern = new GameObject("Lantern").transform;
        lantern.SetParent(lampPivot, false);
        lantern.localPosition = Vector3.down * WireLength;
        Cylinder(lantern, "Cap", new Vector3(0, -.03f, 0), Quaternion.identity, new Vector3(.1f, .018f, .1f), tin);
        Cylinder(lantern, "Tank", new Vector3(0, -.235f, 0), Quaternion.identity, new Vector3(.13f, .03f, .13f), tin);
        foreach (float side in new[] { -1f, 1f })
            Cylinder(lantern, "Tube", new Vector3(side * .066f, -.13f, 0), Quaternion.identity, new Vector3(.009f, .1f, .009f), tin);
        var glass = Lit("lamp_glass", null, null, new Color(1f, .93f, .8f, .18f), .92f);
        Transparent(glass);
        glass.EnableKeyword("_EMISSION");
        glassMaterial = glass;
        var globe = Sphere(lantern, "Globe", new Vector3(0, -.13f, 0), new Vector3(.1f, .15f, .1f), glass);
        globe.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        flameMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "flame" };
        materials.Add(flameMaterial);
        flame = Sphere(lantern, "Flame", new Vector3(0, -.15f, 0), new Vector3(.011f, .028f, .011f), flameMaterial).transform;
        flame.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        lamp = new GameObject("LampLight").AddComponent<Light>();
        lamp.transform.SetParent(lantern, false);
        lamp.transform.localPosition = new Vector3(0, -.13f, 0);
        lamp.type = LightType.Point;
        lamp.color = new Color(1f, .64f, .33f);
        lamp.range = 7f;
        lamp.shadows = LightShadows.Soft;
        lamp.shadowNearPlane = .12f;
        lamp.shadowBias = .02f;
    }

    private void BuildDust()
    {
        var dot = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            float r = new Vector2(x - 31.5f, y - 31.5f).magnitude / 31.5f;
            dot.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1f - r) * Mathf.Clamp01(1f - r)));
        }
        dot.Apply();
        grains = Particles("DustGrains", dot, .55f);
        // Falling grains streak into threads.
        var streak = grains.GetComponent<ParticleSystemRenderer>();
        streak.renderMode = ParticleSystemRenderMode.Stretch;
        streak.velocityScale = .012f;
        streak.lengthScale = 1f;
        haze = Particles("DustHaze", dot, -.002f);
        motes = Particles("DustMotes", dot, 0f);
        var emission = motes.emission;
        emission.rateOverTime = 7f;
        var shape = motes.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(1.6f, 1.2f, Length - .4f);
        shape.position = new Vector3(0, .9f, Length * .5f);
        var main = motes.main;
        main.startLifetime = 9f;
        main.startSize = new ParticleSystem.MinMaxCurve(.002f, .005f);
        main.startSpeed = .01f;
        main.startColor = new Color(.9f, .75f, .55f, .5f);
        main.maxParticles = 200;
        var noise = motes.noise;
        noise.enabled = true;
        noise.strength = .02f;
        noise.frequency = .4f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        motes.Simulate(9f, true, true);
        motes.Play();
        var grow = haze.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, .6f, 1, 1.6f));
        var fade = haze.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(0, 1) });
        fade.color = gradient;
    }

    private ParticleSystem Particles(string name, Texture2D dot, float gravity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(Root, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity;
        main.maxParticles = 4000;
        var emission = ps.emission;
        emission.rateOverTime = 0;
        var shape = ps.shape;
        shape.enabled = false;
        // Unlit: the dust is seen where the lamp catches it, so it is tinted lamp-warm rather than lit.
        var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name };
        material.SetTexture("_BaseMap", dot);
        Transparent(material);
        materials.Add(material);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        ps.Play();
        return ps;
    }

    // ---------------------------------------------------------------- helpers

    private Texture2D Load(string file, bool linear) => LoadPath(Path.Combine(folder, file), linear);

    private static Texture2D LoadPath(string path, bool linear)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = Path.GetFileNameWithoutExtension(path) };
        if (!File.Exists(path) || !texture.LoadImage(File.ReadAllBytes(path)))
        {
            Debug.LogWarning("[ShelterSet] missing texture " + path);
            return null;
        }
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.anisoLevel = 4;
        return texture;
    }

    private Material Lit(string name, Texture albedo, Texture normal, Color tint, float smoothness)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        if (albedo != null) m.SetTexture("_BaseMap", albedo);
        m.SetColor("_BaseColor", tint);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", 0);
        if (normal != null)
        {
            m.SetTexture("_BumpMap", normal);
            m.EnableKeyword("_NORMALMAP");
        }
        materials.Add(m);
        return m;
    }

    private static void Transparent(Material m)
    {
        m.SetFloat("_Surface", 1);
        m.SetFloat("_Blend", 0);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    private GameObject Emit(string name, MeshBuilder builder, Material main, Material second)
    {
        var go = new GameObject(name);
        go.transform.SetParent(Root, false);
        go.AddComponent<MeshFilter>().sharedMesh = builder.ToMesh(name);
        go.AddComponent<MeshRenderer>().sharedMaterials = second != null ? new[] { main, second } : new[] { main };
        return go;
    }

    private static GameObject Primitive(PrimitiveType type, Transform parent, string name, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material) =>
        Primitive(PrimitiveType.Cube, parent, name, position, Quaternion.identity, size, material);

    private static GameObject Sphere(Transform parent, string name, Vector3 position, Vector3 size, Material material) =>
        Primitive(PrimitiveType.Sphere, parent, name, position, Quaternion.identity, size, material);

    /// <summary>A Unity cylinder: <paramref name="size"/> is diameter, half height, diameter.</summary>
    private static GameObject Cylinder(Transform parent, string name, Vector3 position, Quaternion rotation, Vector3 size, Material material) =>
        Primitive(PrimitiveType.Cylinder, parent, name, position, rotation, size, material);

    private static GameObject Cylinder(Transform parent, string name, Vector3 position, Vector3 size, Material material) =>
        Cylinder(parent, name, position, Quaternion.identity, size, material);

    /// <summary>A part of the radio face (rect in face UV, origin bottom left), just proud of the case front.</summary>
    private static void FaceQuad(Transform radio, string name, Rect uv, Material material)
    {
        var b = new MeshBuilder();
        float x0 = (uv.xMin - .5f) * RadioWidth, x1 = (uv.xMax - .5f) * RadioWidth;
        float y0 = (uv.yMin - .5f) * RadioHeight, y1 = (uv.yMax - .5f) * RadioHeight, z = -RadioDepth * .5f - .001f;
        b.Quad(new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z),
            new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMin, uv.yMax));
        var go = new GameObject(name);
        go.transform.SetParent(radio, false);
        go.AddComponent<MeshFilter>().sharedMesh = b.ToMesh(name);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>A sheet lying on a surface, its top edge away from the mouth.</summary>
    private static void FlatQuad(Transform parent, string name, Vector3 position, float yaw, Vector2 size, Material material)
    {
        var b = new MeshBuilder();
        float hx = size.x * .5f, hz = size.y * .5f;
        b.Quad(new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz),
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
        go.AddComponent<MeshFilter>().sharedMesh = b.ToMesh(name);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static Bounds WorldBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>Mesh assembly with two submeshes: the main surface and the log end caps.</summary>
    private sealed class MeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> main = new List<int>(), caps = new List<int>();

        /// <summary>A quad a-b-c-d (clockwise seen from its front), UV in metres over <paramref name="tile"/>.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float tile)
        {
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            Vector3 t = (b - a).normalized, s = Vector3.Cross(n, t);
            Vector2 U(Vector3 p) => new Vector2(Vector3.Dot(p, t), Vector3.Dot(p, s)) / tile;
            Quad(a, b, c, d, U(a), U(b), U(c), U(d));
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d });
            uvs.AddRange(new[] { ua, ub, uc, ud });
            main.AddRange(new[] { i, i + 3, i + 1, i + 1, i + 3, i + 2 });
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, float tile)
        {
            int i = vertices.Count;
            vertices.AddRange(new[] { a, b, c });
            uvs.AddRange(new[] { new Vector2(a.x, a.y) / tile, new Vector2(b.x, b.y) / tile, new Vector2(c.x, c.y) / tile });
            main.AddRange(new[] { i, i + 2, i + 1 });
        }

        /// <summary>A slightly irregular round log from <paramref name="a"/> to <paramref name="b"/>, bark wrapped, ends capped.</summary>
        public void Log(Vector3 a, Vector3 b, float radius, System.Random random)
        {
            const int sides = 12;
            const float barkTile = 1f;
            Vector3 axis = b - a;
            float length = axis.magnitude;
            Vector3 dir = axis / length;
            Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) > .9f ? Vector3.right : Vector3.up).normalized, up = Vector3.Cross(side, dir);
            int rings = Mathf.Max(2, Mathf.CeilToInt(length / .3f));
            float twist = (float)random.NextDouble() * Mathf.PI * 2f, bend = ((float)random.NextDouble() - .5f) * .04f;
            int start = vertices.Count;
            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;
                float rr = radius * (1f + .05f * Mathf.Sin(t * 7f + twist)) * Mathf.Lerp(1.06f, .94f, t);
                Vector3 centre = a + axis * t + side * (bend * Mathf.Sin(t * Mathf.PI));
                for (int k = 0; k <= sides; k++)
                {
                    float angle = k / (float)sides * Mathf.PI * 2f + twist;
                    vertices.Add(centre + (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * rr);
                    uvs.Add(new Vector2(k / (float)sides * 2f * Mathf.PI * radius / barkTile + twist, t * length / barkTile));
                }
            }
            for (int r = 0; r < rings; r++)
            for (int k = 0; k < sides; k++)
            {
                int i0 = start + r * (sides + 1) + k, i1 = i0 + sides + 1;
                main.AddRange(new[] { i0, i1, i0 + 1, i0 + 1, i1, i1 + 1 });
            }
            foreach (int end in new[] { 0, rings })
            {
                int ring = start + end * (sides + 1), centreIndex = vertices.Count;
                Vector3 centre = Vector3.zero;
                for (int k = 0; k < sides; k++) centre += vertices[ring + k];
                vertices.Add(centre / sides);
                uvs.Add(new Vector2(.5f, .5f));
                for (int k = 0; k <= sides; k++)
                {
                    float angle = k / (float)sides * Mathf.PI * 2f;
                    vertices.Add(vertices[ring + k]);
                    uvs.Add(new Vector2(.5f + .5f * Mathf.Cos(angle), .5f + .5f * Mathf.Sin(angle)));
                }
                for (int k = 0; k < sides; k++)
                {
                    int p = centreIndex + 1 + k;
                    if (end == 0) caps.AddRange(new[] { centreIndex, p, p + 1 });
                    else caps.AddRange(new[] { centreIndex, p + 1, p });
                }
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = caps.Count > 0 ? 2 : 1;
            mesh.SetTriangles(main, 0);
            if (caps.Count > 0) mesh.SetTriangles(caps, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
