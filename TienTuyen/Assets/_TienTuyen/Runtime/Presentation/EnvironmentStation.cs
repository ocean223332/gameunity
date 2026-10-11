using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using L = TienTuyen.Presentation.EnvironmentLayout;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// The supply station itself: sandbag emplacements dressing CombatGame's cover
    /// boxes, and the buildings, vehicles and works around the playfield. Every
    /// structure stands more than 4.5 m outside the playfield, beyond the shoulder
    /// camera's 4.3 m trailing distance. Nothing here has a collider.
    /// Buildings, vehicles, bicycles, bags, crates and drums are catalog models (PropLibrary);
    /// the trellis, camouflage net and hammock are built from flora materials and generated cloth
    /// (StationMeshes).
    /// </summary>
    internal static class EnvironmentStation
    {
        private static System.Random random;
        private static Material[] materials;
        private static readonly string[] Bags = { "bag_a", "bag_b", "bag_c", "bag_d" };
        // Catalog sandbag (bag_*), metres: length along X, width along Z, height.
        private const float BagLength = .62f, BagWidth = .37f, BagHeight = .20f;

        public static void Build(Transform root, Transform game, Material ground)
        {
            random = new System.Random(1968);
            var station = new GameObject("Station").transform;
            station.SetParent(root, false);
            var props = new PropBatch(station);
            materials = StationMaterials.Create();
            if (materials == null)
                Debug.LogError("[EnvironmentStation] Missing the flora biome or Resources/Props textures; the trellis, hammock and net are skipped.");
            foreach (Transform child in game)
            {
                if (child.name != "Cover") continue;
                // The cover keeps its collider; the sandbags replace its box renderer.
                var box = child.GetComponent<Renderer>();
                if (Emplacement(props, child) && box != null) box.enabled = false;
            }
            SupplyShed(props, station, ground, -1f, 17.6f);
            Bunker(props, station, ground, -11.5f, 16.2f);
            Watchtower(props, -22.5f, 15.5f);
            CookFire(props, station, 21.5f, 14.5f);
            Depot(props, station);
            Trellis(station);
            Bicycles(props);
            AntiAircraftPit(props, 13.5f, -17.5f);
            TrenchLine(props);
            props.Emit();
        }

        private static float Range(float min, float max) => ProceduralArt.Range(random, min, max);

        private static Matrix4x4 Frame(float x, float z, float yaw, float pitch = 0f, float roll = 0f) =>
            Matrix4x4.TRS(new Vector3(x, L.GroundHeight(x, z), z), Quaternion.Euler(pitch, yaw, roll), Vector3.one);

        private static void Emit(StationMesh mesh, Transform parent, string name)
        {
            if (materials != null) mesh.Emit(parent, name, materials);
        }

        /// <summary>A bamboo culm along a path; its radius and culm (green, straw or grey) vary.</summary>
        private static void Culm(StationMesh mesh, Vector3[] path, float radius, int subdivisions = 1)
        {
            double pick = random.NextDouble();
            var material = pick < .5 ? StationMaterial.Bamboo : pick < .8 ? StationMaterial.BambooTan : StationMaterial.BambooGrey;
            float r = radius * Range(.85f, 1.15f);
            // The culm texture is eight internodes tall and one circumference wide.
            mesh.Tube(material, path, r, 10, Mathf.PI * 2f * r * 8f, subdivisions);
        }

        /// <summary>A catalog model at a local point of a frame, turned by a local yaw.</summary>
        private static void Place(PropBatch props, string model, string part, Matrix4x4 frame, Vector3 local, float yaw = 0f, float sink = 0f)
        {
            var at = frame * Matrix4x4.TRS(local - Vector3.up * sink, Quaternion.Euler(0, yaw, 0), Vector3.one);
            props.Add(model, part, at);
        }

        /// <summary>
        /// A catalog sandbag: <paramref name="size"/> is its length, height and width in metres.
        /// A bag in the bottom course is the ground variant, soaked with laterite at its foot.
        /// </summary>
        private static void SandbagMesh(PropBatch props, Matrix4x4 at, Vector3 size, bool bottom)
        {
            string variant = Bags[random.Next(Bags.Length)] + (bottom ? "_ground" : "");
            props.Add("sandbag", variant, at * Matrix4x4.Scale(new Vector3(size.x / BagLength, size.y / BagHeight, size.z / BagWidth)));
        }

        // ---------- Cover ----------

        /// <summary>
        /// Six staggered courses of bags that fill the cover's collision box (1.1 m high): rows of
        /// bags across its depth, alternate courses laid half a bag over with half bags closing the
        /// ends, each bag scaled to its cell so the outer faces sit on the box faces. Bags enclosed
        /// by others are left out. What you see is what stops bullets.
        /// </summary>
        private static bool Emplacement(PropBatch props, Transform cover)
        {
            const int courses = 6;
            Vector3 size = cover.localScale;
            float course = size.y / courses;
            int rows = Mathf.Max(2, Mathf.RoundToInt(size.z / BagWidth));
            float rowWidth = size.z / rows;
            int bags = Mathf.Max(2, Mathf.RoundToInt(size.x / BagLength));
            float length = size.x / bags;
            var origin = new Vector3(cover.position.x, cover.position.y - size.y * .5f, cover.position.z);
            props.Begin("Cover");
            bool placed = false;
            for (int c = 0; c < courses; c++)
            {
                bool offset = c % 2 == 1, top = c == courses - 1;
                int count = bags + (offset ? 1 : 0);
                for (int row = 0; row < rows; row++)
                {
                    float z = -size.z * .5f + rowWidth * (row + .5f);
                    for (int i = 0; i < count; i++)
                    {
                        float from = Mathf.Max(-size.x * .5f, -size.x * .5f + length * (i - (offset ? .5f : 0f)));
                        float to = Mathf.Min(size.x * .5f, -size.x * .5f + length * (i + 1 - (offset ? .5f : 0f)));
                        if (row > 0 && row < rows - 1 && i > 0 && i < count - 1 && !top) continue;
                        float yaw = (random.NextDouble() < .5 ? 180f : 0f) + Range(-3f, 3f);
                        var at = Matrix4x4.TRS(origin + new Vector3((from + to) * .5f, c * course - .012f, z), Quaternion.Euler(0, yaw, 0), Vector3.one);
                        SandbagMesh(props, at, new Vector3((to - from) * 1.04f, (course + .03f) * Range(.96f, 1f), rowWidth * 1.06f), c == 0);
                        placed = true;
                    }
                }
            }
            return placed;
        }

        // ---------- Buildings ----------

        // shed.fbx: its posts (radius 9 cm) and the edge of its thatch, in its frame.
        private static readonly Vector2[] ShedPosts =
        {
            new Vector2(-4.61f, -1.47f), new Vector2(-1.54f, -1.47f), new Vector2(1.54f, -1.47f), new Vector2(4.62f, -1.47f),
            new Vector2(-4.62f, 1.47f), new Vector2(-1.53f, 1.47f), new Vector2(1.55f, 1.47f), new Vector2(4.62f, 1.47f)
        };
        private static readonly Vector2 ShedEaves = new Vector2(5.14f, 2.29f);
        // The stores' sacks have the cover emplacements' bag size.
        private static readonly Vector3 Sack = new Vector3(.62f, .21f, .40f);
        private const float FootingRadius = .55f;

        /// <summary>Tamped earth heaped round a post's foot, metres above the ground at a distance from its axis.</summary>
        private static float Footing(float r) =>
            r >= FootingRadius ? 0f : .08f * Mathf.Pow(1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.08f, FootingRadius, r)), 2f);

        /// <summary>
        /// Open thatched store on posts over a beaten-earth floor, worked from: rice sacks in a terraced stack
        /// along the back, ammunition crates cross-bonded in courses, fuel drums huddled at the end, and what has
        /// been dragged out and left. The posts stand in tamped earth; the floor, the drip line under the thatch
        /// and the ground under the stores are shaded.
        /// </summary>
        private static void SupplyShed(PropBatch props, Transform parent, Material ground, float x, float z)
        {
            var f = Frame(x, z, 0f);
            props.Begin("SupplyShed");
            Place(props, "shed", "shed", f, Vector3.zero, 0f, .05f);
            // Stores rest on the ground under their own feet, not on the level of the shed's centre.
            Matrix4x4 At(float lx, float y, float lz, float yaw, float pitch = 0f, float roll = 0f)
            {
                Vector3 p = f.MultiplyPoint3x4(new Vector3(lx, 0, lz));
                p.y = L.GroundHeight(p.x, p.z) + y;
                return Matrix4x4.TRS(p, Quaternion.Euler(pitch, yaw, roll), Vector3.one);
            }
            void Bag(float lx, float y, float lz, float yaw, bool bottom, float pitch = 0f, float roll = 0f) =>
                SandbagMesh(props, At(lx + Range(-.025f, .025f), y, lz + Range(-.025f, .025f), yaw + Range(-6f, 6f), pitch + Range(-2f, 2f), roll + Range(-2.5f, 2.5f)),
                    Vector3.Scale(Sack, new Vector3(Range(.95f, 1.05f), Range(.92f, 1.06f), Range(.95f, 1.05f))), bottom);
            void Store(string model, string part, float lx, float y, float lz, float yaw, float pitch = 0f, float roll = 0f) =>
                props.Add(model, part, At(lx, y, lz, yaw, pitch, roll));
            // Ground shading under the stores: centre x, z and half sizes in the shed's frame, and strength.
            var contacts = new List<(Vector4 box, float strength)>();

            // Rice sacks: three rows terraced down to the front, each course laid half a sack over, ragged at the top and the open end.
            float[] rowZ = { 1.05f, .67f, .29f };
            int[] rowCourses = { 6, 5, 3 };
            for (int row = 0; row < 3; row++)
            for (int c = 0; c < rowCourses[row]; c++)
            {
                bool offset = c % 2 == 1, top = c == rowCourses[row] - 1;
                int count = 4 - (offset ? 1 : 0) - (c >= 3 ? 1 : 0);
                for (int i = 0; i < count; i++)
                {
                    if (top && random.NextDouble() < .3) continue;
                    Bag(-4.0f + (i + (offset ? .5f : 0f)) * .6f, c * .19f, rowZ[row], 0f, c == 0);
                }
            }
            contacts.Add((new Vector4(-3.1f, .67f, 1.35f, .62f), .55f));
            // Sacks pulled off the front, one slumped against the stack.
            Bag(-3.3f, 0f, -.22f, 78f, true);
            Bag(-2.75f, 0f, -.3f, 101f, true);
            Bag(-3.0f, .17f, -.27f, 92f, false, 0f, 8f);
            Bag(-2.05f, .12f, .02f, 12f, false, 0f, -24f);
            Bag(-1.25f, 0f, -.05f, 33f, true);
            contacts.Add((new Vector4(-2.5f, -.2f, 1f, .35f), .35f));

            // Ammunition crates cross-bonded: a course across the depth, the next turned along it.
            for (int i = 0; i < 3; i++) Store("crate", "crate", .85f + Range(-.02f, .02f), 0f, .31f + i * .41f, Range(-2f, 2f));
            for (int i = 0; i < 2; i++) Store("crate", "crate", .64f + i * .42f, .35f, .72f + Range(-.02f, .02f), 90f + Range(-2f, 2f));
            for (int i = 0; i < 2; i++) Store("crate", "crate", .85f + Range(-.03f, .03f), .7f, .52f + i * .41f, Range(-3f, 3f));
            Store("crate", "crate", .8f, 1.05f, .74f, 102f);
            contacts.Add((new Vector4(.85f, .72f, .45f, .64f), .5f));
            for (int i = 0; i < 2; i++) Store("crate", "crate", 2.15f + Range(-.03f, .03f), 0f, .42f + i * .41f, Range(-3f, 3f));
            Store("crate", "crate", 2.0f, .35f, .62f, 84f);
            Store("crate", "crate", 1.95f, 0f, -.4f, 14f);
            contacts.Add((new Vector4(2.1f, .3f, .5f, .75f), .45f));

            // Fuel drums huddled at the end, one rolled out on its side and chocked, one left out under the eaves.
            Store("drum", "drum", 3.38f, 0f, .98f, Range(0f, 360f));
            Store("drum", "drum", 4.0f, 0f, 1.03f, Range(0f, 360f));
            Store("drum", "drum", 3.43f, 0f, .35f, Range(0f, 360f));
            Store("drum", "drum", 3.9f, .317f, -.5f, 0f, 0f, 90f);
            props.Add("rock", "rock_c", At(3.75f, -.02f, -.9f, 30f) * Matrix4x4.Scale(Vector3.one * .3f));
            Store("drum", "drum", 4.93f, 0f, -1.95f, Range(0f, 360f), 2.5f, -3f);
            contacts.Add((new Vector4(3.7f, .66f, .62f, .62f), .5f));
            contacts.Add((new Vector4(3.9f, -.5f, .5f, .3f), .4f));
            contacts.Add((new Vector4(4.93f, -1.95f, .33f, .33f), .4f));

            // Left out at the open end: a crate and a heap of sacks.
            Store("crate", "crate", -.6f, 0f, -1.98f, -22f);
            Bag(-5.45f, 0f, .3f, 84f, true);
            Bag(-5.5f, 0f, .95f, 95f, true);
            Bag(-5.47f, .19f, .62f, 70f, false);
            contacts.Add((new Vector4(-.6f, -1.98f, .45f, .3f), .35f));
            contacts.Add((new Vector4(-5.47f, .62f, .3f, .6f), .4f));

            // Packing stones wedged at two posts.
            foreach (var (post, yaw) in new[] { (ShedPosts[1], 40f), (ShedPosts[6], 210f) })
                props.Add("rock", "rock_a", At(post.x + .16f, -.03f, post.y - .1f, yaw) * Matrix4x4.Scale(Vector3.one * .25f));

            ShedGround(parent, ground, f, contacts);
        }

        /// <summary>
        /// The shed's footings, tamped earth round each post in the ground's own material, and its beaten floor:
        /// a multiply shade over the ground, darkest where the thatch hides the sky, along the drip line and under the stores.
        /// </summary>
        private static void ShedGround(Transform parent, Material ground, Matrix4x4 f, List<(Vector4 box, float strength)> contacts)
        {
            float Rise(float lx, float lz)
            {
                float h = 0f;
                foreach (var post in ShedPosts) h = Mathf.Max(h, Footing(Vector2.Distance(new Vector2(lx, lz), post)));
                return h;
            }
            Vector3 Surface(float lx, float lz, float lift)
            {
                Vector3 p = f.MultiplyPoint3x4(new Vector3(lx, 0, lz));
                p.y = L.GroundHeight(p.x, p.z) + Rise(lx, lz) + lift;
                return p;
            }

            // Footings: a ring of earth round each post, its rim tucked under the terrain.
            const int rings = 7, sides = 16;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (var post in ShedPosts)
            {
                int start = vertices.Count;
                for (int r = 0; r <= rings; r++)
                for (int s = 0; s < sides; s++)
                {
                    float radius = Mathf.Lerp(.07f, FootingRadius + .05f, (float)r / rings), a = s * Mathf.PI * 2f / sides;
                    float lx = post.x + Mathf.Cos(a) * radius, lz = post.y + Mathf.Sin(a) * radius;
                    float lumpy = (Mathf.PerlinNoise(lx * 6f, lz * 6f) - .5f) * .03f * (1f - (float)r / rings);
                    vertices.Add(Surface(lx, lz, r == rings ? -.04f : lumpy));
                }
                for (int r = 0; r < rings; r++)
                for (int s = 0; s < sides; s++)
                {
                    int a = start + r * sides + s, b = start + r * sides + (s + 1) % sides;
                    triangles.AddRange(new[] { a, b, a + sides, b, b + sides, a + sides });
                }
            }
            var footings = new Mesh { name = "ShedFootings" };
            footings.SetVertices(vertices);
            footings.SetTriangles(triangles, 0);
            footings.RecalculateNormals();
            footings.RecalculateBounds();
            var go = new GameObject("ShedFootings", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = footings;
            go.GetComponent<MeshRenderer>().sharedMaterial = ground;

            var shade = StationMaterials.GroundShade();
            if (shade == null) return;
            float Box(Vector2 p, Vector2 centre, Vector2 half, float soft)
            {
                float dx = Mathf.Max(0, Mathf.Abs(p.x - centre.x) - half.x), dz = Mathf.Max(0, Mathf.Abs(p.y - centre.y) - half.y);
                return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Sqrt(dx * dx + dz * dz) / soft);
            }
            float Strength(float lx, float lz)
            {
                var p = new Vector2(lx, lz);
                // The thatch keeps the sky off the floor, worn darker along the front where people work.
                float light = 1f - .42f * Box(p, Vector2.zero, ShedEaves - new Vector2(.5f, .5f), .7f);
                light *= 1f - .12f * Box(p, new Vector2(0, -.6f), new Vector2(4f, .5f), .6f) * Mathf.PerlinNoise(lx * .8f, lz * .8f + 3f);
                // Rain off the thatch: a damp line under its edge, patchy where it drips less.
                float drip = Mathf.Max(Mathf.Max(Box(p, new Vector2(0, ShedEaves.y), new Vector2(ShedEaves.x, 0), .22f), Box(p, new Vector2(0, -ShedEaves.y), new Vector2(ShedEaves.x, 0), .22f)),
                    Mathf.Max(Box(p, new Vector2(ShedEaves.x, 0), new Vector2(0, ShedEaves.y), .22f), Box(p, new Vector2(-ShedEaves.x, 0), new Vector2(0, ShedEaves.y), .22f)));
                light *= 1f - .45f * drip * Mathf.Lerp(.5f, 1f, Mathf.PerlinNoise(lx * 2.3f, lz * 2.3f));
                foreach (var post in ShedPosts) light *= 1f - .4f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.09f, .5f, Vector2.Distance(p, post))));
                foreach (var (box, strength) in contacts)
                    light *= 1f - strength * Box(p, new Vector2(box.x, box.y), new Vector2(box.z, box.w), .3f);
                return 1f - light;
            }
            const float cell = .2f;
            Vector2 reach = ShedEaves + new Vector2(.9f, .9f);
            int nx = Mathf.CeilToInt(2f * reach.x / cell), nz = Mathf.CeilToInt(2f * reach.y / cell);
            vertices.Clear();
            triangles.Clear();
            var colors = new List<Color>();
            var uvs = new List<Vector2>();
            var tint = new Color(.6f, .54f, .5f);
            for (int k = 0; k <= nz; k++)
            for (int i = 0; i <= nx; i++)
            {
                float lx = -reach.x + i * cell, lz = -reach.y + k * cell;
                var p = Surface(lx, lz, .02f);
                vertices.Add(p);
                colors.Add(new Color(tint.r, tint.g, tint.b, Strength(lx, lz)));
                uvs.Add(new Vector2(p.x, p.z) * .5f);
            }
            for (int k = 0; k < nz; k++)
            for (int i = 0; i < nx; i++)
            {
                int a = k * (nx + 1) + i, c = a + nx + 1;
                if (Mathf.Max(Mathf.Max(colors[a].a, colors[a + 1].a), Mathf.Max(colors[c].a, colors[c + 1].a)) < .01f) continue;
                triangles.AddRange(new[] { a, c, a + 1, a + 1, c, c + 1 });
            }
            var floor = new Mesh { name = "ShedFloor" };
            floor.SetVertices(vertices);
            floor.SetColors(colors);
            floor.SetUVs(0, uvs);
            floor.SetTriangles(triangles, 0);
            floor.RecalculateNormals();
            floor.RecalculateBounds();
            go = new GameObject("ShedFloor", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = floor;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = shade;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // Bunker earth cover (build_bunker.py mound_height): a dome over a short spine behind the A-frame, cut at
        // the front by a ramp rising from the plank back wall so the rear log pairs run into the slope. Slopes
        // stay under 45 degrees: the ground shader projects its layers from above and stretches on steeper faces.
        private const float MoundTop = 2.1f, MoundSlope = .95f, MoundCrest = .8f, MoundFront = -1.22f;
        private static readonly Vector2 MoundSpine = new Vector2(.9f, 1.6f);

        /// <summary>Height of the bunker's earth cover above the ground at a local point (x, z); zero or less outside it.</summary>
        internal static float MoundHeight(float x, float z)
        {
            float d = Vector2.Distance(new Vector2(x, z), new Vector2(0, Mathf.Clamp(z, MoundSpine.x, MoundSpine.y)));
            float dome = MoundTop - MoundSlope * (d < MoundCrest ? d * d / (2f * MoundCrest) : d - MoundCrest * .5f);
            return Mathf.Min(dome, MoundSlope * (z - MoundFront));
        }

        /// <summary>A-frame log shelter (mouth towards -Z) half under an earth mound, with sandbag cheeks.</summary>
        private static void Bunker(PropBatch props, Transform parent, Material ground, float x, float z)
        {
            var f = Frame(x, z, 6f);
            props.Begin("Bunker");
            Place(props, "bunker", "bunker", f, Vector3.zero);
            // Cheeks: three courses of bags laid lengthwise beside the mouth.
            for (int course = 0; course < 3; course++)
            for (int k = 0; k < 2 - course / 2; k++)
            foreach (float side in new[] { -1f, 1f })
            {
                var at = f * Matrix4x4.TRS(new Vector3(side * (1.62f - course * .08f), course * .19f, -1.95f + k * .62f + (course % 2) * .2f),
                    Quaternion.Euler(0, -90f + Range(-5f, 5f), 0), Vector3.one);
                SandbagMesh(props, at, new Vector3(BagLength, BagHeight, BagWidth), course == 0);
            }
            Mound(parent, f, ground);
            var fern = StationMaterials.FloraPrefab("FernSmall");
            var openGround = new Dictionary<Material, Material>();
            for (int i = 0; i < 9; i++)
            {
                // One fern per strip across the mound, so they spread over both flanks.
                float lx = Mathf.Lerp(-1.8f, 1.8f, (i + Range(.15f, .85f)) / 9f), lz = Range(.4f, 2.8f);
                var p = f.MultiplyPoint3x4(new Vector3(lx, 0, lz));
                p.y = L.GroundHeight(p.x, p.z) + MoundHeight(lx, lz) - .08f;
                float scale = Range(.8f, 1.25f), yaw = Range(0f, 360f);
                if (fern == null) continue;
                var plant = Object.Instantiate(fern, p, Quaternion.Euler(0, yaw, 0), parent);
                plant.name = "MoundFern";
                plant.transform.localScale *= scale;
                foreach (var renderer in plant.GetComponentsInChildren<Renderer>())
                {
                    var shared = renderer.sharedMaterials;
                    for (int m = 0; m < shared.Length; m++)
                    {
                        if (!openGround.TryGetValue(shared[m], out var open))
                            openGround[shared[m]] = open = StationMaterials.OpenGroundFern(shared[m]);
                        shared[m] = open;
                    }
                    renderer.sharedMaterials = shared;
                }
            }
        }

        /// <summary>The mound as a heightfield draped on the terrain, in the terrain's own ground material.</summary>
        private static void Mound(Transform parent, Matrix4x4 f, Material ground)
        {
            const float cell = .15f;
            float reach = MoundTop / MoundSlope + MoundCrest * .5f;
            float z0 = MoundFront - cell;
            int nx = Mathf.CeilToInt(2f * reach / cell) + 1, nz = Mathf.CeilToInt((MoundSpine.y + reach - z0) / cell) + 1;
            var vertices = new Vector3[(nx + 1) * (nz + 1)];
            var heights = new float[vertices.Length];
            for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float lx = -reach + i * cell, lz = z0 + j * cell;
                float h = MoundHeight(lx, lz);
                Vector3 p = f.MultiplyPoint3x4(new Vector3(lx, 0, lz));
                // Outside the footprint the skirt tucks under the terrain.
                p.y = L.GroundHeight(p.x, p.z) + (h > 0 ? h - .03f : -.2f);
                vertices[j * (nx + 1) + i] = p;
                heights[j * (nx + 1) + i] = h;
            }
            var triangles = new List<int>();
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, c = a + nx + 1;
                if (Mathf.Max(Mathf.Max(heights[a], heights[a + 1]), Mathf.Max(heights[c], heights[c + 1])) <= 0) continue;
                triangles.AddRange(new[] { a, c, a + 1, a + 1, c, c + 1 });
            }
            var mesh = new Mesh { name = "BunkerMound", vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("BunkerMound");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ground;
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }

        /// <summary>Log lookout tower with a thatched shelter: the tallest structure of the station (ladder on -Z).</summary>
        private static void Watchtower(PropBatch props, float x, float z)
        {
            var f = Frame(x, z, 18f);
            props.Begin("Watchtower");
            Place(props, "watchtower", "watchtower", f, Vector3.zero, 0f, .1f);
            Place(props, "rock", "rock_b", f, new Vector3(1.9f, 0, .6f), 40f, .06f);
            Place(props, "rock", "rock_a", f, new Vector3(-1.7f, 0, 1.4f), 160f, .04f);
        }

        /// <summary>Stone fire ring with a tripod and pot and two log seats (one model), and a hammock slung between posts.</summary>
        private static void CookFire(PropBatch props, Transform station, float x, float z)
        {
            var f = Frame(x, z, 0f);
            props.Begin("CookFire");
            Place(props, "cookfire", "cookfire", f, Vector3.zero);
            var h = Frame(x + 3.2f, z + .6f, 80f);
            var mesh = new StationMesh();
            foreach (float end in new[] { -1f, 1f })
            {
                Vector3 top = h.MultiplyPoint3x4(new Vector3(end * 1.6f, 1.75f, 0));
                var foot = new Vector3(top.x, L.GroundHeight(top.x, top.z) - .15f, top.z);
                mesh.Tube(StationMaterial.Wood, new[] { foot, top + h.MultiplyVector(new Vector3(end * Range(0f, .06f), 0, Range(-.04f, .04f))) }, .08f, 9, 1.5f);
                // Rope from the post to the gathered end of the cloth.
                mesh.Tube(StationMaterial.Wood, new[] { h.MultiplyPoint3x4(new Vector3(end * 1.56f, 1.62f, 0)), h.MultiplyPoint3x4(new Vector3(end * 1.28f, 1.46f, 0)) }, .012f, 5, .4f);
            }
            // Parachute-nylon hammock: gathered at the ends, sagging in the middle, its sides curling up.
            const int rows = 15, columns = 7;
            var grid = new Vector3[rows, columns];
            var uv = new Vector2[rows, columns];
            for (int i = 0; i < rows; i++)
            {
                float t = (float)i / (rows - 1), sag = Mathf.Sin(t * Mathf.PI), lx = Mathf.Lerp(-1.3f, 1.3f, t);
                float half = .46f * Mathf.Pow(Mathf.Max(sag, 0f), .6f) + .02f;
                for (int j = 0; j < columns; j++)
                {
                    float s = (float)j / (columns - 1) * 2f - 1f;
                    grid[i, j] = h.MultiplyPoint3x4(new Vector3(lx, 1.46f - sag * .7f + s * s * .16f * sag, s * half));
                    uv[i, j] = new Vector2(lx, s * half) / .5f;
                }
            }
            mesh.Sheet(StationMaterial.Canvas, grid, uv, .15f);
            Emit(mesh, station, "Hammock");
        }

        // ---------- Vehicles and depot ----------

        // Catalog trucks face along their own axes: the ZIL-157 cab is towards -Z, the wreck's towards +X.
        // These turns bring the cab to the frame's +Z.
        private const float ZilYaw = 180f, WreckYaw = -90f;

        // Where each truck's tyres (the wreck's rims) touch the ground, in its frame with the cab towards +Z.
        private static readonly Vector2[] ZilWheels =
            { new Vector2(-1f, 2.4f), new Vector2(1f, 2.4f), new Vector2(-1f, -1.1f), new Vector2(1f, -1.1f), new Vector2(-1f, -2.4f), new Vector2(1f, -2.4f) };
        private static readonly Vector2[] WreckWheels =
            { new Vector2(-.5f, 2.2f), new Vector2(.5f, 2.2f), new Vector2(-.5f, -2.5f), new Vector2(.5f, -2.5f) };

        /// <summary>
        /// A vehicle's frame resting on the terrain: the plane through the ground under its wheels sets its
        /// pitch and roll, and it settles until no wheel is off the ground, the wheel that would hang highest
        /// <paramref name="sink"/> into the soft earth; where the ground twists, the others sink deeper.
        /// </summary>
        private static Matrix4x4 Rest(float x, float z, float yaw, Vector2[] wheels, float sink)
        {
            var turn = Quaternion.Euler(0, yaw, 0);
            // Least squares h = a + b lx + c lz over the contacts.
            float n = wheels.Length, sx = 0, sz = 0, sxx = 0, sxz = 0, szz = 0, sh = 0, sxh = 0, szh = 0;
            foreach (var w in wheels)
            {
                Vector3 p = new Vector3(x, 0, z) + turn * new Vector3(w.x, 0, w.y);
                float h = L.GroundHeight(p.x, p.z);
                sx += w.x; sz += w.y; sxx += w.x * w.x; sxz += w.x * w.y; szz += w.y * w.y;
                sh += h; sxh += w.x * h; szh += w.y * h;
            }
            var m = new Matrix4x4(new Vector4(n, sx, sz, 0), new Vector4(sx, sxx, sxz, 0), new Vector4(sz, sxz, szz, 0), new Vector4(0, 0, 0, 1));
            Vector3 fit = m.inverse.MultiplyVector(new Vector3(sh, sxh, szh));
            var up = turn * new Vector3(-fit.y, 1f, -fit.z).normalized;
            var rotation = Quaternion.FromToRotation(Vector3.up, up) * turn;
            float lift = float.MaxValue;
            foreach (var w in wheels)
            {
                Vector3 p = new Vector3(x, 0, z) + rotation * new Vector3(w.x, 0, w.y);
                lift = Mathf.Min(lift, L.GroundHeight(p.x, p.z) - p.y);
            }
            return Matrix4x4.TRS(new Vector3(x, lift - sink, z), rotation, Vector3.one);
        }

        private static void Depot(PropBatch props, Transform station)
        {
            props.Begin("DepotTruck");
            var truck = Rest(-27.5f, -12f, 72f, ZilWheels, .02f);
            Place(props, "truck_zil", "truck_zil", truck, Vector3.zero, ZilYaw);
            CamouflageNet(station, truck);
            var drums = Frame(-21.2f, -13.6f, 20f);
            props.Begin("DepotDrums");
            for (int i = 0; i < 6; i++)
                Place(props, "drum", "drum", drums, new Vector3((i % 3) * .64f, 0, (i / 3) * .64f), Range(0f, 360f));
            // One drum on its side, its axis along the frame's X.
            props.Add("drum", "drum", drums * Matrix4x4.TRS(new Vector3(-.45f, .317f, .3f), Quaternion.Euler(0, 0, 90f), Vector3.one));
            for (int i = 0; i < 2; i++)
                Place(props, "crate", "crate", drums, new Vector3(2.2f + i * .9f, 0, -.2f), Range(-8f, 8f));
            // A second truck pulling away up the western road.
            props.Begin("RoadTruck");
            Place(props, "truck_zil", "truck_zil", Rest(-47f, 2.6f, -58f, ZilWheels, .02f), Vector3.zero, ZilYaw);
            // The burnt-out wreck on the slope below the station, settled on its rims.
            props.Begin("WreckTruck");
            Place(props, "truck", "truck", Rest(-19.5f, -31.5f, 128f, WreckWheels, .06f), Vector3.zero, WreckYaw);
        }

        /// <summary>
        /// Lowest a cloth thrown over the ZIL can lie at a point of its frame (cab towards +Z): just over the canvas on the
        /// cargo bows, the cab roof and the bonnet, falling away from their edges.
        /// </summary>
        private static float OverZil(float x, float z)
        {
            float Block(float z0, float z1, float top)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x) - 1.25f), dz = Mathf.Max(0, Mathf.Max(z0 - z, z - z1));
                return top + .08f - 1.1f * Mathf.Sqrt(dx * dx + dz * dz);
            }
            return Mathf.Max(Block(-3.55f, .75f, 3f), Mathf.Max(Block(.75f, 1.95f, 2.42f), Block(1.95f, 3.55f, 1.85f)));
        }

        /// <summary>
        /// Camouflage netting garnished with cut foliage over the depot truck: a ridge on two bamboo poles sags
        /// onto the cargo canvas, the sides fall to a hem staked near the ground between pegs, the back end
        /// comes down low and the front is held up on two poles so the truck can drive out.
        /// </summary>
        private static void CamouflageNet(Transform station, Matrix4x4 truck)
        {
            const float halfWidth = 3.75f, halfLength = 5.25f, pole = 4.4f, ridge = 3.3f, sag = .25f, shoulder = 1.1f, pegSpan = 3.5f;
            const float frontHem = 2.55f, backHem = .45f, rolledHem = 1.55f;
            const int nx = 30, nz = 42;
            float Square(float v) => v * v;
            // Height of the net above the ground at a point of the truck's frame, before it drapes over the truck:
            // a crown as wide as the truck along the sagging ridge, then fairly taut down to the hem. The skirts are
            // pegged down over the cargo and hitched up towards the cab, the front an awning over the bonnet, so the
            // truck is hidden from the air and still ready to drive out.
            float Height(float lx, float lz)
            {
                float s = Mathf.Abs(lx) / halfWidth, zc = Mathf.Clamp(lz, -pole, pole);
                float top = ridge - sag * (1f - zc * zc / (pole * pole));
                float crown = top - .18f * Square(Mathf.Min(Mathf.Abs(lx), shoulder) / shoulder);
                float hem = Mathf.Lerp(.2f + .4f * Square(Mathf.Sin(Mathf.PI * (lz + halfLength) / pegSpan)), rolledHem,
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.8f, 2.2f, lz)));
                float h = Mathf.Lerp(crown, hem, Mathf.Pow(Mathf.Max(0, Mathf.Abs(lx) - shoulder) / (halfWidth - shoulder), 1.25f));
                float end = Mathf.Clamp01((Mathf.Abs(lz) - pole) / (halfLength - pole));
                h = Mathf.Lerp(h, Mathf.Min(h, lz > 0 ? frontHem : backHem), Mathf.Pow(end, 1.3f));
                return Mathf.Max(.1f, h + (Mathf.PerlinNoise(lx * .9f + 3.1f, lz * .9f + 7.7f) - .5f) * .2f * s);
            }
            Vector3 Point(float lx, float lz)
            {
                Vector3 p = truck.MultiplyPoint3x4(new Vector3(lx, 0, lz));
                p.y = Mathf.Max(L.GroundHeight(p.x, p.z) + Height(lx, lz), truck.MultiplyPoint3x4(new Vector3(lx, OverZil(lx, lz), lz)).y);
                return p;
            }

            var mesh = new StationMesh();
            var grid = new Vector3[nz + 1, nx + 1];
            var uv = new Vector2[nz + 1, nx + 1];
            for (int k = 0; k <= nz; k++)
            for (int i = 0; i <= nx; i++)
                grid[k, i] = Point(((float)i / nx * 2 - 1) * halfWidth, ((float)k / nz * 2 - 1) * halfLength);
            // The hem is not cut straight: here and there a loose piece of net hangs lower off it.
            void Hang(int k, int i, int inK, int inI, float along, float seed)
            {
                float drop = .38f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.5f, .8f, Mathf.PerlinNoise(along * 1.9f + seed, seed)));
                for (int row = 0; row < 2; row++, k += inK, i += inI)
                {
                    Vector3 p = grid[k, i];
                    p.y = Mathf.Max(L.GroundHeight(p.x, p.z) + .04f, p.y - drop * (row == 0 ? 1f : .4f));
                    grid[k, i] = p;
                }
            }
            for (int i = 0; i <= nx; i++) { Hang(0, i, 1, 0, i * .25f, 11.3f); Hang(nz, i, -1, 0, i * .25f, 23.9f); }
            for (int k = 1; k < nz; k++) { Hang(k, 0, 0, 1, k * .25f, 37.1f); Hang(k, nx, 0, -1, k * .25f, 51.7f); }
            // The net texture tiles once a metre along the cloth, down the skirts too.
            for (int k = 0; k <= nz; k++)
            for (int i = 0; i <= nx; i++)
                uv[k, i] = new Vector2(i == 0 ? 0 : uv[k, i - 1].x + Vector3.Distance(grid[k, i - 1], grid[k, i]),
                                       k == 0 ? 0 : uv[k - 1, i].y + Vector3.Distance(grid[k - 1, i], grid[k, i]));
            mesh.Sheet(StationMaterial.Net, grid, uv, .1f);

            Vector3 axis = truck.MultiplyPoint3x4(new Vector3(0, 1.5f, 0));
            // Fresh green fronds catch the eye, so the skirts carry mostly wilted ones that blend into the net.
            StationMaterial Foliage(float steep)
            {
                double kind = random.NextDouble() * (1 + steep);
                return kind < .35 ? StationMaterial.Frond : kind < .5 ? StationMaterial.Banana : StationMaterial.DriedFrond;
            }
            // Cut foliage tucked into the mesh in sprigs of a few leaves, lying on the cloth down its slope with the tips
            // curling off it; sparser and shaded on the skirts, where single flat leaves would read as stickers.
            for (int k = 0; k < nz; k++)
            for (int i = 0; i < nx; i++)
            {
                Vector3 a = grid[k, i], b = grid[k, i + 1], c = grid[k + 1, i], d = grid[k + 1, i + 1];
                Vector3 normal = Vector3.Cross(c - a, b - a);
                float area = normal.magnitude;
                normal.Normalize();
                Vector3 centre = (a + b + c + d) * .25f;
                if (Vector3.Dot(normal, centre - axis) < 0) normal = -normal;
                float steep = 1f - Mathf.Abs(normal.y);
                for (int sprig = 0; sprig < (int)(area * Mathf.Lerp(.7f, .35f, steep) + random.NextDouble()); sprig++)
                {
                    Vector3 p = Vector3.Lerp(Vector3.Lerp(a, b, Range(0, 1)), Vector3.Lerp(c, d, Range(0, 1)), Range(0, 1)) + normal * .03f;
                    Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, normal), along = (b - a).normalized;
                    if (downhill.sqrMagnitude < .01f) downhill = Vector3.Cross(normal, along) * (random.NextDouble() < .5 ? 1 : -1);
                    var heading = Vector3.ProjectOnPlane(downhill.normalized * Range(.4f, 1f) + along * Range(-.8f, .8f), normal).normalized;
                    var material = Foliage(steep);
                    float size = Range(.4f, .7f), shade = Mathf.Lerp(1f, .6f, steep) * Range(.85f, 1f);
                    for (int leaf = 0, leaves = 2 + random.Next(2); leaf < leaves; leaf++)
                    {
                        var dir = Quaternion.AngleAxis(Range(-35f, 35f) * (1f + 3f * steep), normal) * heading;
                        mesh.Card(material, p + dir * Range(-.05f, .08f), dir, size * Range(.6f, 1f), normal * Range(.02f, .14f),
                            shade, Range(0f, 1f), Vector3.Cross(normal, dir));
                    }
                }
            }
            // Sprigs hanging off the hem break up its line.
            void Fringe(int k0, int i0, int dk, int di, int count)
            {
                for (int n = 0; n < count; n++)
                {
                    int k = k0 + dk * n, i = i0 + di * n;
                    if (random.NextDouble() < .85) continue;
                    Vector3 p = grid[k, i], next = grid[Mathf.Min(k + Mathf.Abs(dk), nz), Mathf.Min(i + Mathf.Abs(di), nx)];
                    Vector3 outward = p - axis;
                    outward.y = 0;
                    outward.Normalize();
                    var dir = (Vector3.down + outward * Range(.1f, .4f)).normalized;
                    mesh.Card(Foliage(1f), p + outward * .03f, dir, Mathf.Min(Range(.3f, .55f), p.y - L.GroundHeight(p.x, p.z) + .1f),
                        outward * Range(.05f, .2f), .9f, Range(0f, 1f), (next - p).normalized);
                }
            }
            Fringe(0, 0, 0, 1, nx);
            Fringe(nz, 0, 0, 1, nx);
            Fringe(0, 0, 1, 0, nz);
            Fringe(0, nx, 1, 0, nz);

            // Ridge poles under the crown with a culm along the ridge, front poles holding up the hem, and stakes at the pegs.
            Vector3 Foot(Vector3 top, float depth) => new Vector3(top.x, L.GroundHeight(top.x, top.z) - depth, top.z);
            var ridgeLine = new List<Vector3>();
            for (int k = 0; k <= 4; k++) ridgeLine.Add(Point(0, Mathf.Lerp(-pole, pole, k / 4f)) + Vector3.down * .06f);
            Culm(mesh, ridgeLine.ToArray(), .045f, 2);
            foreach (float z in new[] { -pole, pole })
            {
                Vector3 top = Point(0, z) + Vector3.down * .04f;
                Culm(mesh, new[] { Foot(top, .15f), top }, .065f);
            }
            foreach (float x in new[] { -1.6f, 1.6f })
            {
                Vector3 top = Point(x, halfLength - .1f) + Vector3.down * .03f;
                Culm(mesh, new[] { Foot(top, .15f), top }, .05f);
            }
            // Pegged where the skirt reaches the ground, propped on a forked stick where it is hitched up.
            for (float z = -halfLength; z <= halfLength + .01f; z += pegSpan)
            foreach (float x in new[] { -halfWidth, halfWidth })
            {
                Vector3 hem = Point(x, z), lean = truck.MultiplyVector(new Vector3(Mathf.Sign(x) * .1f, 0, 0));
                if (hem.y - L.GroundHeight(hem.x, hem.z) > .8f) Culm(mesh, new[] { Foot(hem, .1f) + lean, hem + Vector3.up * .05f }, .035f);
                else mesh.Tube(StationMaterial.Wood, new[] { Foot(hem, .1f) + lean, hem + Vector3.up * .12f }, .025f, 6, .6f);
            }
            Emit(mesh, station, "CamouflageNet");
        }

        // ---------- The trail east ----------

        /// <summary>Samples the road at an arc length from its start.</summary>
        private static Vector2 RoadAt(float distance, out Vector2 tangent)
        {
            var road = L.Road;
            for (int i = 0; i < road.Length - 1; i++)
            {
                float segment = Vector2.Distance(road[i], road[i + 1]);
                if (distance <= segment || i == road.Length - 2)
                {
                    tangent = (road[i + 1] - road[i]).normalized;
                    return road[i] + tangent * distance;
                }
                distance -= segment;
            }
            tangent = Vector2.right;
            return road[road.Length - 1];
        }

        private static float RoadLengthToX(float x)
        {
            float length = 0;
            var road = L.Road;
            for (int i = 0; i < road.Length - 1; i++)
            {
                if (road[i + 1].x >= x)
                    return length + (x - road[i].x) / (road[i + 1].x - road[i].x) * Vector2.Distance(road[i], road[i + 1]);
                length += Vector2.Distance(road[i], road[i + 1]);
            }
            return length;
        }

        /// <summary>Bamboo arches roofed with cut foliage hide the trail from the air as it leaves the station.</summary>
        private static void Trellis(Transform station)
        {
            var mesh = new StationMesh();
            float start = RoadLengthToX(22f), end = RoadLengthToX(52f);
            const float halfWidth = 2.9f, wall = 3.5f, crown = 4.6f;
            Vector3 previousLeft = Vector3.zero, previousRight = Vector3.zero, previousApex = Vector3.zero;
            bool first = true;
            for (float s = start; s <= end; s += 2.3f)
            {
                Vector2 c = RoadAt(s, out Vector2 t);
                var side = new Vector2(-t.y, t.x);
                Vector2 l2 = c + side * halfWidth, r2 = c - side * halfWidth;
                float gl = L.GroundHeight(l2.x, l2.y), gr = L.GroundHeight(r2.x, r2.y), gc = L.GroundHeight(c.x, c.y);
                var left = new Vector3(l2.x, gl + wall, l2.y);
                var right = new Vector3(r2.x, gr + wall, r2.y);
                var apex = new Vector3(c.x, gc + crown, c.y);
                // Posts and arch, each cut from a different culm.
                Culm(mesh, new[] { new Vector3(l2.x, gl - .2f, l2.y), left }, .07f);
                Culm(mesh, new[] { new Vector3(r2.x, gr - .2f, r2.y), right }, .07f);
                Culm(mesh, new[] { left, Vector3.Lerp(left, apex, .5f) + Vector3.up * .25f, apex, Vector3.Lerp(right, apex, .5f) + Vector3.up * .25f, right }, .055f, 4);
                if (!first)
                {
                    // Ridge pole and wall plates tie the arches together.
                    Culm(mesh, new[] { previousApex, apex }, .045f);
                    Culm(mesh, new[] { previousLeft, left }, .04f);
                    Culm(mesh, new[] { previousRight, right }, .04f);
                    // Leaf thatch over the arch, with gaps where the sun breaks through.
                    for (int strip = 0; strip < 4; strip++)
                    {
                        float a0 = strip / 4f, a1 = (strip + 1) / 4f;
                        Vector3 P(Vector3 l, Vector3 ap, Vector3 r, float u) => u < .5f
                            ? Vector3.Lerp(l, ap, u * 2f) + Vector3.up * Mathf.Sin(u * 2f * Mathf.PI) * .2f
                            : Vector3.Lerp(ap, r, u * 2f - 1f) + Vector3.up * Mathf.Sin((u * 2f - 1f) * Mathf.PI) * .2f;
                        if (random.NextDouble() < .12) continue;
                        Vector3 q0 = P(previousLeft, previousApex, previousRight, a0) + Vector3.up * .08f, q1 = P(left, apex, right, a0) + Vector3.up * .08f;
                        Vector3 q2 = P(left, apex, right, a1) + Vector3.up * .08f, q3 = P(previousLeft, previousApex, previousRight, a1) + Vector3.up * .08f;
                        // Shingled down the slope from the crown, turned a little along the trail.
                        Vector3 downhill = (strip < 2 ? q0 - q3 : q3 - q0).normalized, along = (q1 - q0).normalized;
                        for (int leaf = 0; leaf < 14; leaf++)
                        {
                            Vector3 root = Vector3.Lerp(Vector3.Lerp(q0, q1, Range(0, 1)), Vector3.Lerp(q3, q2, Range(0, 1)), Range(0, 1))
                                - downhill * .3f + Vector3.up * Range(.05f, .25f);
                            var dir = (downhill + along * Range(-.7f, .7f)).normalized;
                            double kind = random.NextDouble();
                            var material = kind < .45 ? StationMaterial.Frond : kind < .8 ? StationMaterial.Banana : StationMaterial.DriedFrond;
                            mesh.Card(material, root, dir, kind < .45 || kind >= .8 ? Range(1.3f, 1.9f) : Range(1f, 1.5f),
                                Vector3.down * Range(.1f, .35f), 1f, Range(0f, 1f));
                        }
                    }
                    // Fronds tied along the wall plates hang down the sides, tips flaring out.
                    foreach (var pair in new[] { (previousLeft, left), (previousRight, right) })
                    {
                        Vector3 along = (pair.Item2 - pair.Item1).normalized;
                        Vector3 outward = pair.Item1 - previousApex;
                        outward.y = 0f;
                        outward.Normalize();
                        for (int k = 0; k < 12; k++)
                        {
                            Vector3 root = Vector3.Lerp(pair.Item1, pair.Item2, Range(0f, 1f)) + outward * .08f + Vector3.up * .15f;
                            var dir = (Vector3.down + outward * Range(.15f, .4f) + along * Range(-.25f, .25f)).normalized;
                            mesh.Card(k % 3 == 0 ? StationMaterial.DriedFrond : StationMaterial.Frond, root, dir, Range(1.6f, 2.6f),
                                outward * Range(.2f, .45f), .85f, Range(0f, 1f));
                        }
                    }
                }
                previousLeft = left; previousRight = right; previousApex = apex;
                first = false;
            }
            Emit(mesh, station, "Trellis");
        }

        /// <summary>Loaded utility bicycles parked on their stands at the trellis.</summary>
        private static void Bicycles(PropBatch props)
        {
            props.Begin("Bicycles");
            var spots = new[] { new Vector3(22.3f, 8.9f, 196f), new Vector3(24f, 9.4f, 200f), new Vector3(25.7f, 9.8f, 192f) };
            foreach (var spot in spots)
                Place(props, "bicycle", "bicycle", Frame(spot.x, spot.y, spot.z), Vector3.zero, 0f, .02f);
        }

        // ---------- Southern works ----------

        /// <summary>Circular sandbag pit with a DShK on its anti-aircraft tripod raised towards the low sun.</summary>
        private static void AntiAircraftPit(PropBatch props, float x, float z)
        {
            var f = Frame(x, z, 0f);
            props.Begin("AntiAircraftPit");
            const int bags = 22;
            for (int course = 0; course < 3; course++)
            for (int i = 0; i < bags; i++)
            {
                float a = (i + course * .5f) * Mathf.PI * 2 / bags;
                // Gap towards the station for the crew.
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 90f)) < 20f) continue;
                Vector3 p = f.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * 2.5f, 0, Mathf.Sin(a) * 2.5f));
                p.y = L.GroundHeight(p.x, p.z) + course * .26f - .02f;
                var at = Matrix4x4.TRS(p, Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90f + Range(-4f, 4f), 0), Vector3.one);
                SandbagMesh(props, at, new Vector3(.7f, .3f, .42f), course == 0);
            }
            Place(props, "aa_gun", "aa_gun", f, Vector3.zero, 215f, .05f);
            for (int i = 0; i < 2; i++)
                Place(props, "crate", "crate", f, new Vector3(-1.0f + i * .9f, 0, -1.3f), 30f + Range(-6f, 6f));
        }

        /// <summary>
        /// A low parapet along the southern lip of the plateau, beyond the camera's
        /// reach behind the player; gaps where paths go down.
        /// </summary>
        private static void TrenchLine(PropBatch props)
        {
            var points = new[] { new Vector2(-17.5f, -14.7f), new Vector2(-12f, -15.4f), new Vector2(-7f, -14.6f), new Vector2(-2.5f, -15.3f), new Vector2(3f, -14.7f), new Vector2(8.5f, -15.3f) };
            for (int i = 0; i < points.Length - 1; i++)
            {
                if (i == 2) continue; // where the footpath goes down the slope
                Vector2 a = points[i], c = points[i + 1];
                float yaw = -Mathf.Atan2(c.y - a.y, c.x - a.x) * Mathf.Rad2Deg;
                float length = Vector2.Distance(a, c);
                int count = Mathf.RoundToInt(length / .64f);
                props.Begin("TrenchLine");
                for (int course = 0; course < 2; course++)
                for (int k = 0; k < count - course; k++)
                {
                    Vector2 p = Vector2.Lerp(a, c, (k + .5f + course * .5f) / count);
                    var at = Matrix4x4.TRS(new Vector3(p.x, L.GroundHeight(p.x, p.y) + course * .25f - .02f, p.y),
                        Quaternion.Euler(0, yaw + Range(-4f, 4f), 0), Vector3.one);
                    SandbagMesh(props, at, new Vector3(.66f, .3f, .42f), course == 0);
                }
            }
        }
    }
}
