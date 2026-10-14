using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TienTuyen.Combat;
using TienTuyen.Presentation;
using TienTuyen.Progression;
using UnityEngine;

/// <summary>
/// The trailer's shot list (-trailer &lt;outDir&gt;). It cuts its establishing and battle
/// shots (<see cref="BuildBaseShots"/>) between staged beats: enemies arriving, each enemy type, each weapon,
/// and a closing charger stinger. Staged enemies are placed by hand; the one the camera
/// follows is the subject.
/// </summary>
public sealed partial class IntroCinematic
{
    /// <summary>Content ids, as in TienTuyen.Content.ContentIds.</summary>
    private static class Ids
    {
        public const string Rifle = "weapon.rifle", Smg = "weapon.smg", Shotgun = "weapon.shotgun";
        public const string Infantry = "enemy.infantry", Charger = "enemy.charger", Shooter = "enemy.shooter", Elite = "enemy.elite";
    }

    private Transform subject;
    private object subjectEnemy;
    private Vector3 subjectDirection = Vector3.left;
    private Array pickupPool;
    private FieldInfo pickupActive, pickupBody, enemyCharging;

    /// <summary>The establishing and battle shots the trailer cuts between its staged beats (once the 54.5 s intro's).</summary>
    private void BuildBaseShots()
    {
        Vector3 right = Vector3.Cross(Vector3.up, Facing);
        Vector3 side = Vector3.Cross(Vector3.up, Front); // the hero's right while facing the enemy line

        // Over the canopy toward the clearing.
        shots.Add(new Shot
        {
            Name = "aerial", Seconds = 8f,
            Begin = () => heroRig.SnapAim(Facing),
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 p = Vector3.Lerp(new Vector3(-92f, 0, -74f), new Vector3(-21f, 0, -16f), e);
                p.y = CombatEnvironment.GroundHeight(p.x, p.z) + Mathf.Lerp(34f, 17f, e);
                Place(p, Vector3.Lerp(new Vector3(8f, 4f, 8f), new Vector3(0, .5f, 0), e), 44f, Mathf.Lerp(-4f, 0f, e));
                Lens(false);
            }
        });
        // Inside the camp: the lone soldier, the supply truck beyond.
        shots.Add(new Shot
        {
            Name = "camp", Seconds = 5.5f,
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 p = Vector3.Lerp(new Vector3(9.5f, 2.0f, 1.4f), new Vector3(7.6f, 2.2f, 4.6f), e);
                Place(p, Vector3.Lerp(new Vector3(-1.6f, 1.0f, -.9f), new Vector3(-1.0f, 1.0f, -.3f), e), 30f, 0, .25f);
                Lens(true, Hero + Vector3.up, 5.6f);
            }
        });
        // Low behind the hero, looking up at the banner against the sky.
        shots.Add(new Shot
        {
            Name = "flag", Seconds = 6f,
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 around = Quaternion.Euler(0, Mathf.Lerp(-38f, -12f, e), 0) * -Facing;
                Vector3 p = Hero + around * Mathf.Lerp(2.1f, 1.6f, e) + Vector3.up * .45f;
                Place(p, Hero + Vector3.up * 2.25f - right * .35f + Facing * .6f, 46f);
                Lens(true, Hero + Vector3.up * 1.9f, 2.4f);
            }
        });
        // Low front three-quarter push-in on the hero.
        shots.Add(new Shot
        {
            Name = "hero", Seconds = 5.5f,
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 around = Quaternion.Euler(0, Mathf.Lerp(28f, 16f, e), 0) * Facing;
                Vector3 p = Hero + around * Mathf.Lerp(3.6f, 2.5f, e) + Vector3.up * .42f;
                Place(p, Hero + Vector3.up * 1.18f, 34f);
                Lens(true, Hero + Vector3.up * 1.4f, 2.2f);
            }
        });
        // Ground level in the grass as the enemy line comes on.
        shots.Add(new Shot
        {
            Name = "enemies", Seconds = 4.2f,
            Begin = () => Teleport(-6f, 0f),
            Camera = (u, t) =>
            {
                Vector3 threat = Threat();
                Vector3 p = Hero + Front * 2.4f + side * (1.2f - .5f * u) + Vector3.up * .3f;
                Place(p, threat + Vector3.up * 1.2f, 30f, 0, .35f);
                ClearLine(p, threat, 1.2f);
                Lens(true, threat + Vector3.up, 2f);
            }
        });
        shots.Add(new Shot
        {
            Name = "shoulder", Seconds = 4.5f,
            Begin = () => Teleport(-6f, .5f),
            Move = t => new Vector2(0, -.25f),
            Camera = (u, t) =>
            {
                Vector3 aim = Vector3.Slerp(Front, Aim(), .35f);
                Vector3 across = Vector3.Cross(Vector3.up, aim);
                Vector3 p = Hero - aim * Mathf.Lerp(2.6f, 2.0f, u) + across * .78f + Vector3.up * 1.75f;
                Place(p, Hero + aim * 12f + Vector3.up * 1.0f, 50f, 0, 1f);
                ClearLine(p, Hero, .6f);
                Lens(false);
            }
        });
        // Running the lane under fire, filmed from behind and to the side.
        shots.Add(new Shot
        {
            Name = "track", Seconds = 4.5f,
            Begin = () => Teleport(-6f, 6.3f),
            Move = t => new Vector2(0, -.45f * Mathf.Clamp01(t * 3f)),
            Camera = (u, t) =>
            {
                Vector3 p = Hero - Front * 3.7f - Vector3.forward * .4f + Vector3.up * 1.15f;
                Place(p, Hero + Front * 3f - Vector3.forward * .8f + Vector3.up * 1.1f, 42f, 0, .7f);
                ClearLine(p, Hero, .9f);
                Lens(true, Hero + Vector3.up, 2f);
            }
        });
        // In front of the muzzle, a little off the line of fire.
        shots.Add(new Shot
        {
            Name = "reverse", Seconds = 3.5f,
            Begin = () => Teleport(-6f, -1f),
            Camera = (u, t) =>
            {
                Vector3 p = Hero + Front * Mathf.Lerp(5.0f, 4.3f, u) + side * 1.9f + Vector3.up * .75f;
                Place(p, Hero + Vector3.up * 1.15f, 30f, 0, .45f);
                ClearLine(p, Hero, 1.1f);
                Lens(true, Hero + Vector3.up * 1.2f, 1.8f);
            }
        });
        // Profile medium shot; played back at half speed in the edit.
        shots.Add(new Shot
        {
            Name = "slowmo", Seconds = 3.2f,
            Begin = () => Teleport(-6f, 0f),
            Camera = (u, t) =>
            {
                Vector3 around = Quaternion.Euler(0, Mathf.Lerp(-62f, -48f, u), 0) * Front;
                Vector3 p = Hero + around * 3.1f + Vector3.up * .95f;
                Place(p, Hero + Vector3.up * 1.2f + Front * .6f, 34f, 0, .15f);
                ClearLine(p, Hero, 1f);
                Lens(true, Hero + Vector3.up * 1.3f, 1.6f);
            }
        });
        // Behind the hero, craning up and back over the battle toward the low sun.
        shots.Add(new Shot
        {
            Name = "finale", Seconds = 8f,
            Begin = () => Teleport(-6f, 0f),
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 p = Hero - Front * Mathf.Lerp(1.9f, 7.5f, e) + side * Mathf.Lerp(.8f, 1.6f, e) + Vector3.up * Mathf.Lerp(1.5f, 5.2f, e);
                Place(p, Hero + Front * Mathf.Lerp(6f, 14f, e) + Vector3.up * Mathf.Lerp(1.6f, 1.2f, e), Mathf.Lerp(46f, 52f, e), 0, .2f);
                ClearLine(p, Hero, 1f);
                Lens(false);
            }
        });
    }

    private void BuildTrailerShots()
    {
        BuildBaseShots();
        var intro = new Dictionary<string, Shot>();
        foreach (var shot in shots) intro[shot.Name] = shot;
        shots.Clear();
        Vector3 right = Vector3.Cross(Vector3.up, Facing);
        Vector3 side = Vector3.Cross(Vector3.up, Front); // the hero's right while facing the enemy line

        // I. The road: the intro's establishing shots, plus the hero against the low sun.
        shots.Add(intro["aerial"]);
        shots.Add(intro["camp"]);
        shots.Add(new Shot
        {
            Name = "t_silhouette", Seconds = 5f,
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 p = Hero - Facing * Mathf.Lerp(5.6f, 4.4f, e) + right * Mathf.Lerp(1.1f, .7f, e) + Vector3.up * Mathf.Lerp(.3f, .55f, e);
                Place(p, Hero + Vector3.up * 1.7f + Facing * 2.5f, 40f, 0, .2f);
                Lens(true, Hero + Vector3.up * 1.3f, 4f);
            }
        });
        shots.Add(intro["flag"]);
        shots.Add(intro["hero"]);
        shots.Add(new Shot
        {
            Name = "t_preroll", Seconds = 3f, Record = false,
            Begin = () =>
            {
                UnityEngine.Random.InitState(1972);
                Arm(WeaponKind.Smg);
            },
            Camera = (u, t) => Place(Hero + new Vector3(-6f, 3f, 0), Hero, 50f)
        });

        // II. The threat. Enemies materialise on their warning markers, seen from the grass.
        shots.Add(new Shot
        {
            Name = "t_spawn", Seconds = 4.5f, Fire = Never, Spawns = false,
            Begin = () => { Clear(); Teleport(-6f, 0f); },
            Tick = Schedule((.1f, Ids.Infantry, 6.5f, .2f), (.45f, Ids.Infantry, 8.2f, -1.6f),
                (.8f, Ids.Infantry, 7.2f, 1.6f), (1.2f, Ids.Charger, 9.8f, .4f),
                (1.6f, Ids.Infantry, 10.5f, -2.4f), (2.0f, Ids.Shooter, 11.5f, 1.6f)),
            Camera = (u, t) =>
            {
                Vector3 p = Vector3.Lerp(new Vector3(-1.2f, .95f, -1.7f), new Vector3(-1.9f, .9f, -1.5f), u);
                Place(p, new Vector3(8f, .35f, .2f), 30f, 0, .3f);
                Lens(true, new Vector3(7.5f, .8f, 0), 2.8f);
            }
        });
        // High behind the enemy line as it converges on the lone soldier (not overhead: about 17 degrees down).
        shots.Add(new Shot
        {
            Name = "t_wide", Seconds = 4.5f,
            Begin = () =>
            {
                Clear();
                Teleport(-6f, 0f);
                Spawn(Ids.Infantry, 5.5f, -1.0f, .2f);
                Spawn(Ids.Infantry, 6.5f, 1.8f, .3f);
                Spawn(Ids.Charger, 7.5f, -3.0f, .35f);
                Spawn(Ids.Infantry, 8.0f, .4f, .45f);
                Spawn(Ids.Shooter, 9.5f, -1.6f, .55f);
                Spawn(Ids.Infantry, 11.0f, 1.2f, .65f);
                Spawn(Ids.Infantry, 12.0f, -2.2f, .75f);
                Spawn(Ids.Charger, 12.5f, 4.6f, .85f);
                Spawn(Ids.Infantry, 13.5f, -.4f, .95f);
            },
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 p = Vector3.Lerp(new Vector3(13.5f, 6.2f, -5.6f), new Vector3(12.2f, 5.0f, -3.4f), e);
                Place(p, Vector3.Lerp(new Vector3(-5f, .8f, .5f), new Vector3(-5.5f, .9f, 0), e), 44f, 0, .1f);
                Lens(false);
            }
        });
        // One beat per enemy type, the camera locked on the one in focus.
        shots.Add(new Shot
        {
            Name = "t_infantry", Seconds = 3f, Fire = Never, Spawns = false,
            Begin = () => Stage(Ids.Infantry, 3.2f, .6f,
                (Ids.Infantry, 6.5f, -1.8f), (Ids.Infantry, 7.8f, 1.9f), (Ids.Infantry, 9.2f, -.2f)),
            Camera = (u, t) => FaceSubject(0, Mathf.Lerp(3.4f, 2.9f, u), .9f, .6f, 1.45f, 32f, 2.2f)
        });
        shots.Add(new Shot
        {
            Name = "t_shooter", Seconds = 3.2f, Fire = Never, Spawns = false,
            Begin = () => Stage(Ids.Shooter, .6f, 1.4f, (Ids.Infantry, 5f, -1.5f), (Ids.Infantry, 6.5f, 2.2f)),
            Camera = (u, t) => FaceSubject(55f, 3.4f, 0, 1.0f, 1.4f, 30f, 2.4f)
        });
        shots.Add(new Shot
        {
            Name = "t_charger", Seconds = 3.2f, Fire = Never, Spawns = false, Guard = false,
            Begin = () => Stage(Ids.Charger, -.6f, -.3f),
            Camera = (u, t) =>
            {
                Place(new Vector3(-3.7f, 1.3f, -4.6f), new Vector3(-3.8f, .7f, 0), 44f, 0, .25f);
                Lens(true, new Vector3(-3.8f, .8f, 0), 4f);
            }
        });
        shots.Add(new Shot
        {
            Name = "t_elite", Seconds = 3.2f, Fire = Never, Spawns = false,
            Begin = () => Stage(Ids.Elite, 2.6f, .4f,
                (Ids.Infantry, 6f, -2f), (Ids.Infantry, 7.2f, 1.6f), (Ids.Infantry, 8.5f, -.4f)),
            Camera = (u, t) => FaceSubject(0, Mathf.Lerp(2.6f, 2.1f, u), .55f, .3f, 1.35f, 36f, 2.2f)
        });

        // One beat per weapon. The rifle over the shoulder at range.
        shots.Add(new Shot
        {
            Name = "t_rifle", Seconds = 4f,
            Begin = () =>
            {
                Arm(WeaponKind.Rifle);
                Spawn(Ids.Infantry, 10.5f, -.8f, .1f);
                Spawn(Ids.Infantry, 12.6f, 1.4f, .3f);
                Spawn(Ids.Infantry, 12.2f, -2.0f, .5f);
                Spawn(Ids.Infantry, 13.5f, .2f, .9f);
            },
            Camera = (u, t) =>
            {
                Vector3 aim = Aim();
                Vector3 across = Vector3.Cross(Vector3.up, aim);
                Vector3 p = Hero - aim * 3.5f + across * 1.25f + Vector3.up * 1.9f;
                Place(p, Hero + aim * 14f + Vector3.up * .95f, 22f, 0, .5f);
                ClearLine(p, Hero, .5f);
                Lens(true, Threat() + Vector3.up, 5.6f);
            }
        });
        // The submachine gun from in front, muzzle toward the lens.
        shots.Add(new Shot
        {
            Name = "t_smg", Seconds = 4f,
            Begin = () =>
            {
                Arm(WeaponKind.Smg);
                Spawn(Ids.Infantry, 6.5f, -1.2f, .1f);
                Spawn(Ids.Infantry, 7.4f, 1.3f, .2f);
                Spawn(Ids.Infantry, 8.6f, -2.4f, .35f);
                Spawn(Ids.Infantry, 9.5f, .3f, .5f);
                Spawn(Ids.Infantry, 7.0f, 3.0f, .65f);
                Spawn(Ids.Infantry, 10.8f, 1.8f, .8f);
            },
            Camera = (u, t) =>
            {
                Vector3 p = Hero + Front * Mathf.Lerp(3.4f, 3.0f, u) - side * 1.5f + Vector3.up * .85f;
                Place(p, Hero + Vector3.up * 1.15f, 32f, 0, .4f);
                ClearLine(p, Hero, 1f);
                Lens(true, Hero + Vector3.up * 1.2f, 2f);
            }
        });
        // The shotgun in profile as the line closes in.
        shots.Add(new Shot
        {
            Name = "t_shotgun", Seconds = 4f,
            Begin = () => Arm(WeaponKind.Shotgun),
            // Hold fire until they are close enough to fall inside the frame.
            Fire = () => NearestEnemy() < 4.2f,
            Tick = Schedule((0, Ids.Infantry, -1.0f, .5f), (.5f, Ids.Infantry, .2f, -1.3f),
                (1.1f, Ids.Infantry, -.2f, 1.6f), (1.7f, Ids.Infantry, 1.0f, -.2f), (2.3f, Ids.Infantry, .5f, 1.0f)),
            Camera = (u, t) =>
            {
                Vector3 p = Hero + side * 4.2f + Front * 2.1f + Vector3.up * .95f;
                Place(p, Hero + Front * 2.1f + Vector3.up * 1.0f, 44f, 0, .35f);
                Lens(true, Hero + Front * 2.1f + Vector3.up, 5.6f);
            }
        });
        // The calm before the drop: every weapon at once from here on, the hero waits.
        shots.Add(new Shot
        {
            Name = "t_lone", Seconds = 4.5f, Fire = Never,
            Begin = () => Arm(WeaponKind.Smg, Ids.Rifle, Ids.Shotgun),
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 around = Quaternion.Euler(0, Mathf.Lerp(-28f, -18f, e), 0) * Front;
                Vector3 p = Hero + around * Mathf.Lerp(3.0f, 2.4f, e) + Vector3.up * .9f;
                Place(p, Hero + Vector3.up * 1.5f, 32f, 0, .2f);
                ClearLine(p, Hero, 1f);
                Lens(true, Hero + Vector3.up * 1.4f, 1.8f);
            }
        });

        // III. The battle: the intro's combat shots plus an orbit and a lateral crane.
        shots.Add(intro["enemies"]);
        shots.Add(intro["shoulder"]);
        shots.Add(intro["track"]);
        shots.Add(new Shot
        {
            Name = "t_orbit", Seconds = 5f,
            Begin = () => Teleport(-6f, 0f),
            Camera = (u, t) =>
            {
                Vector3 around = Quaternion.Euler(0, Mathf.Lerp(-100f, 95f, Ease(u)), 0) * Front;
                Vector3 p = Hero + around * 3.1f + Vector3.up * 1.2f;
                Place(p, Hero + Vector3.up * 1.15f + Front * .6f, 38f, 0, .3f);
                ClearLine(p, Hero, 1f);
                Lens(true, Hero + Vector3.up * 1.2f, 2.4f);
            }
        });
        shots.Add(intro["reverse"]);
        shots.Add(new Shot
        {
            Name = "t_crane", Seconds = 4f,
            Begin = () => Teleport(-6f, 0f),
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 p = Hero - Front * 4.6f + side * Mathf.Lerp(4.2f, -3.6f, e) + Vector3.up * Mathf.Lerp(3.2f, 3.9f, e);
                Place(p, Hero + Front * 7f + Vector3.up * .6f, 46f, 0, .15f);
                Lens(false);
            }
        });
        shots.Add(intro["slowmo"]);
        shots.Add(intro["finale"]);

        // IV. Stinger: a charger lunges, the shotgun answers mid-dash.
        shots.Add(new Shot
        {
            Name = "t_sting", Seconds = 5f, Spawns = false, Guard = false,
            Begin = () =>
            {
                Arm(WeaponKind.Shotgun);
                subjectEnemy = null;
                subject = Spawn(Ids.Charger, 1.6f, .25f, .35f);
            },
            Fire = () => subjectEnemy != null && (bool)enemyCharging.GetValue(subjectEnemy) &&
                         Vector3.Distance(Flat(subject.position), Flat(player.position)) < 3.8f,
            Camera = (u, t) =>
            {
                Vector3 p = Hero - Front * 3.4f - side * 2.0f + Vector3.up * 1.3f;
                Place(p, new Vector3(-2.2f, 1.0f, .1f), 36f, 0, .45f);
                Lens(true, subject.position, 2.8f);
            }
        });
    }

    // ---------------------------------------------------------------- staging

    private static bool Never() => false;

    /// <summary>Planar distance from the hero to the closest living enemy.</summary>
    private float NearestEnemy()
    {
        float nearest = float.MaxValue;
        foreach (var enemy in enemies)
            if (enemy.gameObject.activeInHierarchy)
                nearest = Mathf.Min(nearest, Vector3.Distance(Flat(enemy.position), Flat(player.position)));
        return nearest;
    }

    /// <summary>A fresh run with the given weapons at the last wave, the hero on the west lane.</summary>
    private void Arm(WeaponKind kind, params string[] extra)
    {
        if (game.State != CombatState.Menu) game.ReturnToMenu();
        game.SelectWeapon(kind);
        game.BeginRun();
        var weapons = (IList)typeof(ProgressionRun).GetField("weapons", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game.Run);
        foreach (string id in extra) weapons.Add(new OwnedWeapon(id, 2, 0));
        typeof(CombatGame).GetProperty("Wave").SetValue(game, CombatRules.MaxWaves);
        Clear();
        Teleport(-6f, 0f);
    }

    /// <summary>Removes every enemy, bullet and pickup.</summary>
    private void Clear()
    {
        CallPrivate(game, "ResetWave");
        relocated.Clear();
        subject = null;
        subjectEnemy = null;
    }

    /// <summary>A clean field with the subject and a few enemies behind it.</summary>
    private void Stage(string id, float x, float z, params (string id, float x, float z)[] others)
    {
        Clear();
        Teleport(-6f, 0f);
        subject = Spawn(id, x, z, .05f);
        subjectDirection = Flat(player.position - subject.position).normalized;
        float delay = .25f;
        foreach (var other in others) Spawn(other.id, other.x, other.z, delay += .15f);
    }

    /// <summary>Raises an enemy at a point, exactly as the game's own spawner does.</summary>
    private Transform Spawn(string id, float x, float z, float warning = .6f)
    {
        foreach (object enemy in enemyPool)
        {
            if ((bool)enemyActive.GetValue(enemy) || (bool)enemyPending.GetValue(enemy)) continue;
            // TienTuyen.Content is not auto-referenced, so the definition is looked up by reflection.
            Type catalog = Type.GetType("TienTuyen.Content.ContentCatalog, TienTuyen.Content");
            object definition = catalog.GetMethod("GetEnemy").Invoke(catalog.GetProperty("P2").GetValue(null), new object[] { id });
            string role = definition.GetType().GetProperty("Role").GetValue(definition).ToString();
            var body = (GameObject)enemyBody.GetValue(enemy);
            var marker = (GameObject)enemyWarning.GetValue(enemy);
            body.transform.position = new Vector3(x, .8f, z);
            marker.transform.position = new Vector3(x, .035f, z);
            marker.transform.localScale = new Vector3(1.15f, .015f, 1.15f);
            marker.transform.rotation = Quaternion.identity;
            marker.SetActive(true);
            Type type = enemy.GetType();
            type.GetField("pending").SetValue(enemy, true);
            type.GetField("warningTimer").SetValue(enemy, warning);
            type.GetField("definition").SetValue(enemy, definition);
            type.GetField("elite").SetValue(enemy, role == "Elite");
            type.GetField("shooter").SetValue(enemy, role == "Shooter");
            type.GetField("charger").SetValue(enemy, role == "Charger");
            relocated.Add(enemy);
            if (subject == null) subjectEnemy = enemy;
            return body.transform;
        }
        return null;
    }

    /// <summary>Spawns each entry once the shot reaches its time.</summary>
    private Action<float> Schedule(params (float at, string id, float x, float z)[] entries)
    {
        int next = 0;
        return t =>
        {
            while (next < entries.Length && t >= entries[next].at)
            {
                var entry = entries[next++];
                Spawn(entry.id, entry.x, entry.z);
            }
        };
    }

    /// <summary>Kill rewards drop no pickups on film: they read as clutter on the ground.</summary>
    private void HidePickups()
    {
        foreach (object pickup in pickupPool)
        {
            if (!(bool)pickupActive.GetValue(pickup)) continue;
            pickupActive.SetValue(pickup, false);
            ((GameObject)pickupBody.GetValue(pickup)).SetActive(false);
        }
    }

    /// <summary>
    /// Locks the camera onto the subject, facing it from the hero's side. yaw swings the
    /// camera around the subject; across shifts it sideways.
    /// </summary>
    private void FaceSubject(float yaw, float distance, float across, float height, float look, float fov, float aperture)
    {
        if (subject == null) return;
        Vector3 toHero = Flat(player.position - subject.position);
        if (toHero.sqrMagnitude > .25f)
            subjectDirection = Vector3.Slerp(subjectDirection, toHero.normalized, 1f - Mathf.Exp(-2f * Time.deltaTime)).normalized;
        Vector3 direction = Quaternion.Euler(0, yaw, 0) * subjectDirection;
        Vector3 cross = Vector3.Cross(Vector3.up, direction);
        Vector3 feet = Flat(subject.position);
        Vector3 p = feet + direction * distance + cross * across + Vector3.up * height;
        Place(p, feet + Vector3.up * look + subjectDirection * .3f, fov, 0, .2f);
        ClearLine(p, subject.position, .5f);
        Lens(true, feet + Vector3.up * look, aperture);
    }
}
