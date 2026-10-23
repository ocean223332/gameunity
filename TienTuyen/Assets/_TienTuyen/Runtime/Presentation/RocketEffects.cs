using TienTuyen.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace TienTuyen.Presentation
{
    // Presentation owns no damage or colliders. All objects and materials are created once.
    [DefaultExecutionOrder(260)]
    [RequireComponent(typeof(CombatGame))]
    public sealed class RocketEffects : MonoBehaviour
    {
        public const int ExplosionCapacity = 8, ScorchCapacity = 12, TrailCapacity = 16;
        const float ExplosionLifetime = 2.8f, ScorchLifetime = 18f;
        sealed class Trail
        {
            public Transform projectile;
            public Vector3 previous, direction;
            public ParticleSystem launch, flame, smoke;
        }
        sealed class Impact
        {
            public ParticleSystem flash, fire, sparks, smoke, dust;
            public float age = -1;
        }
        sealed class Scorch
        {
            public MeshRenderer renderer;
            public float age = -1;
        }
        readonly Trail[] trails = new Trail[TrailCapacity];
        readonly Impact[] impacts = new Impact[ExplosionCapacity];
        readonly Scorch[] scorches = new Scorch[ScorchCapacity];
        readonly System.Random random = new System.Random(1818);
        MaterialPropertyBlock fade;
        CombatGame game;
        Transform pool;
        Material glowMaterial, fireMaterial, smokeMaterial, scorchMaterial;
        Texture2D cloudTexture;
        Mesh scorchMesh;
        int nextTrail, nextImpact, nextScorch;

        public int ActiveTrailCount
        {
            get { int n = 0; foreach (var t in trails) if (t != null && (t.projectile != null || t.launch.particleCount + t.flame.particleCount + t.smoke.particleCount > 0)) n++; return n; }
        }
        public int ActiveExplosionCount
        {
            get { int n = 0; foreach (var i in impacts) if (i != null && i.age >= 0) n++; return n; }
        }
        public int ActiveScorchCount
        {
            get { int n = 0; foreach (var s in scorches) if (s != null && s.age >= 0) n++; return n; }
        }

        void OnEnable()
        {
            game = GetComponent<CombatGame>();
            Initialize();
            game.RocketLaunched += Launch;
            game.RocketExploded += Explode;
            game.TransientsCleared += Clear;
        }

        void Initialize()
        {
            if (pool != null) return;
            fade = new MaterialPropertyBlock();
            pool = new GameObject("RocketEffectsPool").transform; pool.SetParent(transform, false);
            glowMaterial = ProceduralArt.Particle("RocketGlowShared", true, Color.white);
            cloudTexture = MakeCloudTexture();
            fireMaterial = ProceduralArt.Particle("RocketFireShared", false, Color.white);
            fireMaterial.SetTexture("_BaseMap", cloudTexture);
            smokeMaterial = ProceduralArt.Particle("RocketSmokeShared", false, Color.white);
            smokeMaterial.SetTexture("_BaseMap", cloudTexture);
            scorchMaterial = ProceduralArt.Particle("RocketScorchShared", false, Color.white);
            scorchMaterial.renderQueue = 2990;
            scorchMesh = MakeScorchMesh();
            for (int i = 0; i < trails.Length; i++)
                trails[i] = new Trail
                {
                    launch = Particles("RocketLaunchFlame", glowMaterial, 24),
                    flame = Particles("RocketTrailFlame", glowMaterial, 48),
                    smoke = Particles("RocketTrailSmoke", smokeMaterial, 64)
                };
            for (int i = 0; i < impacts.Length; i++)
            {
                impacts[i] = new Impact
                {
                    flash = Particles("RocketImpactFlash", glowMaterial, 2),
                    fire = Particles("RocketImpactFire", fireMaterial, 24),
                    sparks = Particles("RocketImpactSparks", glowMaterial, 40, true),
                    smoke = Particles("RocketImpactSmoke", smokeMaterial, 20),
                    dust = Particles("RocketGroundDust", smokeMaterial, 24)
                };
                ConfigureImpact(impacts[i]);
            }
            for (int i = 0; i < scorches.Length; i++)
            {
                var node = new GameObject("RocketGroundScorch", typeof(MeshFilter), typeof(MeshRenderer));
                node.transform.SetParent(pool, false);
                node.GetComponent<MeshFilter>().sharedMesh = scorchMesh;
                var renderer = node.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = scorchMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false; renderer.enabled = false;
                scorches[i] = new Scorch { renderer = renderer };
            }
        }

        ParticleSystem Particles(string name, Material material, int capacity, bool sparks = false)
        {
            var node = new GameObject(name); node.transform.SetParent(pool, false);
            var ps = node.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.maxParticles = capacity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = sparks ? .7f : 0;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var color = ps.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.7f, .35f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .65f, 1, sparks ? .15f : 1.7f));
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            if (sparks) { renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.velocityScale = .035f; renderer.lengthScale = 1.5f; }
            ps.Play(false); ps.Pause(false);
            return ps;
        }

        void Launch(Transform projectile, Vector3 direction, bool hostile)
        {
            var trail = trails[nextTrail]; nextTrail = (nextTrail + 1) % trails.Length;
            Stop(trail.launch); Stop(trail.flame); Stop(trail.smoke);
            trail.projectile = projectile; trail.previous = projectile.position;
            trail.direction = direction.normalized;
            for (int i = 0; i < 12; i++)
                Emit(trail.launch, projectile.position - direction * Range(.15f, .6f),
                    -direction * Range(1, 3) + Scatter(.65f), Range(.17f, .34f), Range(.09f, .18f), new Color(2.2f, .8f, .18f, .9f));
            TrailPuff(trail, projectile.position);
        }

        void TrailPuff(Trail trail, Vector3 position)
        {
            Emit(trail.flame, position - trail.direction * .13f, -trail.direction * .7f,
                Range(.1f, .19f), .11f, new Color(2.5f, .65f, .1f, .8f));
            Emit(trail.smoke, position - trail.direction * .22f, Vector3.up * .3f + Scatter(.1f),
                Range(.15f, .24f), .55f, new Color(.32f, .3f, .27f, .33f));
        }

        void Explode(Vector3 position, float radius, bool hostile)
        {
            // Destroy(projectile) is deferred until the end of the frame: stop its trail now.
            foreach (var t in trails)
                if (t.projectile != null && (t.projectile.position - position).sqrMagnitude < .01f) t.projectile = null;
            var impact = TakeImpact();
            float power = Mathf.Clamp(radius / 2.5f, .65f, 1.6f);
            Emit(impact.flash, position + Vector3.up * .3f, Vector3.zero,
                2.3f * power, .13f, new Color(2.2f, 1.35f, .45f, .8f));
            // A low fire crown and an upward plume make the blast readable from every camera.
            for (int i = 0; i < 18; i++)
            {
                float angle = i * Mathf.PI * 2 / 18;
                var outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Emit(impact.fire, position + outward * Range(.15f, .55f) * power + Vector3.up * Range(.1f, .65f),
                    outward * Range(.6f, 1.8f) * power + Vector3.up * Range(.6f, 2f),
                    Range(1.4f, 1.9f) * power, Range(.48f, .78f), Color.white);
            }
            for (int i = 0; i < 32; i++)
                Emit(impact.sparks, position + Vector3.up * .15f, Scatter(6 * power) + Vector3.up * 3,
                    Range(.035f, .075f), Range(.4f, .95f), new Color(2.2f, 1.05f, .22f, 1));
            EmitSmoke(impact, position, power);
            float ground = CombatEnvironment.GroundHeight(position.x, position.z);
            if (Mathf.Abs(position.y - ground) <= radius)
                for (int i = 0; i < 18; i++)
                {
                    float angle = i * Mathf.PI * 2 / 18 + Range(-.08f, .08f);
                    var outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    var at = position + outward * .4f * power;
                    at.y = CombatEnvironment.GroundHeight(at.x, at.z) + .12f;
                    Emit(impact.dust, at, outward * Range(2.4f, 4f) * power + Vector3.up * .18f,
                        Range(.55f, .9f) * power, Range(.65f, .95f), new Color(.48f, .38f, .25f, .48f));
                }
            PlaceScorch(position, radius);
        }

        Impact TakeImpact()
        {
            var impact = impacts[nextImpact]; nextImpact = (nextImpact + 1) % impacts.Length;
            StopImpact(impact); impact.age = 0;
            return impact;
        }

        public void RetreatSmoke(Vector3 position) => EmitSmoke(TakeImpact(), position, 1);

        void EmitSmoke(Impact impact, Vector3 position, float power)
        {
            for (int i = 0; i < 14; i++)
                Emit(impact.smoke, position + Scatter(.55f * power) + Vector3.up * .45f,
                    Scatter(.65f) * power + Vector3.up * Range(.8f, 1.7f),
                    Range(1.5f, 2f) * power, Range(1.7f, 2.6f), new Color(.42f, .4f, .37f, .8f));
        }

        void PlaceScorch(Vector3 position, float radius)
        {
            float ground = CombatEnvironment.GroundHeight(position.x, position.z);
            if (Mathf.Abs(position.y - ground) > radius) return;
            var scorch = scorches[nextScorch]; nextScorch = (nextScorch + 1) % scorches.Length;
            const float sample = .25f;
            var normal = new Vector3(CombatEnvironment.GroundHeight(position.x - sample, position.z) - CombatEnvironment.GroundHeight(position.x + sample, position.z),
                sample * 2, CombatEnvironment.GroundHeight(position.x, position.z - sample) - CombatEnvironment.GroundHeight(position.x, position.z + sample)).normalized;
            scorch.renderer.transform.SetPositionAndRotation(new Vector3(position.x, ground + .025f, position.z),
                Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, Range(0, 360), 0));
            scorch.renderer.transform.localScale = Vector3.one * Mathf.Clamp(radius * 1.1f, .6f, 4);
            scorch.age = 0; scorch.renderer.enabled = true; ScorchOpacity(scorch, .8f);
        }

        void ScorchOpacity(Scorch scorch, float opacity)
        {
            fade.SetColor("_BaseColor", new Color(.08f, .065f, .045f, opacity));
            scorch.renderer.SetPropertyBlock(fade);
        }

        void LateUpdate()
        {
            if (game != null && game.State == CombatState.Playing) Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            if (pool == null || dt <= 0) return;
            foreach (var trail in trails)
            {
                Simulate(trail.launch, dt); Simulate(trail.flame, dt); Simulate(trail.smoke, dt);
                if (trail.projectile == null || !trail.projectile.gameObject.activeInHierarchy) { trail.projectile = null; continue; }
                var position = trail.projectile.position;
                int count = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(position, trail.previous) / .18f), 1, 16);
                for (int j = 1; j <= count; j++) TrailPuff(trail, Vector3.Lerp(trail.previous, position, (float)j / count));
                trail.previous = position;
            }
            foreach (var impact in impacts)
            {
                if (impact.age < 0) continue;
                Simulate(impact.flash, dt); Simulate(impact.fire, dt); Simulate(impact.sparks, dt); Simulate(impact.smoke, dt); Simulate(impact.dust, dt);
                impact.age += dt;
                if (impact.age >= ExplosionLifetime) { StopImpact(impact); impact.age = -1; }
            }
            foreach (var scorch in scorches)
            {
                if (scorch.age < 0) continue;
                scorch.age += dt;
                if (scorch.age >= ScorchLifetime) { scorch.age = -1; scorch.renderer.enabled = false; }
                else ScorchOpacity(scorch, .8f * Mathf.Clamp01((ScorchLifetime - scorch.age) / 6));
            }
        }

        public void Clear()
        {
            if (pool == null) return;
            foreach (var t in trails) { t.projectile = null; Stop(t.launch); Stop(t.flame); Stop(t.smoke); }
            foreach (var i in impacts) { i.age = -1; StopImpact(i); }
            foreach (var s in scorches) { s.age = -1; s.renderer.enabled = false; }
            nextTrail = nextImpact = nextScorch = 0;
        }

        static void StopImpact(Impact impact)
        {
            Stop(impact.flash); Stop(impact.fire); Stop(impact.sparks); Stop(impact.smoke); Stop(impact.dust);
        }

        static void ConfigureImpact(Impact impact)
        {
            var fireColor = impact.fire.colorOverLifetime;
            var gradient = new Gradient();
            gradient.SetKeys(new[] {
                new GradientColorKey(new Color(1.8f, 1.2f, .45f), 0),
                new GradientColorKey(new Color(1.65f, .58f, .065f), .35f),
                new GradientColorKey(new Color(.75f, .18f, .035f), .72f),
                new GradientColorKey(new Color(.14f, .095f, .07f), 1) },
                new[] { new GradientAlphaKey(.9f, 0), new GradientAlphaKey(.85f, .35f), new GradientAlphaKey(0, 1) });
            fireColor.color = gradient;
            var fireSize = impact.fire.sizeOverLifetime;
            fireSize.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
                new Keyframe(0, .75f), new Keyframe(.25f, 1.3f), new Keyframe(.65f, 1.4f), new Keyframe(1, .9f)));
            var smokeColor = impact.smoke.colorOverLifetime;
            gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0, .12f), new GradientAlphaKey(.85f, .28f), new GradientAlphaKey(.55f, .65f), new GradientAlphaKey(0, 1) });
            smokeColor.color = gradient;
        }

        // One small shared texture: irregular lobes and density replace featureless round blobs.
        static Texture2D MakeCloudTexture()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) {
                name = "RocketCloudShared", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size, v = (y + .5f) / size;
                    float distance = new Vector2(u * 2 - 1, v * 2 - 1).magnitude;
                    float noise = Mathf.PerlinNoise(u * 5.5f + 3.1f, v * 5.5f + 8.7f) * .65f +
                        Mathf.PerlinNoise(u * 13 + 9.3f, v * 13 + 2.4f) * .35f;
                    float alpha = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f + noise * .22f, .7f + noise * .28f, distance));
                    alpha *= Mathf.Lerp(.3f, 1f, noise);
                    byte tone = (byte)Mathf.Lerp(185, 255, noise);
                    pixels[y * size + x] = new Color32(tone, tone, tone, (byte)(alpha * 255));
                }
            texture.SetPixels32(pixels); texture.Apply(true, true); return texture;
        }

        static void Stop(ParticleSystem ps) => ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        static void Simulate(ParticleSystem ps, float dt) { if (ps.particleCount > 0) { ps.Simulate(dt, false, false, false); ps.Pause(false); } }
        void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float lifetime, Color color)
        {
            ps.Emit(new ParticleSystem.EmitParams { position = position, velocity = velocity, startSize = size,
                startLifetime = lifetime, startColor = color, rotation = Range(0, 360) }, 1);
            ps.Pause(false);
        }
        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
        Vector3 Scatter(float amount) => new Vector3(Range(-amount, amount), Range(-amount, amount), Range(-amount, amount));

        static Mesh MakeScorchMesh()
        {
            const int sides = 24;
            var vertices = new Vector3[sides + 1]; var colors = new Color[sides + 1];
            var uv = new Vector2[sides + 1]; var triangles = new int[sides * 3];
            colors[0] = Color.white; uv[0] = Vector2.one * .5f;
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2 / sides;
                float radius = .5f * (1 + .08f * Mathf.Sin(i * 2.3f));
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                uv[i + 1] = new Vector2(vertices[i + 1].x + .5f, vertices[i + 1].z + .5f);
                colors[i + 1] = new Color(1, 1, 1, 0);
                triangles[i * 3] = 0; triangles[i * 3 + 1] = (i + 1) % sides + 1; triangles[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "RocketScorchSharedMesh", vertices = vertices, colors = colors, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        void OnDisable()
        {
            if (game != null) { game.RocketLaunched -= Launch; game.RocketExploded -= Explode; game.TransientsCleared -= Clear; }
            Clear();
        }
        void OnDestroy()
        {
            if (pool != null) Destroy(pool.gameObject);
            Destroy(glowMaterial); Destroy(fireMaterial); Destroy(smokeMaterial); Destroy(scorchMaterial); Destroy(scorchMesh); Destroy(cloudTexture);
        }
    }
}
