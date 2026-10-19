using System;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// Where a bullet struck the world, and what it leaves there: a puff of the surface's dust, a few chips and a mark
    /// that lasts. The point is found as the resolver stops a round, against the cover boxes (DuelArenaLayout.RayHitsCover)
    /// within the gun's range sphere; the ground, which the resolver never tests, is met here as well. The arena has no
    /// colliders, so nothing here uses physics. Marks are projected decals on the arena's receiver layer, so they never
    /// land on a body. Run with -duelAudioLog to log every impact.
    /// </summary>
    internal sealed class DuelImpacts
    {
        /// <summary>Marks alive at once; past this the oldest is taken for the next (a long firefight cycles them).</summary>
        public const int Capacity = 48;
        private const float Lifetime = 30f, FadeSeconds = 6f;
        private const uint ReceiverLayer = 2;   // the bit DuelStage sets on the arena's renderers
        private const float GroundStep = .5f;
        private static readonly bool LogEvents = Array.IndexOf(Environment.GetCommandLineArgs(), "-duelAudioLog") >= 0;

        // The arena's own colours: laterite for the floor and the bags' fill, burlap, broken concrete, soot.
        private static readonly Color Laterite = new Color(.62f, .42f, .29f, .55f), Burlap = new Color(.52f, .45f, .35f, .5f),
            Concrete = new Color(.66f, .64f, .6f, .5f), Brick = new Color(.6f, .38f, .3f, .5f), Soot = new Color(.3f, .29f, .28f, .45f);

        private struct Mark
        {
            public DecalProjector projector;
            public float age;
        }

        private readonly Transform root;
        private readonly Mark[] marks = new Mark[Capacity];
        private readonly ParticleSystem dust, chips, sparks;
        private readonly System.Random random = new System.Random(1968);
        private int next;

        public DuelImpacts(Transform parent)
        {
            root = new GameObject("DuelImpacts").transform;
            root.SetParent(parent, false);
            var material = new Material(Shader.Find("Shader Graphs/Decal")) { name = "ImpactMarks", enableInstancing = true };
            material.SetTexture("Base_Map", MarkAtlas());
            material.SetFloat("Normal_Blend", 0);
            for (int i = 0; i < marks.Length; i++)
            {
                var node = new GameObject("ImpactMark");
                node.transform.SetParent(root, false);
                var projector = node.AddComponent<DecalProjector>();
                projector.material = material;
                projector.renderingLayerMask = ReceiverLayer;
                projector.pivot = Vector3.zero;
                projector.drawDistance = 45f;
                projector.fadeScale = .8f;
                projector.startAngleFade = 55;
                projector.endAngleFade = 80;
                projector.uvScale = Vector2.one * .5f;
                projector.enabled = false;
                marks[i] = new Mark { projector = projector, age = -1 };
            }
            dust = Particles("ImpactDust", 400, -.05f, false, Color.white);
            chips = Particles("ImpactChips", 300, 2.2f, false, Color.white);
            sparks = Particles("ImpactSparks", 120, 1.4f, true, new Color(3.2f, 2f, .9f, 1f));
        }

        public int ActiveMarkCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < marks.Length; i++) if (marks[i].age >= 0) count++;
                return count;
            }
        }

        /// <summary>
        /// Where a ray from <paramref name="origin"/> first meets cover or ground within the gun's range, measured from
        /// <paramref name="muzzle"/> as the resolver measures it. <paramref name="kind"/> is null for the ground.
        /// </summary>
        public static bool Trace(Vector3 origin, Vector3 direction, Vector3 muzzle, float range, out Vector3 point,
            out Vector3 normal, out DuelCoverKind? kind)
        {
            point = default;
            normal = Vector3.up;
            kind = null;
            Vector3 offset = origin - muzzle;
            float b = Vector3.Dot(offset, direction), discriminant = b * b - offset.sqrMagnitude + range * range;
            if (discriminant < 0) return false;
            float length = -b + Mathf.Sqrt(discriminant);
            if (length <= 0) return false;
            bool cover = DuelArenaLayout.RayHitsCover(origin, direction, length, out float coverDistance);
            if (Ground(origin, direction, cover ? coverDistance : length, out float groundDistance))
            {
                point = origin + direction * groundDistance;
                const float e = .1f;
                normal = new Vector3(DuelArenaView.Height(point.x - e, point.z) - DuelArenaView.Height(point.x + e, point.z), 2f * e,
                    DuelArenaView.Height(point.x, point.z - e) - DuelArenaView.Height(point.x, point.z + e)).normalized;
                return true;
            }
            if (!cover) return false;
            point = origin + direction * coverDistance;
            normal = CoverFace(point, out var struck);
            kind = struck;
            return true;
        }

        /// <summary>The first point along the ray, before <paramref name="limit"/>, at or under the ground.</summary>
        private static bool Ground(Vector3 origin, Vector3 direction, float limit, out float distance)
        {
            distance = 0;
            float before = 0;
            for (float s = Mathf.Min(GroundStep, limit); ; s = Mathf.Min(s + GroundStep, limit))
            {
                Vector3 p = origin + direction * s;
                if (p.y <= DuelArenaView.Height(p.x, p.z))
                {
                    float after = s;
                    for (int i = 0; i < 10; i++)
                    {
                        float mid = (before + after) * .5f;
                        Vector3 m = origin + direction * mid;
                        if (m.y <= DuelArenaView.Height(m.x, m.z)) after = mid; else before = mid;
                    }
                    distance = after;
                    return true;
                }
                if (s >= limit) return false;
                before = s;
            }
        }

        /// <summary>The outward normal of the cover face <paramref name="point"/> lies on, and that cover's kind.</summary>
        private static Vector3 CoverFace(Vector3 point, out DuelCoverKind kind)
        {
            float best = float.MaxValue;
            Vector3 normal = Vector3.up;
            kind = DuelCoverKind.Plain;
            foreach (var cover in DuelArenaLayout.Covers)
            {
                float dx = Mathf.Abs(point.x - cover.Center.x) - cover.Half.x, dz = Mathf.Abs(point.z - cover.Center.y) - cover.Half.y;
                float dy = point.y - cover.Height;
                // On a box's surface the largest of the three gaps is zero and names the face.
                float gap = Mathf.Max(dx, Mathf.Max(dz, dy));
                if (Mathf.Abs(gap) >= best) continue;
                best = Mathf.Abs(gap);
                kind = cover.Kind;
                normal = gap == dy ? Vector3.up
                    : gap == dx ? new Vector3(Mathf.Sign(point.x - cover.Center.x), 0, 0)
                    : new Vector3(0, 0, Mathf.Sign(point.z - cover.Center.y));
            }
            return normal;
        }

        /// <summary>The evidence of one round at a point <see cref="Trace"/> found.</summary>
        public void Strike(Vector3 point, Vector3 normal, DuelCoverKind? kind, int slot)
        {
            bool metal = kind == DuelCoverKind.Truck || kind == DuelCoverKind.Apc;
            Color puff = !kind.HasValue ? Laterite : metal ? Soot : Surface(kind.Value);
            Burst(dust, point + normal * .04f, normal, 4, .3f, 1.1f, .12f, .32f, .7f, puff);
            Burst(chips, point + normal * .03f, normal, metal ? 2 : 5, 1.6f, 3.4f, .02f, .05f, .55f,
                new Color(puff.r * .55f, puff.g * .55f, puff.b * .55f, 1f));
            if (metal) Burst(sparks, point + normal * .02f, normal, 5, 2.5f, 5f, .03f, .07f, .18f, Color.white);

            ref var mark = ref marks[next];
            next = (next + 1) % marks.Length;
            var projector = mark.projector;
            var up = Mathf.Abs(normal.y) > .95f ? Vector3.forward : Vector3.up;
            projector.transform.SetPositionAndRotation(point, Quaternion.LookRotation(-normal, up) * Quaternion.Euler(0, 0, Range(0, 360)));
            // Tiles 0-1 are holes in a hard face; 2-3 are craters kicked in the dirt.
            int variant = (kind.HasValue ? 0 : 2) + random.Next(2);
            projector.uvBias = new Vector2(variant % 2, variant / 2) * .5f;
            float diameter = kind.HasValue ? Range(.1f, .14f) : Range(.16f, .24f);
            projector.size = new Vector3(diameter, diameter, .3f);
            projector.fadeFactor = 1f;
            projector.enabled = true;
            mark.age = 0;
            if (LogEvents)
                Debug.Log($"DuelImpact t={Time.time:0.00} slot {slot} {(kind.HasValue ? kind.Value.ToString() : "Ground")} at {point.ToString("F2")} normal {normal.ToString("F1")}");
        }

        private static Color Surface(DuelCoverKind kind)
        {
            switch (kind)
            {
                case DuelCoverKind.WarehouseWall: case DuelCoverKind.Column: case DuelCoverKind.Bunker: case DuelCoverKind.RoofSlab:
                    return Concrete;
                case DuelCoverKind.BrickWall:
                    return Brick;
                default:
                    return Burlap;
            }
        }

        public void Tick(float deltaTime)
        {
            for (int i = 0; i < marks.Length; i++)
            {
                ref var mark = ref marks[i];
                if (mark.age < 0) continue;
                mark.age += deltaTime;
                if (mark.age >= Lifetime) { mark.age = -1; mark.projector.enabled = false; continue; }
                if (mark.age > Lifetime - FadeSeconds) mark.projector.fadeFactor = (Lifetime - mark.age) / FadeSeconds;
            }
        }

        public void Clear()
        {
            dust.Clear();
            chips.Clear();
            sparks.Clear();
            for (int i = 0; i < marks.Length; i++) { marks[i].age = -1; marks[i].projector.enabled = false; }
            next = 0;
        }

        private ParticleSystem Particles(string name, int capacity, float gravity, bool additive, Color tint)
        {
            var node = new GameObject(name);
            node.transform.SetParent(root, false);
            var ps = node.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)capacity;
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = capacity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, additive ? AnimationCurve.Linear(0, 1, 1, .2f) : AnimationCurve.EaseInOut(0, .6f, 1, 1.8f));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.7f, .35f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            if (!additive)
            {
                var drag = ps.limitVelocityOverLifetime;
                drag.enabled = true;
                drag.dampen = .15f;
                drag.limit = .35f;
            }
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ProceduralArt.Particle(name + "Mat", additive, tint);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (additive)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = .035f;
                renderer.lengthScale = 1.2f;
            }
            ps.Play();
            return ps;
        }

        /// <summary>Particles thrown off the face along its normal, fanned out.</summary>
        private void Burst(ParticleSystem system, Vector3 at, Vector3 normal, int count, float minSpeed, float maxSpeed,
            float minSize, float maxSize, float lifetime, Color color)
        {
            var parameters = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                var direction = (normal + new Vector3(Range(-.8f, .8f), Range(-.3f, .8f), Range(-.8f, .8f))).normalized;
                parameters.position = at;
                parameters.velocity = direction * Range(minSpeed, maxSpeed);
                parameters.startSize = Range(minSize, maxSize);
                parameters.startLifetime = lifetime * Range(.7f, 1.2f);
                parameters.startColor = color;
                parameters.rotation = Range(0, 360);
                system.Emit(parameters, 1);
            }
        }

        /// <summary>
        /// Four 64-pixel marks in a 2x2 atlas, drawn once: two punched holes with a chipped, scorched rim for hard faces,
        /// two shallow craters with thrown earth for the floor. Alpha carries the shape.
        /// </summary>
        private static Texture2D MarkAtlas()
        {
            const int tile = 64, size = tile * 2;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "ImpactMarkAtlas", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            var random = new System.Random(7);
            for (int variant = 0; variant < 4; variant++)
            {
                bool hole = variant < 2;
                int ox = (variant % 2) * tile, oy = (variant / 2) * tile;
                // A lumpy rim: the radius wobbles with angle, differently for each tile.
                var wobble = new float[16];
                for (int i = 0; i < wobble.Length; i++) wobble[i] = .8f + .4f * (float)random.NextDouble();
                for (int y = 0; y < tile; y++)
                for (int x = 0; x < tile; x++)
                {
                    float u = (x + .5f) / tile * 2f - 1f, v = (y + .5f) / tile * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float angle = (Mathf.Atan2(v, u) / (Mathf.PI * 2f) + .5f) * wobble.Length;
                    int a0 = (int)angle % wobble.Length;
                    float rim = Mathf.Lerp(wobble[a0], wobble[(a0 + 1) % wobble.Length], angle - Mathf.Floor(angle));
                    float grain = (float)random.NextDouble();
                    Color c;
                    if (hole)
                    {
                        float core = .2f * rim, edge = .8f * rim;
                        if (r < core) c = new Color(.04f, .035f, .03f, 1f);
                        else if (r < edge)
                        {
                            float t = (r - core) / (edge - core);
                            c = new Color(.16f, .14f, .12f, Mathf.Clamp01((1f - t) * (1f - t) * 1.1f - grain * .35f * t));
                        }
                        else c = new Color(.16f, .14f, .12f, 0f);
                    }
                    else
                    {
                        float edge = .75f * rim;
                        float t = r / edge;
                        float alpha = t < 1f ? Mathf.Clamp01((1f - t * t) * .85f - grain * .3f) : 0f;
                        // Thrown flecks past the rim.
                        if (t >= 1f && t < 1.3f && grain > .93f) alpha = .7f;
                        c = new Color(.2f, .13f, .08f, alpha);
                    }
                    pixels[(oy + y) * size + ox + x] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
    }
}
