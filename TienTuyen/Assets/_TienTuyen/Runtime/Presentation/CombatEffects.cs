using System;
using TienTuyen.Combat;
using UnityEngine;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Pooled combat feedback: muzzle light and smoke, bullet impacts and blood,
    /// footstep puffs and camera kick. Observes CombatGame events only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatEffects : MonoBehaviour
    {
        private CombatGame game;
        private Func<Transform> muzzle;
        private Transform player;
        private CombatThirdPersonCamera view;
        private ParticleSystem sparks, dust, smoke, debris;
        private CombatBloodEffects blood;
        private Vector3 shotDirection = Vector3.forward, enemyImpact;
        private int impactFrame = -1;
        private bool hasEnemyImpact;
        private object presentedRun;
        private Light muzzleLight;
        private float muzzleLightTime, muzzleLightShare = 1f;
        private CombatFirstPersonView firstPerson;
        private Vector3 lastStep;
        private readonly System.Random random = new System.Random(4242);
        private static readonly Color LateriteDust = new Color(.69f, .47f, .32f, .55f);

        public void Initialize(CombatGame combat, Transform actor, Func<Transform> muzzleProvider, CombatThirdPersonCamera camera)
        {
            game = combat;
            player = actor;
            muzzle = muzzleProvider;
            view = camera;
            sparks = CreateSystem("ImpactSparks", true, 1.6f, 160, true, new Color(3.4f, 2f, .85f, 1f));
            dust = CreateSystem("ImpactDust", false, -.06f, 220, false, Color.white);
            smoke = CreateSystem("MuzzleSmoke", false, -.12f, 80, false, Color.white);
            debris = CreateSystem("ImpactDebris", false, 2.2f, 120, false, Color.white);
            blood = gameObject.AddComponent<CombatBloodEffects>();
            blood.Initialize();
            presentedRun = game.Run;
            muzzleLight = new GameObject("MuzzleLight").AddComponent<Light>();
            muzzleLight.transform.SetParent(transform, false);
            muzzleLight.type = LightType.Point;
            muzzleLight.color = new Color(1f, .72f, .38f);
            muzzleLight.range = 6f;
            muzzleLight.intensity = 0;
            muzzleLight.shadows = LightShadows.None;
            muzzleLight.enabled = false;
            if (player != null) lastStep = player.position;
            game.WeaponFired += OnWeaponFired;
            game.ShotImpact += OnShotImpact;
            game.EnemyDamaged += OnEnemyDamaged;
            game.PlayerDamaged += OnPlayerDamaged;
            game.EnemyFired += OnEnemyFired;
        }

        private void OnDestroy()
        {
            if (game == null) return;
            game.WeaponFired -= OnWeaponFired;
            game.ShotImpact -= OnShotImpact;
            game.EnemyDamaged -= OnEnemyDamaged;
            game.PlayerDamaged -= OnPlayerDamaged;
            game.EnemyFired -= OnEnemyFired;
        }

        private void LateUpdate()
        {
            if (game == null || game.State == CombatState.Paused) return;
            float dt = Time.unscaledDeltaTime;
            ResetBloodForRun();
            blood.Tick(dt);
            if (muzzleLightTime > 0)
            {
                muzzleLightTime = Mathf.Max(0, muzzleLightTime - dt);
                muzzleLight.intensity = 7f * muzzleLightShare * (muzzleLightTime / .06f);
                muzzleLight.enabled = muzzleLightTime > 0;
            }
            // Laterite dust kicked up by running boots.
            if (player != null && game.State == CombatState.Playing)
            {
                Vector3 step = player.position - lastStep;
                step.y = 0;
                if (step.sqrMagnitude > .85f * .85f)
                {
                    lastStep = player.position;
                    Puff(dust, player.position + Vector3.down * .75f, 3, .25f, .55f, .35f, LateriteDust * new Color(1, 1, 1, .6f));
                }
                else if (step.sqrMagnitude > 25f) lastStep = player.position;
            }
        }

        private void OnWeaponFired(int slot, Vector3 start, Vector3 end)
        {
            shotDirection = (end - start).normalized;
            hasEnemyImpact = false;
            Transform socket = muzzle?.Invoke();
            Vector3 origin = socket != null ? socket.position : start + Vector3.up * .5f;
            muzzleLight.transform.position = origin + (end - start).normalized * .15f;
            muzzleLight.enabled = true;
            muzzleLightTime = .06f;
            // The same light shows on the first-person gun and hands; a suppressed gun gives little of it.
            if (firstPerson == null && view != null) firstPerson = view.GetComponent<CombatFirstPersonView>();
            muzzleLightShare = firstPerson != null ? firstPerson.MuzzleLight : 1f;
            // In first person the muzzle is under a metre from the eye, not three or four: smoke sized for the shoulder
            // camera would fill the view, and more so in ADS, where the world zooms but the viewmodel does not.
            float puff = view != null && view.FirstPersonWeight > .5f ? .3f : 1f;
            Puff(smoke, origin, 2, .2f * puff, .35f * puff, .9f, new Color(.8f, .8f, .78f, .25f));
            var weapon = game.WeaponAt(slot);
            float kick = weapon == null ? .3f : weapon.PelletsPerShot > 1 ? .9f : weapon.ShotCooldownSeconds < .2f ? .18f : .38f;
            if (view != null) view.Kick(kick, kick * .12f);
        }

        private void OnShotImpact(Vector3 point, bool hitEnemy)
        {
            if (hitEnemy)
            {
                enemyImpact = point;
                impactFrame = Time.frameCount;
                hasEnemyImpact = true;
            }
            else
            {
                // Misses kick up the red earth where the round lands.
                Vector3 ground = new Vector3(point.x, CombatEnvironment.GroundHeight(point.x, point.z) + .05f, point.z);
                Puff(dust, ground, 5, .35f, .8f, .8f, LateriteDust);
                Burst(debris, ground, 6, 1.5f, 3.2f, .04f, .08f, .6f, new Color(.35f, .22f, .14f, 1f));
                Burst(sparks, point, 3, 2f, 4f, .05f, .12f, .2f, new Color(1f, .8f, .55f, 1f));
            }
        }

        private void OnEnemyDamaged(Transform actor, float damage, bool critical, bool killed)
        {
            if (actor == null) return;
            ResetBloodForRun();
            bool fresh = hasEnemyImpact && impactFrame == Time.frameCount && (enemyImpact - actor.position).sqrMagnitude < 3f;
            Vector3 point = fresh ? enemyImpact : actor.position + Vector3.up * .2f;
            Vector3 direction = fresh ? shotDirection : player != null ? (actor.position - player.position).normalized : actor.forward;
            hasEnemyImpact = false;
            blood.Hit(actor, point, direction, critical, killed);
        }

        private void ResetBloodForRun()
        {
            if (ReferenceEquals(presentedRun, game.Run)) return;
            presentedRun = game.Run;
            blood.Clear();
        }

        private void OnPlayerDamaged(Vector3 source, float damage)
        {
            if (view != null) view.Kick(0, Mathf.Clamp(damage / 25f, .25f, .7f));
        }

        private void OnEnemyFired(Transform actor, Vector3 direction) =>
            Puff(smoke, actor.position + Vector3.up * .45f + direction * .6f, 2, .15f, .3f, .7f, new Color(.8f, .8f, .78f, .22f));

        // ---------- Particle plumbing ----------

        private ParticleSystem CreateSystem(string name, bool additive, float gravity, int capacity, bool stretch, Color tint)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)name.Length * 7919u;
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = capacity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, additive ? AnimationCurve.Linear(0, 1, 1, .2f) : AnimationCurve.EaseInOut(0, .5f, 1, 1.6f));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.8f, .4f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            if (!additive)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.dampen = .12f;
                limit.limit = .4f;
            }
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            // Emit colours are 8-bit; HDR brightness for bloom comes from the material tint.
            renderer.sharedMaterial = ProceduralArt.Particle(name + "Mat", additive, tint);
            if (stretch)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = .035f;
                renderer.lengthScale = 1.2f;
            }
            ps.Play();
            return ps;
        }

        private void Puff(ParticleSystem system, Vector3 at, int count, float minSize, float maxSize, float lifetime, Color color) =>
            Burst(system, at, count, .2f, .9f, minSize, maxSize, lifetime, color);

        private void Burst(ParticleSystem system, Vector3 at, int count, float minSpeed, float maxSpeed,
            float minSize, float maxSize, float lifetime, Color color)
        {
            if (system == null) return;
            var parameters = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                var direction = new Vector3(Range(-1, 1), Range(.1f, 1f), Range(-1, 1)).normalized;
                parameters.position = at + direction * .05f;
                parameters.velocity = direction * Range(minSpeed, maxSpeed);
                parameters.startSize = Range(minSize, maxSize);
                parameters.startLifetime = lifetime * Range(.7f, 1.2f);
                parameters.startColor = color;
                parameters.rotation = Range(0, 360);
                system.Emit(parameters, 1);
            }
        }

        private float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
    }
}
