using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TienTuyen.Content;
using TienTuyen.Presentation;
using TienTuyen.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TienTuyen.Combat.Tests
{
    /// <summary>Third-person manual aim: trigger, crosshair ray, misses, reload and view-relative input.</summary>
    public sealed class CombatManualAimTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private CombatGame game;
        private Transform player;

        [UnitySetUp]
        public IEnumerator LoadCombatScene()
        {
            yield return SceneManager.LoadSceneAsync("CombatSpike", LoadSceneMode.Single);
            game = Object.FindFirstObjectByType<CombatGame>();
            Assert.That(game, Is.Not.Null);
            game.enabled = false;
            game.BeginRun();
            SetField(game, "spawnTimer", 999f);
            player = game.transform.Find("Player");
        }

        [UnityTearDown]
        public IEnumerator CleanScene()
        {
            var combatScene = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Manual aim test cleanup"));
            yield return SceneManager.UnloadSceneAsync(combatScene);
        }

        [Test]
        public void TriggerFiresOnlyWhileHeldAndHitsTheEnemyUnderTheCrosshair()
        {
            var enemy = CreateTarget(new Vector3(4f, .8f, 0f));
            int ammo = game.Ammo;
            game.SetViewInput(90f, enemy.transform.position, false);
            Invoke("FireWeapon");
            Assert.That(game.ManualAim, Is.True);
            Assert.That(game.AimOnTarget, Is.True);
            Assert.That(game.Ammo, Is.EqualTo(ammo), "Manual aim must not fire without the trigger.");

            bool? impactHit = null;
            game.ShotImpact += (point, hit) => impactHit = hit;
            float hp = (float)GetField(enemy.entry, "hp");
            game.SetViewInput(90f, enemy.transform.position, true);
            Invoke("FireWeapon");
            Assert.That(game.Ammo, Is.EqualTo(ammo - 1));
            Assert.That(impactHit, Is.True);
            Assert.That((float)GetField(enemy.entry, "hp"), Is.LessThan(hp));
            Assert.That(Vector3.Dot(game.PlayerAimDirection, Vector3.right), Is.GreaterThan(.99f));
        }

        [Test]
        public void AQuickKnifeSlashCutsAnEnemyWithinReachAndNotOneBeyond()
        {
            var near = CreateTarget(new Vector3(1.4f, .8f, 0f));
            var far = CreateTarget(new Vector3(2.6f, .8f, 0f), 1);
            float nearHp = (float)GetField(near.entry, "hp"), farHp = (float)GetField(far.entry, "hp");
            game.SetViewInput(90f, far.transform.position, false);
            Assert.That(game.Slash(), Is.True);
            for (int step = 0; step < 50 && game.KnifeSwinging; step++) Invoke("UpdateKnife", .02f);
            Assert.That((float)GetField(near.entry, "hp"), Is.LessThan(nearHp), "Within reach in front: cut.");
            Assert.That((float)GetField(far.entry, "hp"), Is.EqualTo(farHp), "Beyond reach: untouched.");
            Assert.That(game.KnifeSwinging, Is.False);
            Assert.That(game.SelectedEntry, Is.EqualTo(game.ActiveWeaponSlot), "The gun is back in hand after the slash.");
        }

        [Test]
        public void AShotAimedOverAnEnemysHeadMisses()
        {
            var enemy = CreateTarget(new Vector3(4f, .8f, 0f));
            float hp = (float)GetField(enemy.entry, "hp");
            int ammo = game.Ammo;
            // From eye height, pitched 20 degrees up: about 3 m high where it passes the enemy.
            Vector3 eye = player.position + Vector3.up * .8f;
            game.SetViewInput(90f, eye + new Vector3(20f, 0, 0), true, eye, Quaternion.Euler(-20f, 90f, 0) * Vector3.forward);
            Invoke("FireWeapon");
            Assert.That(game.AimOnTarget, Is.False);
            Assert.That(game.Ammo, Is.EqualTo(ammo - 1));
            Assert.That((float)GetField(enemy.entry, "hp"), Is.EqualTo(hp));
        }

        [Test]
        public void AShotThroughTheEnemysBodyHits()
        {
            var enemy = CreateTarget(new Vector3(4f, .8f, 0f));
            float hp = (float)GetField(enemy.entry, "hp");
            // From a raised, offset camera like the shoulder view, down onto the enemy's chest.
            Vector3 camera = player.position + new Vector3(-3f, 1.9f, .9f);
            Vector3 chest = enemy.transform.position + Vector3.up * .4f;
            game.SetViewInput(90f, chest, true, camera, chest - camera);
            Invoke("FireWeapon");
            Assert.That(game.AimOnTarget, Is.True);
            Assert.That((float)GetField(enemy.entry, "hp"), Is.LessThan(hp));
        }

        [Test]
        public void MissesSpendAmmunitionAndLandOnTheAimRay()
        {
            var enemy = CreateTarget(new Vector3(4f, .8f, 0f));
            float hp = (float)GetField(enemy.entry, "hp");
            int ammo = game.Ammo;
            Vector3 impact = Vector3.zero;
            bool hit = true;
            game.ShotImpact += (point, struck) => { impact = point; hit = struck; };
            // Aim at open ground to the north, away from the enemy in the east.
            game.SetViewInput(0f, player.position + new Vector3(0, 0, 6f), true);
            Invoke("FireWeapon");
            Assert.That(game.AimOnTarget, Is.False);
            Assert.That(game.Ammo, Is.EqualTo(ammo - 1));
            Assert.That(hit, Is.False);
            Assert.That((float)GetField(enemy.entry, "hp"), Is.EqualTo(hp));
            Assert.That(Mathf.Abs(impact.x - player.position.x), Is.LessThan(.01f));
            Assert.That(impact.z, Is.GreaterThan(player.position.z + 1f));
        }

        [Test]
        public void PitchedMissImpactUsesTheViewRayGroundIntersection()
        {
            CreateTarget(new Vector3(4f, .8f, 0f));
            Vector3 camera = player.position + new Vector3(-3f, 2.2f, 1f);
            Vector3 direction = (player.position + Vector3.up * .05f - camera).normalized;
            Vector3 impact = Vector3.positiveInfinity;
            game.ShotImpact += (point, hit) => impact = point;
            game.SetViewInput(90f, player.position + Vector3.forward * 6f, true, camera, direction);
            Invoke("FireWeapon");
            Assert.That(impact.y, Is.EqualTo(0f).Within(.02f), "A downward miss must end on the ground under the view ray.");
            Assert.That(Vector3.Distance(camera, impact), Is.LessThan(8f), "The impact must be near the ray's ground intersection.");
        }

        [Test]
        public void CoverStopsTheAimRayBeforeAHiddenEnemy()
        {
            // The first cover box is centred at (-8, -3.4); put the soldier east of it and the enemy west.
            player.position = new Vector3(-5f, .8f, -3.4f);
            var enemy = CreateTarget(new Vector3(-11f, .8f, -3.4f));
            float hp = (float)GetField(enemy.entry, "hp");
            game.SetViewInput(-90f, enemy.transform.position, true);
            Invoke("FireWeapon");
            Assert.That(game.AimOnTarget, Is.False);
            Assert.That((float)GetField(enemy.entry, "hp"), Is.EqualTo(hp));
        }

        [Test]
        public void ReloadKeyRefillsAPartlyUsedMagazine()
        {
            CreateTarget(new Vector3(4f, .8f, 0f));
            game.SetViewInput(90f, new Vector3(4f, .8f, 0f), true);
            Invoke("FireWeapon");
            Assert.That(game.Ammo, Is.LessThan(game.MagazineSize));
            game.RequestReload();
            Assert.That(game.ReloadRemaining, Is.GreaterThan(0f));
            game.SetViewInput(90f, new Vector3(4f, .8f, 0f), false);
            for (int frame = 0; frame < 60 * 3; frame++) game.StepSimulation(1f / 60f, Vector2.zero);
            Assert.That(game.Ammo, Is.EqualTo(game.MagazineSize));
        }

        [Test]
        public void OnlyTheWeaponInHandFires()
        {
            Give(ContentIds.Smg);
            var slots = new List<int>();
            game.WeaponFired += (slot, start, end) => slots.Add(slot);
            HoldTriggerAtOpenGround(true);
            Simulate(2f);
            Assert.That(slots.Count, Is.GreaterThan(1));
            Assert.That(slots, Is.All.EqualTo(0));
            Assert.That(game.AmmoAt(1), Is.EqualTo(24), "A holstered weapon must not spend rounds.");
        }

        [Test]
        public void DrawingAWeaponBlocksFireUntilItIsReady()
        {
            Give(ContentIds.Smg);
            var switches = new List<(int from, int to)>();
            var slots = new List<int>();
            game.WeaponSwitched += (from, to) => switches.Add((from, to));
            game.WeaponFired += (slot, start, end) => slots.Add(slot);
            HoldTriggerAtOpenGround(true);
            Assert.That(game.SwitchWeapon(1), Is.True);
            Assert.That(switches, Is.EqualTo(new[] { (0, 1) }));
            Simulate(CombatRules.WeaponDrawSeconds - .1f);
            Assert.That(slots, Is.Empty, "No shot while the weapon is being drawn.");
            Assert.That(game.IsSwitching, Is.True);
            Assert.That(game.SwitchProgress, Is.GreaterThan(0f).And.LessThan(1f));
            Simulate(.2f);
            Assert.That(game.SwitchProgress, Is.EqualTo(1f));
            Assert.That(slots, Is.Not.Empty.And.All.EqualTo(1));
            Assert.That(game.LastWeapon(), Is.True);
            Assert.That(game.ActiveWeaponSlot, Is.Zero);
            Assert.That(switches[switches.Count - 1], Is.EqualTo((1, 0)));
        }

        [Test]
        public void HolsteringCancelsAReloadButKeepsTheMagazine()
        {
            Give(ContentIds.Smg);
            HoldTriggerAtOpenGround(true);
            Invoke("FireWeapon");
            HoldTriggerAtOpenGround(false);
            int rounds = game.Ammo;
            Assert.That(rounds, Is.LessThan(game.MagazineSize));
            game.RequestReload();
            Assert.That(game.ReloadRemaining, Is.GreaterThan(0f));
            game.SwitchWeapon(1);
            Assert.That(game.ReloadAt(0), Is.Zero);
            Simulate(3f);
            Assert.That(game.AmmoAt(0), Is.EqualTo(rounds), "A holstered weapon does not reload.");
        }

        [Test]
        public void TheNextWaveKeepsAnOwnedWeaponInHandAfterTheShopShiftsSlots()
        {
            Give(ContentIds.Smg, ContentIds.Shotgun);
            game.SwitchWeapon(2);
            Assert.That(game.Run.TrySellWeapon(0, out _), Is.True);
            Invoke("ResetWave");
            Assert.That(game.ActiveWeapon.Id, Is.EqualTo(ContentIds.Shotgun));
            Assert.That(game.ActiveWeaponSlot, Is.EqualTo(1));
            Assert.That(game.Run.TrySellWeapon(1, out _), Is.True);
            Invoke("ResetWave");
            Assert.That(game.ActiveWeaponSlot, Is.Zero);
            Assert.That(game.Ammo, Is.EqualTo(game.MagazineSize));
            HoldTriggerAtOpenGround(true);
            Invoke("FireWeapon");
            Assert.That(game.Ammo, Is.EqualTo(game.MagazineSize - 1));
        }

        [Test]
        public void SellingAWeaponInTheShopKeepsEachSlotsAmmoWithItsWeapon()
        {
            Give(ContentIds.Smg, ContentIds.Shotgun);
            var ammo = (int[])GetField(game, "weaponAmmo");
            ammo[1] = 7;
            ammo[2] = 3;
            Invoke("FinishWave");
            Assert.That(game.State, Is.EqualTo(CombatState.Shop));
            Assert.That(game.SellWeapon(0, out _), Is.True);
            Assert.That((game.AmmoAt(0), game.AmmoAt(1)), Is.EqualTo((7, 3)));
            Assert.That(game.Ammo, Is.EqualTo(7), "The HUD reads the weapon now in hand.");
        }

        [Test]
        public void MovementInputTurnsWithTheCamera()
        {
            var rotate = typeof(CombatGame).GetMethod("RotatePlanar", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(rotate, Is.Not.Null);
            var forwardEast = (Vector2)rotate.Invoke(null, new object[] { Vector2.up, 90f });
            var rightSouth = (Vector2)rotate.Invoke(null, new object[] { Vector2.right, 90f });
            var forwardNorth = (Vector2)rotate.Invoke(null, new object[] { Vector2.up, 0f });
            Assert.That(Vector2.Distance(forwardEast, Vector2.right), Is.LessThan(.0001f));
            Assert.That(Vector2.Distance(rightSouth, Vector2.down), Is.LessThan(.0001f));
            Assert.That(Vector2.Distance(forwardNorth, Vector2.up), Is.LessThan(.0001f));
        }

        [UnityTest]
        public IEnumerator PresentationSwitchesToAPerspectiveShoulderCamera()
        {
            for (int frame = 0; frame < 20 && game.transform.Find("Player/HeroVisual") == null; frame++)
                yield return null;
            yield return null;
            var view = Camera.main;
            Assert.That(view, Is.Not.Null);
            Assert.That(view.orthographic, Is.False);
            Assert.That(view.GetComponent<CombatThirdPersonCamera>(), Is.Not.Null);
            Assert.That(game.GetComponent<CombatEnvironment>(), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator RunStartsFromTheMenuShotWithGameplayTransformsUntouched()
        {
            for (int frame = 0; frame < 20 && game.transform.Find("Player/HeroVisual") == null; frame++)
                yield return null;
            var rig = game.transform.Find("Player/HeroVisual").GetComponent<CombatCharacterRig>();
            var view = Camera.main.GetComponent<CombatThirdPersonCamera>();
            game.ReturnToMenu();
            Vector3 position = player.position;
            Quaternion rotation = player.rotation;
            for (float waited = 0; waited < 5f && view.MenuBlend < 1f; waited += Time.unscaledDeltaTime) yield return null;
            Assert.That(view.MenuBlend, Is.EqualTo(1f));
            Assert.That(rig.Relaxed, Is.True, "The menu hero stands relaxed, never in the aim pose.");
            Assert.That(rig.AtEaseWeight, Is.EqualTo(1f), "The menu shot opens on the hero already at ease, not lowering his weapon into it.");
            Assert.That(rig.StepFrame(out _, out _), Is.True, "The menu hero's foot is already up on the step.");

            game.BeginRun();
            for (float waited = 0; waited < 5f && view.MenuBlend > 0f; waited += Time.unscaledDeltaTime) yield return null;
            Assert.That(view.MenuBlend, Is.EqualTo(0f));
            Assert.That(rig.Relaxed, Is.False);
            Assert.That(player.position, Is.EqualTo(position));
            Assert.That(player.rotation, Is.EqualTo(rotation), "The menu shot turns only the visual, never the gameplay actor.");
        }

        [UnityTest]
        public IEnumerator FirstPersonHidesTheBodyButItsShadowAndTogglingBackRestoresIt()
        {
            for (int frame = 0; frame < 20 && game.transform.Find("Player/HeroVisual") == null; frame++)
                yield return null;
            var view = Camera.main.GetComponent<CombatThirdPersonCamera>();
            var fps = view.GetComponent<CombatFirstPersonView>();
            var renderers = game.transform.Find("Player/HeroVisual").GetComponentsInChildren<Renderer>(true);
            var before = new (UnityEngine.Rendering.ShadowCastingMode mode, bool off)[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) before[i] = (renderers[i].shadowCastingMode, renderers[i].forceRenderingOff);

            view.SetFirstPerson(true);
            for (float waited = 0; waited < 5f && !fps.Active; waited += Time.unscaledDeltaTime) yield return null;
            Assert.That(fps.Active, Is.True);
            for (int i = 0; i < renderers.Length; i++)
                Assert.That(renderers[i].forceRenderingOff || renderers[i].shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly,
                    Is.True, renderers[i].name + " must not be drawn in first person.");

            view.SetFirstPerson(false);
            for (float waited = 0; waited < 5f && fps.Active; waited += Time.unscaledDeltaTime) yield return null;
            Assert.That(fps.Active, Is.False);
            for (int i = 0; i < renderers.Length; i++)
                Assert.That((renderers[i].shadowCastingMode, renderers[i].forceRenderingOff), Is.EqualTo(before[i]), renderers[i].name);
        }

        [UnityTest]
        public IEnumerator FirstPersonKnifeTakesTheGunsPlaceAndCrossesTheViewAtContact()
        {
            var view = Camera.main.GetComponent<CombatThirdPersonCamera>();
            var fps = view.GetComponent<CombatFirstPersonView>();
            view.SetFirstPerson(true);
            for (float waited = 0; waited < 5f && !fps.Active; waited += Time.unscaledDeltaTime) yield return null;
            yield return null;
            var models = view.transform.Find("ViewmodelCamera/Viewmodel");
            var gun = models.Find("Viewmodel_" + game.ActiveWeapon.Id);
            Assert.That(game.DrawKnife(), Is.True);
            game.StepSimulation(CombatRules.WeaponDrawSeconds + .1f, Vector2.zero);
            yield return null;
            var knife = models.Find("Viewmodel_knife");
            Assert.That(knife != null && knife.gameObject.activeInHierarchy, Is.True, "First person shows no knife.");
            Assert.That(gun.gameObject.activeInHierarchy, Is.False, "The gun's viewmodel stays in view with the knife out.");

            // The blade crosses the middle of the view when the slash strikes, so what is seen and the damage agree.
            Assert.That(game.Slash(), Is.True);
            game.StepSimulation(CombatRules.KnifeContactSeconds, Vector2.zero);
            yield return null;
            Transform blade = null;
            foreach (var bone in knife.GetComponentsInChildren<Transform>()) if (bone.name == "v_weapon.knife") blade = bone;
            Vector3 seen = view.transform.Find("ViewmodelCamera").GetComponent<Camera>().WorldToViewportPoint(blade.position);
            Assert.That(seen.x, Is.InRange(.35f, .65f), "The blade is off the middle of the view at contact.");
        }

        [UnityTest]
        public IEnumerator EveryViewmodelsArmsLieInsideTheirCullingBounds()
        {
            // Bounds in the wrong space let the camera cull arms that are on screen (the smg's first-person arms vanished).
            var view = Camera.main.GetComponent<CombatThirdPersonCamera>();
            var fps = view.GetComponent<CombatFirstPersonView>();
            foreach (var kind in new[] { WeaponKind.Rifle, WeaponKind.Smg, WeaponKind.Shotgun })
            {
                typeof(CombatGame).GetProperty("SelectedWeapon").SetValue(game, kind);
                game.BeginRun();
                view.SetFirstPerson(true);
                for (float waited = 0; waited < 5f && !fps.Active; waited += Time.unscaledDeltaTime) yield return null;
                yield return null;
                var model = view.transform.Find("ViewmodelCamera/Viewmodel/Viewmodel_" + game.ActiveWeapon.Id);
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var posed = new Mesh();
                    skin.BakeMesh(posed, true);
                    int outside = 0;
                    foreach (var v in posed.vertices)
                        if (!skin.bounds.Contains(skin.transform.TransformPoint(v))) outside++;
                    Object.Destroy(posed);
                    Assert.That(outside, Is.Zero, kind + " " + skin.name + " has vertices outside its culling bounds");
                }
            }
        }

        [UnityTest]
        public IEnumerator TheRiflesImportedViewmodelHoldsItsMuzzleInViewAndItsSightsOnTheCentre()
        {
            Assume.That(game.ActiveWeapon.Id, Is.EqualTo(ContentIds.Rifle));
            var view = Camera.main.GetComponent<CombatThirdPersonCamera>();
            var fps = view.GetComponent<CombatFirstPersonView>();
            view.SetFirstPerson(true);
            for (float waited = 0; waited < 5f && !fps.Active; waited += Time.unscaledDeltaTime) yield return null;
            yield return null;
            var model = view.transform.Find("ViewmodelCamera/Viewmodel/Viewmodel_" + ContentIds.Rifle);
            Assert.That(model.GetComponentInChildren<Animator>(), Is.Not.Null, "The rifle should use its imported viewmodel.");
            Assert.That(fps.TryGetMuzzle(out var muzzle), Is.True);
            // Wrong axes put the muzzle off screen or behind the eye; wrong units put it metres or centimetres away.
            Vector3 seen = Camera.main.WorldToViewportPoint(muzzle);
            Assert.That(seen.x, Is.InRange(0f, 1f));
            Assert.That(seen.y, Is.InRange(0f, 1f));
            Assert.That(seen.z, Is.InRange(.2f, 1.5f));

            // Aiming brings the iron sights onto the screen centre, where the shot goes.
            for (int frame = 0; frame < 40; frame++)
            {
                SetField(view, "aimBlend", 1f);
                yield return null;
            }
            Assert.That(fps.TryGetSightLine(out var rear, out var front), Is.True, "The rifle should have its sights file.");
            var centre = new Vector2(.5f, .5f);
            Assert.That(Vector2.Distance(rear, centre), Is.LessThan(.005f), "Rear sight off the screen centre.");
            Assert.That(Vector2.Distance(front, centre), Is.LessThan(.005f), "Front sight off the screen centre.");
            Assert.That(front.z, Is.GreaterThan(rear.z));

            // On the sights they are the crosshair, so it is hidden; the shoulder view has no sights and keeps it.
            game.SetViewInput(90f, new Vector3(4f, .8f, 0f), false);
            yield return null;
            var hud = Object.FindFirstObjectByType<CombatHudOverlay>();
            var dot = GetField(hud, "dot");
            float Alpha() => ((Color)dot.GetType().GetProperty("color").GetValue(dot)).a;
            Assert.That(Alpha(), Is.LessThan(.01f), "Crosshair drawn over the iron sights.");
            view.SetFirstPerson(false);
            for (float waited = 0; waited < 5f && fps.Active; waited += Time.unscaledDeltaTime)
            {
                SetField(view, "aimBlend", 1f);
                yield return null;
            }
            yield return null;
            Assert.That(Alpha(), Is.GreaterThan(.5f), "Third-person aim lost its crosshair.");
        }

        [UnityTest]
        public IEnumerator TheViewmodelReloadHasFinishedWhenTheGameReloadEnds()
        {
            Assume.That(game.ActiveWeapon.Id, Is.EqualTo(ContentIds.Rifle));
            var view = Camera.main.GetComponent<CombatThirdPersonCamera>();
            var fps = view.GetComponent<CombatFirstPersonView>();
            view.SetFirstPerson(true);
            for (float waited = 0; waited < 5f && !fps.Active; waited += Time.unscaledDeltaTime) yield return null;
            yield return null;
            var model = view.transform.Find("ViewmodelCamera/Viewmodel/Viewmodel_" + ContentIds.Rifle);
            Transform muzzle = null;
            foreach (var part in model.GetComponentsInChildren<Transform>()) if (part.name == "Muzzle") muzzle = part;
            Vector3 idle = model.InverseTransformPoint(muzzle.position);

            ((int[])GetField(game, "weaponAmmo"))[0] = 1;
            game.RequestReload();
            Assert.That(game.ReloadRemaining, Is.GreaterThan(0f));
            for (int frame = 0; frame < 600 && game.ReloadRemaining > 0; frame++)
            {
                game.StepSimulation(1f / 60f, Vector2.zero);
                yield return null;
            }
            Assert.That(game.ReloadRemaining, Is.Zero);
            // The frame after the game's reload ends, the gun may go back on its sights: the take must be over by then.
            yield return null;
            Assert.That(Vector3.Distance(model.InverseTransformPoint(muzzle.position), idle), Is.LessThan(.002f),
                "The reload take is still moving the gun after the game's reload ended.");
        }

        [Test]
        public void AHeadshotDealsMoreDamageAndFeedbackMatchesHpRemoved()
        {
            var enemy = CreateTarget(new Vector3(4f, .8f, 0));
            float feedback = 0;
            bool headshot = false;
            game.DamageFeedback += (point, damage, head, critical) => { feedback = damage; headshot = head; };
            SetField(game, "rng", new System.Random(123));
            float before = (float)GetField(enemy.entry, "hp");
            game.SetViewInput(90, new Vector3(4, 1, 0), true, new Vector3(0, 1, 0), Vector3.right);
            Invoke("FireWeapon");
            float bodyDamage = feedback;
            Assert.That(bodyDamage, Is.GreaterThan(0));
            Assert.That(headshot, Is.False);
            Assert.That(before - (float)GetField(enemy.entry, "hp"), Is.EqualTo(bodyDamage).Within(.01f));

            ((float[])GetField(game, "weaponFireTimer"))[0] = 0;
            SetField(game, "rng", new System.Random(123));
            before = (float)GetField(enemy.entry, "hp");
            game.SetViewInput(90, new Vector3(4, 1.8f, 0), true, new Vector3(0, 1.8f, 0), Vector3.right);
            Invoke("FireWeapon");
            Assert.That(headshot, Is.True);
            Assert.That(feedback, Is.EqualTo(bodyDamage * CombatRules.HeadshotMultiplier).Within(.01f));
            Assert.That(before - (float)GetField(enemy.entry, "hp"), Is.EqualTo(feedback).Within(.01f));
        }

        [Test]
        public void MissesHaveNoDamageFeedbackAndLethalHitsReportOnlyRemainingHp()
        {
            var enemy = CreateTarget(new Vector3(4f, .8f, 0));
            int reports = 0;
            float damageReported = 0;
            game.DamageFeedback += (point, damage, head, critical) => { reports++; damageReported = damage; };
            game.SetViewInput(90, new Vector3(4, 3, 0), true, new Vector3(0, 3, 0), Vector3.right);
            Invoke("FireWeapon");
            Assert.That(reports, Is.Zero);

            SetField(enemy.entry, "hp", 1f);
            ((float[])GetField(game, "weaponFireTimer"))[0] = 0;
            game.SetViewInput(90, new Vector3(4, 1, 0), true, new Vector3(0, 1, 0), Vector3.right);
            Invoke("FireWeapon");
            Assert.That(reports, Is.EqualTo(1));
            Assert.That(damageReported, Is.EqualTo(1f));
        }

        [Test]
        public void ShotAboveSandbagsHitsTheExposedEnemyBehindThem()
        {
            player.position = new Vector3(-5f, .8f, -3.4f);
            var enemy = CreateTarget(new Vector3(-11f, .8f, -3.4f));
            float before = (float)GetField(enemy.entry, "hp");
            var eye = new Vector3(-5f, 1.4f, -3.4f);
            game.SetViewInput(-90f, eye + Vector3.left * 6f, true, eye, Vector3.left);
            Invoke("FireWeapon");
            Assert.That(game.AimOnTarget, Is.True, "A 1.4 m ray clears the visible 1.1 m sandbags.");
            Assert.That((float)GetField(enemy.entry, "hp"), Is.LessThan(before));
        }

        [Test]
        public void LowShotStillStopsOnTheSandbags()
        {
            player.position = new Vector3(-5f, .8f, -3.4f);
            var enemy = CreateTarget(new Vector3(-11f, .8f, -3.4f));
            float before = (float)GetField(enemy.entry, "hp");
            Vector3 impact = Vector3.positiveInfinity;
            game.ShotImpact += (point, hit) => impact = point;
            var eye = new Vector3(-5f, .7f, -3.4f);
            game.SetViewInput(-90f, eye + Vector3.left * 6f, true, eye, Vector3.left);
            Invoke("FireWeapon");
            Assert.That(game.AimOnTarget, Is.False);
            Assert.That((float)GetField(enemy.entry, "hp"), Is.EqualTo(before));
            Assert.That(impact.x, Is.EqualTo(-6.8f).Within(.02f));
        }

        [Test]
        public void CoverBetweenThirdPersonCameraAndHeroCannotSendShotBehindHero()
        {
            player.position = new Vector3(-6f, .8f, -3.4f);
            Vector3 start = Vector3.zero, end = Vector3.zero;
            game.WeaponFired += (slot, from, to) => { start = from; end = to; };
            var eye = new Vector3(-11f, .9f, -3.4f);
            game.SetViewInput(90f, new Vector3(5f, .9f, -3.4f), true, eye, Vector3.right);
            Invoke("FireWeapon");
            Assert.That(game.Ammo, Is.LessThan(game.MagazineSize));
            Assert.That(Vector3.Dot(end - start, Vector3.right), Is.GreaterThan(0),
                "An obstacle behind the gun must not reverse the tracer.");
        }

        [Test]
        public void GroundAimPointBehindHeroCannotReverseViewRayDirection()
        {
            player.position = new Vector3(-6f, .8f, -3.4f);
            game.SetViewInput(90f, new Vector3(-8f, 0, -3.4f), true,
                new Vector3(-11f, 1.6f, -3.4f), new Vector3(1, -.7f, 0));
            Assert.That(Vector3.Dot(game.PlayerAimDirection, Vector3.right), Is.GreaterThan(.99f),
                "The forward view ray controls aim even when it meets ground behind the player.");
        }
        [Test]
        public void LookingAtGroundBehindHeroStillFiresForwardAndStopsAtGround()
        {
            player.position = new Vector3(-6f, .8f, -3.4f);
            Vector3 impact = Vector3.positiveInfinity;
            game.ShotImpact += (point, hit) => impact = point;
            game.SetViewInput(90f, new Vector3(-8f, 0, -3.4f), true,
                new Vector3(-11f, 1.6f, -3.4f), new Vector3(1, -.7f, 0));
            Invoke("FireWeapon");
            Assert.That(impact.y, Is.EqualTo(0f).Within(.02f), "Nearby downward fire must not travel underground.");
            Assert.That(impact.x, Is.GreaterThan(player.position.x));
        }
        private (object entry, Transform transform) CreateTarget(Vector3 position, int slot = 0)
        {
            SetField(game, "spawnTimer", 0f);
            Invoke("UpdateSpawn", 0f);
            SetField(game, "spawnTimer", 999f);
            var enemies = (Array)GetField(game, "enemies");
            var enemy = enemies.GetValue(slot);
            if (GetField(enemy, "definition") == null) SetField(enemy, "definition", GetField(enemies.GetValue(0), "definition"));
            Assert.That(GetField(enemy, "definition"), Is.Not.Null);
            SetField(enemy, "active", true);
            SetField(enemy, "pending", false);
            SetField(enemy, "hp", 100000f);
            var body = (GameObject)GetField(enemy, "body");
            body.SetActive(true);
            body.transform.position = position;
            ((GameObject)GetField(enemy, "warning")).SetActive(false);
            ((float[])GetField(game, "weaponFireTimer"))[0] = 0f;
            return (enemy, body.transform);
        }

        /// <summary>Adds weapons through the shop at no cost and starts the wave over with full magazines.</summary>
        private void Give(params string[] ids)
        {
            foreach (string id in ids)
            {
                var shop = new ShopSession(game.Run, 1, new[] { new ShopOffer(id, OfferKind.Weapon, 1, 0), null, null, null });
                Assert.That(shop.TryPurchase(0), Is.EqualTo(PurchaseResult.Bought));
            }
            Invoke("ResetWave");
            SetField(game, "spawnTimer", 999f);
        }

        // North of the soldier is open ground: every shot misses but still counts.
        private void HoldTriggerAtOpenGround(bool trigger) =>
            game.SetViewInput(0f, player.position + new Vector3(0, 0, 6f), trigger);

        private void Simulate(float seconds)
        {
            for (float t = 0; t < seconds - 1e-4f; t += 1f / 60f) game.StepSimulation(1f / 60f, Vector2.zero);
        }

        private void Invoke(string name, params object[] arguments)
        {
            var method = typeof(CombatGame).GetMethod(name, PrivateInstance);
            Assert.That(method, Is.Not.Null, "Missing test seam: " + name);
            method.Invoke(game, arguments);
        }

        private static object GetField(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing test seam: " + name);
            return field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing test seam: " + name);
            field.SetValue(target, value);
        }
    }
}
