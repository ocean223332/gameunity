using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TienTuyen.Presentation
{
    public sealed class CombatBloodEffects : MonoBehaviour
    {
        public const int DecalCapacity = 64;
        public const int SurfaceDecalCapacity = 8;
        public const int TotalDecalCapacity = DecalCapacity + SurfaceDecalCapacity;
        private const uint BloodReceiverLayer = 2;

        /// <summary>The ground's height at (x, z) for the ground decals; null (the campaign) is the station's terrain.</summary>
        public System.Func<float, float, float> GroundHeight;
        private const float Lifetime = 45f, FadeSeconds = 8f, PoolStart = .25f;
        private enum Kind { Droplet, Streak, Mist }
        private struct Decal
        {
            public DecalProjector projector;
            public Vector3 size;
            public float age, delay, spreadSeconds, opacity;
        }
        private struct Wound
        {
            public Transform actor;
            public Vector3 lastDrip;
            public float remaining, cooldown;
        }
        private readonly Decal[] decals = new Decal[TotalDecalCapacity];
        private readonly Wound[] wounds = new Wound[64];
        private readonly RaycastHit[] surfaceHits = new RaycastHit[16];
        private readonly System.Random random = new System.Random(1972);
        private ParticleSystem droplets, mist, streaks;
        private Material decalMaterial, surfaceMaterial, dropletMaterial, mistMaterial, streakMaterial;
        private Collider[] coverColliders;
        private int nextDecal, nextSurface = DecalCapacity, nextWound;
        private bool initialized;

        public int ActiveDecalCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < decals.Length; i++) if (decals[i].age >= 0) count++;
                return count;
            }
        }

        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            coverColliders = transform.GetComponentsInChildren<Collider>();
            for (int i = 0; i < coverColliders.Length; i++)
                if (coverColliders[i].name != "Cover") coverColliders[i] = null;
            var environment = transform.Find("Environment");
            if (environment != null)
                foreach (var receiver in environment.GetComponentsInChildren<Renderer>(true)) receiver.renderingLayerMask |= BloodReceiverLayer;
            decalMaterial = new Material(Shader.Find("TienTuyen/Blood/BloodSurface")) { name = "GroundBlood", enableInstancing = true };
            decalMaterial.SetTexture("Base_Map", Resources.Load<Texture2D>("Blood/GroundAtlas"));
            decalMaterial.SetFloat("Normal_Blend", 0);
            decalMaterial.SetFloat("Wetness", .62f);
            surfaceMaterial = new Material(Shader.Find("Shader Graphs/Decal")) { name = "SurfaceBlood", enableInstancing = true };
            surfaceMaterial.SetTexture("Base_Map", Resources.Load<Texture2D>("Blood/SplatterAtlas"));
            surfaceMaterial.SetFloat("Normal_Blend", 0);
            for (int i = 0; i < decals.Length; i++)
            {
                var node = new GameObject("BloodDecal");
                node.transform.SetParent(transform, false);
                node.transform.position = Vector3.down * 1000;
                var projector = node.AddComponent<DecalProjector>();
                projector.material = i < DecalCapacity ? decalMaterial : surfaceMaterial;
                projector.renderingLayerMask = BloodReceiverLayer;
                projector.pivot = Vector3.zero;
                projector.drawDistance = 55f;
                projector.fadeScale = .8f;
                projector.fadeFactor = 0;
                // Fade where the projection grazes a surface instead of smearing across it.
                projector.startAngleFade = 55;
                projector.endAngleFade = 80;
                projector.size = Vector3.one * .01f;
                projector.uvScale = Vector2.one * .5f;
                decals[i] = new Decal { projector = projector, age = -1 };
            }
            droplets = CreateParticles("BloodDroplets", 1800, 1.1f, true, false, Resources.Load<Texture2D>("Blood/Droplet"), out dropletMaterial);
            mist = CreateParticles("BloodMist", 600, .06f, true, true, Resources.Load<Texture2D>("Blood/Mist"), out mistMaterial);
            streaks = CreateParticles("BloodStreaks", 900, .7f, true, false, Resources.Load<Texture2D>("Blood/Droplet"), out streakMaterial);
            var dropletRenderer = droplets.GetComponent<ParticleSystemRenderer>();
            dropletRenderer.velocityScale = .028f;
            dropletRenderer.lengthScale = 1.4f;
            var streakRenderer = streaks.GetComponent<ParticleSystemRenderer>();
            streakRenderer.velocityScale = .05f;
            streakRenderer.lengthScale = 1.5f;
            // Mist puffs stretch along their flight so the cloud forms a plume behind the exit wound;
            // freeform stretching keeps them from collapsing into slivers when the shot points at the camera.
            var mistRenderer = mist.GetComponent<ParticleSystemRenderer>();
            mistRenderer.velocityScale = .1f;
            mistRenderer.lengthScale = 1.3f;
            mistRenderer.freeformStretching = true;
            // Mist.png is a 2x2 atlas; each puff keeps one random tile for its whole life.
            var sheet = mist.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = sheet.numTilesY = 2;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0, .999f);
            var mistColor = mist.colorOverLifetime;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.75f, .3f), new GradientAlphaKey(0, 1) });
            mistColor.color = fade;
            var mistSize = mist.sizeOverLifetime;
            mistSize.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .8f, 2.4f, 2.4f), new Keyframe(1, 1.6f, .4f, .4f)));
            var mistDrag = mist.limitVelocityOverLifetime;
            mistDrag.enabled = true;
            mistDrag.dampen = .18f;
            mistDrag.limit = .25f;
        }

        public void Hit(Transform actor, Vector3 point, Vector3 direction, bool critical, bool killed)
        {
            direction = direction.sqrMagnitude > .001f ? direction.normalized : Vector3.forward;
            float intensity = killed ? critical ? 2.6f : 2.1f : critical ? 1.7f : 1f;
            // Combat reports the hit at the body's centre; blood leaves through the entry and exit surfaces.
            Vector3 entry = point - direction * .22f, exit = point + direction * .2f;
            // Far hits grow a little so they still read at gameplay range (about 14 m from the camera at 10 m from the player).
            var view = Camera.main;
            float reach = view != null ? Mathf.Clamp(Vector3.Distance(view.transform.position, point) / 7f, 1f, 1.6f) : 1f;
            Spray(droplets, entry, exit, direction, Mathf.RoundToInt(46 * intensity), intensity, reach, Kind.Droplet);
            Spray(streaks, entry, exit, direction, Mathf.RoundToInt(12 * intensity), intensity, reach, Kind.Streak);
            Spray(mist, entry, exit, direction, Mathf.RoundToInt(26 * intensity), intensity, reach, Kind.Mist);
            var side = Vector3.Cross(direction, Vector3.up);
            int splats = killed ? 3 : critical ? 2 : 1;
            for (int i = 0; i < splats; i++)
            {
                var behind = point + direction * Range(.5f, 1f) * (1 + i * .45f) + side * Range(-.3f, .3f);
                Ground(behind, Range(.7f, 1.05f) * Mathf.Sqrt(intensity), random.Next(3), 0, direction, Range(.16f, .3f) + i * .08f);
            }
            int count = Physics.RaycastNonAlloc(point + direction * .25f, direction, surfaceHits, 2.75f, ~0, QueryTriggerInteraction.Ignore);
            int nearest = -1;
            float distance = 3f;
            for (int i = 0; i < count; i++)
            {
                var hit = surfaceHits[i];
                // The static Cover boxes locate the dressed sandbag surfaces; visual station props have no colliders.
                bool cover = false;
                for (int c = 0; c < coverColliders.Length; c++)
                    if (coverColliders[c] != null && hit.collider == coverColliders[c]) { cover = true; break; }
                if (!cover || hit.distance >= distance) continue;
                distance = hit.distance;
                nearest = i;
            }
            if (nearest >= 0)
            {
                var hit = surfaceHits[nearest];
                PlaceDecal(hit.point - hit.normal * .06f, hit.normal, Range(.75f, 1.1f) * Mathf.Sqrt(intensity), .4f, random.Next(4), 0, true, Vector3.zero, .05f + hit.distance * .02f);
            }
            if (killed)
            {
                Ground(actor.position + direction * .2f, Range(1f, 1.35f), 3, 3.2f, Vector3.zero, .35f);
                for (int i = 0; i < wounds.Length; i++) if (wounds[i].actor == actor) wounds[i] = default;
            }
            else
            {
                int slot = -1;
                for (int i = 0; i < wounds.Length; i++) if (wounds[i].actor == actor) { slot = i; break; }
                if (slot < 0) { slot = nextWound; nextWound = (nextWound + 1) % wounds.Length; }
                wounds[slot] = new Wound { actor = actor, lastDrip = actor.position, remaining = critical ? 6f : 4f, cooldown = .3f };
            }
        }

        public void GroundSplatter(Vector3 point, float diameter, bool pool = false, Vector3 direction = default) =>
            Ground(point, diameter, pool ? 3 : random.Next(3), pool ? 3.2f : 0, direction, 0);

        public void SurfaceSplatter(Vector3 point, Vector3 normal, float diameter) =>
            PlaceDecal(point, normal, diameter, .4f, random.Next(4), 0, true, Vector3.zero, 0);

        private float Height(float x, float z) => GroundHeight != null ? GroundHeight(x, z) : CombatEnvironment.GroundHeight(x, z);

        private void Ground(Vector3 point, float diameter, int variant, float spreadSeconds, Vector3 direction, float delay)
        {
            point.y = Height(point.x, point.z);
            const float sample = .25f;
            var normal = new Vector3(
                Height(point.x - sample, point.z) - Height(point.x + sample, point.z),
                sample * 2,
                Height(point.x, point.z - sample) - Height(point.x, point.z + sample)).normalized;
            PlaceDecal(point, normal, diameter, .35f, variant, spreadSeconds, false, direction, delay);
        }

        private void PlaceDecal(Vector3 point, Vector3 normal, float diameter, float depth, int variant, float spreadSeconds, bool surface, Vector3 direction, float delay)
        {
            int slot = surface ? nextSurface : nextDecal;
            if (surface) nextSurface = DecalCapacity + (nextSurface - DecalCapacity + 1) % SurfaceDecalCapacity;
            else nextDecal = (nextDecal + 1) % DecalCapacity;
            ref var decal = ref decals[slot];
            var projector = decal.projector;
            var along = Vector3.ProjectOnPlane(direction, normal);
            bool directional = along.sqrMagnitude > .001f;
            var up = directional ? along.normalized : Mathf.Abs(normal.y) > .95f ? Vector3.forward : Vector3.up;
            projector.transform.SetPositionAndRotation(point, Quaternion.LookRotation(-normal, up) * Quaternion.Euler(0, 0, directional ? Range(-18, 18) : Range(0, 360)));
            projector.uvBias = new Vector2(variant % 2, variant / 2) * .5f;
            decal.size = new Vector3(diameter * Range(directional ? .55f : .85f, directional ? .8f : 1.15f), diameter * (directional ? 1.25f : 1), depth);
            decal.spreadSeconds = spreadSeconds;
            decal.age = 0;
            decal.delay = delay;
            projector.size = spreadSeconds > 0 ? new Vector3(decal.size.x * PoolStart, decal.size.y * PoolStart, depth) : decal.size;
            decal.opacity = surface ? .95f : spreadSeconds > 0 ? .96f : .92f;
            // Delayed splats stay invisible until the spray lands.
            projector.fadeFactor = delay > 0 ? 0 : decal.opacity;
        }

        public void Tick(float dt)
        {
            if (!initialized || dt <= 0) return;
            droplets.Simulate(dt, false, false, false);
            mist.Simulate(dt, false, false, false);
            streaks.Simulate(dt, false, false, false);
            for (int i = 0; i < decals.Length; i++)
            {
                ref var decal = ref decals[i];
                if (decal.age < 0) continue;
                decal.age += dt;
                if (decal.age >= Lifetime) { Hide(ref decal); continue; }
                if (decal.delay > 0)
                {
                    decal.delay -= dt;
                    if (decal.delay > 0) continue;
                    decal.age = 0;
                    decal.projector.fadeFactor = decal.opacity;
                }
                if (decal.spreadSeconds > 0)
                {
                    // The pool spreads fast at first, then creeps out.
                    float t = Mathf.Clamp01(decal.age / decal.spreadSeconds);
                    float growth = Mathf.Lerp(PoolStart, 1f, 1 - (1 - t) * (1 - t));
                    decal.projector.size = new Vector3(decal.size.x * growth, decal.size.y * growth, decal.size.z);
                    if (decal.age >= decal.spreadSeconds) decal.spreadSeconds = 0;
                }
                if (decal.age > Lifetime - FadeSeconds)
                    decal.projector.fadeFactor = decal.opacity * Mathf.Clamp01((Lifetime - decal.age) / FadeSeconds);
            }
            for (int i = 0; i < wounds.Length; i++)
            {
                ref var wound = ref wounds[i];
                if (wound.actor == null) continue;
                wound.remaining -= dt;
                if (wound.remaining <= 0 || !wound.actor.gameObject.activeInHierarchy) { wound = default; continue; }
                wound.cooldown -= dt;
                if (wound.cooldown > 0 || (wound.actor.position - wound.lastDrip).sqrMagnitude < .18f * .18f) continue;
                wound.lastDrip = wound.actor.position;
                wound.cooldown = .3f;
                Ground(wound.lastDrip + new Vector3(Range(-.12f, .12f), 0, Range(-.12f, .12f)), Range(.16f, .28f), 3, 0, Vector3.zero, 0);
            }
        }

        public void Clear()
        {
            if (!initialized) return;
            droplets.Clear();
            mist.Clear();
            streaks.Clear();
            for (int i = 0; i < decals.Length; i++) Hide(ref decals[i]);
            for (int i = 0; i < wounds.Length; i++) wounds[i] = default;
            nextDecal = nextWound = 0;
            nextSurface = DecalCapacity;
        }

        private static void Hide(ref Decal decal)
        {
            decal.age = -1;
            decal.projector.fadeFactor = 0;
            decal.projector.transform.position = Vector3.down * 1000;
        }

        private ParticleSystem CreateParticles(string name, int capacity, float gravity, bool stretch, bool lit, Texture texture, out Material material)
        {
            var node = new GameObject(name);
            node.transform.SetParent(transform, false);
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
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.9f, .6f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, stretch ? AnimationCurve.Linear(0, 1, 1, .45f) : AnimationCurve.Linear(0, .9f, 1, 1.35f));
            // Mist is lit like the scene so the cloud darkens in shade; tiny drops stay unlit so they always read.
            // The alpha-blended setup lives in material assets: a material made only in code leaves its transparent
            // shader variant out of the Player build, and the particles draw as opaque squares.
            material = new Material(Resources.Load<Material>(lit ? "Blood/BloodParticleLit" : "Blood/BloodParticleUnlit")) { name = name + "Material" };
            material.SetTexture("_BaseMap", texture);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = lit;
            // Half-spherical normals give the lit cloud some volume instead of one flat camera-facing tone.
            if (lit) renderer.normalDirection = .35f;
            if (stretch)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = .012f;
                renderer.lengthScale = 1.1f;
            }
            ps.Play();
            ps.Pause();
            return ps;
        }

        private void Spray(ParticleSystem system, Vector3 entry, Vector3 exit, Vector3 forward, int count, float intensity, float reach, Kind kind)
        {
            Vector3 right = Vector3.Cross(forward, Mathf.Abs(forward.y) > .95f ? Vector3.forward : Vector3.up).normalized;
            Vector3 up = Vector3.Cross(right, forward);
            float power = Mathf.Sqrt(intensity);
            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                // Most blood leaves through the exit wound; a share sprays back out of the entry towards the shooter.
                bool back = kind == Kind.Streak ? i % 6 == 0 : i % 3 == 0;
                Vector3 origin = back ? entry : exit, velocity;
                float rotation = Range(0, 360);
                switch (kind)
                {
                    case Kind.Mist:
                        // Many small puffs strung out of each wound, widening and growing with distance: two cones, not a ball.
                        float along = Mathf.Pow(Range(0, 1f), .8f);
                        Vector3 axis = back ? -forward : forward;
                        Vector3 lateral = right * Range(-1f, 1f) + up * Range(-.7f, 1f);
                        origin += (axis * along * (back ? .2f : .34f) + lateral * (.05f + .15f * along)) * power;
                        velocity = axis * (back ? Range(.4f, 1.3f) : Range(.6f, 2.2f) * power) * (.6f + .6f * along)
                            + right * Range(-.5f, .5f) * (.4f + along) + up * Range(-.15f, .45f);
                        emit.startSize = Range(.17f, .36f) * power * reach * (back ? .7f + .5f * along : .8f + .6f * along);
                        emit.startLifetime = Range(.3f, .46f);
                        emit.startColor = new Color(Range(.5f, .62f), Range(.045f, .07f), Range(.035f, .06f), Range(.42f, .95f));
                        // Stretched puffs follow their flight; only a slight tilt keeps the tiles from lining up.
                        rotation = Range(-14, 14);
                        emit.angularVelocity = Range(-25, 25);
                        break;
                    case Kind.Streak:
                        velocity = (back ? -forward * Range(2f, 4f) : forward * Range(6f, 10.5f) * power)
                            + right * Range(-2.2f, 2.2f) + up * Range(-.2f, 1.6f);
                        emit.startSize = Range(.028f, .05f) * reach;
                        emit.startLifetime = Range(.2f, .32f);
                        emit.startColor = new Color(Range(.46f, .6f), Range(.02f, .045f), Range(.015f, .035f), 1);
                        emit.angularVelocity = 0;
                        break;
                    default:
                        velocity = (back ? -forward * Range(1f, 2.8f) : forward * Range(2.5f, 6.5f) * power)
                            + right * Range(-1.9f, 1.9f) + up * Range(-.3f, 1.8f);
                        emit.startSize = Range(.024f, .058f) * reach;
                        emit.startLifetime = Range(.4f, .7f);
                        emit.startColor = new Color(Range(.46f, .62f), Range(.02f, .04f), Range(.015f, .03f), 1);
                        emit.angularVelocity = 0;
                        origin += right * Range(-.07f, .07f) + up * Range(-.06f, .08f);
                        break;
                }
                emit.position = origin;
                emit.velocity = velocity;
                emit.rotation = rotation;
                system.Emit(emit, 1);
            }
            system.Pause(false);
        }

        private float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        private void OnDestroy()
        {
            Destroy(decalMaterial);
            Destroy(surfaceMaterial);
            Destroy(dropletMaterial);
            Destroy(mistMaterial);
            Destroy(streakMaterial);
        }
    }
}
