using System;
using System.Collections.Generic;
using TienTuyen.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Presentation-only art pass for the CombatSpike simulation.  CombatGame owns all
    /// movement, hit tests and pooled objects; this component decorates those objects
    /// after they exist and never changes their colliders or gameplay state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatGame))]
    public sealed class CombatArtDirector : MonoBehaviour
    {
        private static readonly Color Olive = Hex("#728064");
        private static readonly Color OliveDark = Hex("#3F5143");
        private static readonly Color Cloth = Hex("#53634C");
        private static readonly Color Skin = Hex("#B98966");
        private static readonly Color Metal = Hex("#39433E");
        private static readonly Color Wood = Hex("#79553D");
        private static readonly Color Sand = Hex("#9D8A67");
        private static readonly Color Enemy = Hex("#666158");
        private static readonly Color EnemyDark = Hex("#454A45");
        private static readonly Color Coral = Hex("#E57A5B");
        private static readonly Color Gold = Hex("#E2B65A");
        private static readonly Color Ground = Hex("#5E674F");
        private static readonly Color Dirt = Hex("#765B45");
        private static readonly Color Foliage = Hex("#314638");
        private const float HeroMarkerDiameter = .72f;
        private const float EnemyMarkerDiameter = .55f;
        private const float MarkerThicknessScale = .025f;

        private CombatGame game;
        private Transform player, supply;
        private GameObject playerVisual, supplyVisual;
        private readonly Dictionary<Transform, EnemyVisual> enemyVisuals = new Dictionary<Transform, EnemyVisual>();
        private readonly Dictionary<Transform, Vector3> previousEnemyPositions = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Vector3> previousBulletPositions = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, bool> previousEnemyActive = new Dictionary<Transform, bool>();
        private readonly List<Transform> warnings = new List<Transform>();
        private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>(StringComparer.Ordinal);
        private Material groundMaterial, coverMaterial, playerMaterial, enemyMaterial, shooterMaterial, projectileMaterial, pickupMaterial, warningMaterial;
        private bool arenaBuilt;
        private float time;
        private Vector3 lastPlayerPosition;
        private float hitTimer;
        private float lastHealth = -1f;
        private CombatState lastState = (CombatState)(-1);
        private bool lastChargerDash;
        private LineRenderer tracerBeam;
        private Transform tracer, heroMarker;
        private CombatCharacterRig playerRig;
        private Vector3 lastShotDirection = Vector3.forward, presentationTracerEnd;
        private float shotAimRemaining;
        private int visibleWeaponSlot;
        private readonly List<Transform> bulletTransforms = new List<Transform>();

        private sealed class EnemyVisual
        {
            public Transform root;
            public GameObject infantry, shooter, charger, elite;
            public GameObject ring;
            public Renderer bodyRenderer;
            public Transform chargerMarker;
            public CombatCharacterRig infantryRig, shooterRig, chargerRig, eliteRig;
            public Vector3 shotDirection;
            public float shotAimRemaining;
        }

        private void Awake()
        {
            game = GetComponent<CombatGame>();
            CreateMaterials();
        }

        private void Start()
        {
            // CombatGame builds its arena in Awake, so all generated pool objects are
            // available by Start.  Wait one frame before decorating to be robust to
            // domain reloads and editor instantiated scenes.
            Invoke(nameof(BuildPresentation), 0f);
        }

        private void OnEnable()
        {
            if (game == null) game = GetComponent<CombatGame>();
            game.WeaponFired += OnWeaponFired;
            game.EnemyFired += OnEnemyFired;
        }

        private void OnDisable()
        {
            if (game == null) return;
            game.WeaponFired -= OnWeaponFired;
            game.EnemyFired -= OnEnemyFired;
        }

        private void LateUpdate()
        {
            if (!arenaBuilt) return;
            if (game != null && game.State == CombatState.Paused) return;
            time += Time.unscaledDeltaTime;
            if (game != null && game.State != lastState)
            {
                if (game.State == CombatState.Victory && player != null) SpawnBurst(player.position, Gold);
                if (game.State == CombatState.Defeat && player != null) SpawnBurst(player.position, Coral);
                lastState = game.State;
            }
            if (game != null && game.ChargerDashActive && !lastChargerDash)
                foreach (var pair in enemyVisuals) if (pair.Key.gameObject.activeSelf && pair.Value.charger.activeSelf) SpawnBurst(pair.Key.position, Coral);
            if (game != null) lastChargerDash = game.ChargerDashActive;
            UpdatePlayer();
            UpdateEnemies();
            UpdateWarnings();
            UpdateSupply();
            UpdateTracerAndBullets();
        }

        private void CreateMaterials()
        {
            groundMaterial = RuntimeMaterial("Art_Ground", Ground);
            coverMaterial = RuntimeMaterial("Art_Sandbag", Sand);
            playerMaterial = RuntimeMaterial("Art_Player", Olive);
            enemyMaterial = RuntimeMaterial("Art_Enemy", Enemy);
            shooterMaterial = RuntimeMaterial("Art_Shooter", Hex("#777164"));
            projectileMaterial = RuntimeMaterial("Art_Projectile", Hex("#F6D27A"), true);
            pickupMaterial = RuntimeMaterial("Art_Supply", Gold, true);
            warningMaterial = RuntimeMaterial("Art_Warning", Coral, true);
            AssignCombatMaterials();
        }

        private void AssignCombatMaterials()
        {
            if (game == null) return;
            game.GroundMaterial = groundMaterial;
            game.CoverMaterial = coverMaterial;
            game.PlayerMaterial = playerMaterial;
            game.EnemyMaterial = enemyMaterial;
            game.ShooterMaterial = shooterMaterial;
            game.ProjectileMaterial = projectileMaterial;
            game.PickupMaterial = pickupMaterial;
            game.WarningMaterial = warningMaterial;
        }

        private void BuildPresentation()
        {
            if (game == null || arenaBuilt) return;
            player = transform.Find("Player");
            supply = transform.Find("SupplyCrate");
            tracer = transform.Find("Tracer");
            if (player != null) playerVisual = BuildPlayer(player);
            if (supply != null) supplyVisual = BuildSupply(supply);
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("Enemy_", StringComparison.Ordinal))
                    RegisterEnemy(child);
                else if (child.name.StartsWith("EnemyWarning_", StringComparison.Ordinal))
                    warnings.Add(child);
                else if (child.name.StartsWith("EnemyBullet_", StringComparison.Ordinal))
                    StyleProjectile(child);
                else if (child.name.StartsWith("Pickup_", StringComparison.Ordinal))
                    StylePickup(child);
                else if (child.name == "Ground")
                    child.GetComponent<Renderer>().sharedMaterial = groundMaterial;
                else if (child.name == "Cover")
                    DecorateCover(child);
            }
            BuildArenaProps();
            if (player != null) lastPlayerPosition = player.position;
            if (playerVisual != null) heroMarker = playerVisual.transform.Find("HeroFootMarker");
            if (game != null) lastHealth = game.Health;
            EnsureTracerBeam();
            arenaBuilt = true;
        }

        private GameObject BuildPlayer(Transform actor)
        {
            var old = actor.GetComponent<Renderer>();
            if (old != null) old.enabled = false;
            var root = new GameObject("HeroVisual").transform;
            root.SetParent(actor, false);
            AddMarkerRing(root, Olive, HeroMarkerDiameter, "HeroFootMarker");
            MakeCapsule("Torso", root, new Vector3(0, .55f, 0), new Vector3(.48f, .68f, .34f), playerMaterial.color);
            MakeCube("Backpack", root, new Vector3(-.26f, .58f, -.02f), new Vector3(.22f, .48f, .32f), Cloth, .08f);
            MakeSphere("Head", root, new Vector3(0, 1.12f, 0), new Vector3(.31f, .31f, .31f), Skin);
            MakeCylinder("Helmet", root, new Vector3(0, 1.30f, 0), new Vector3(.37f, .12f, .37f), OliveDark, 8);
            MakeCube("LegL", root, new Vector3(-.17f, .13f, 0), new Vector3(.16f, .42f, .19f), Cloth, .05f);
            MakeCube("LegR", root, new Vector3(.17f, .13f, 0), new Vector3(.16f, .42f, .19f), Cloth, .05f);
            BuildWeapon(root, "RifleVisual", new Vector3(.32f, .73f, .16f), new Vector3(.66f, .09f, .09f), Metal, 0.12f);
            BuildWeapon(root, "SmgVisual", new Vector3(.31f, .70f, .14f), new Vector3(.46f, .10f, .10f), Metal, 0.12f);
            BuildWeapon(root, "ShotgunVisual", new Vector3(.34f, .76f, .16f), new Vector3(.78f, .12f, .12f), Wood, 0.12f);
            // CombatGame's actor transform is at capsule center (Y=.8), while the
            // Blender asset pivot is at the feet.  Keep gameplay transforms intact
            // and compensate only in the presentation child.
            if (AttachImportedModel(root, "hero", "HeroBlenderModel", new Vector3(0, -.8f, 0), Vector3.one))
                DisableChildren(root, "Torso", "Backpack", "Head", "Helmet", "LegL", "LegR");
            if (AttachImportedModel(root.Find("RifleVisual"), "rifle", "RifleBlenderModel", Vector3.zero, Quaternion.Euler(0, -90, 0), Vector3.one * .72f))
                DisableChildren(root.Find("RifleVisual"), "Stock", "Barrel", "Sight");
            if (AttachImportedModel(root.Find("SmgVisual"), "smg", "SmgBlenderModel", Vector3.zero, Quaternion.Euler(0, -90, 0), Vector3.one * .72f))
                DisableChildren(root.Find("SmgVisual"), "Stock", "Barrel", "Sight");
            if (AttachImportedModel(root.Find("ShotgunVisual"), "shotgun", "ShotgunBlenderModel", Vector3.zero, Quaternion.Euler(0, -90, 0), Vector3.one * .72f))
                DisableChildren(root.Find("ShotgunVisual"), "Stock", "Barrel", "Sight");
            root.localScale = InverseScale(actor.localScale);
            playerRig = root.gameObject.AddComponent<CombatCharacterRig>();
            playerRig.Initialize(root.Find("HeroBlenderModel"), new[]
            {
                root.Find("RifleVisual"), root.Find("SmgVisual"), root.Find("ShotgunVisual")
            });
            return root.gameObject;
        }

        private void BuildWeapon(Transform parent, string name, Vector3 localPosition, Vector3 scale, Color color, float bevel)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            group.localPosition = localPosition;
            group.localRotation = Quaternion.Euler(0, 0, -18);
            MakeCube("Stock", group, new Vector3(-scale.x * .22f, 0, 0), new Vector3(scale.x * .42f, scale.y * 1.4f, scale.z * 1.4f), color, bevel);
            MakeCube("Barrel", group, new Vector3(scale.x * .26f, 0, 0), new Vector3(scale.x * .62f, scale.y * .70f, scale.z * .70f), color, bevel);
            MakeCube("Sight", group, new Vector3(scale.x * .03f, scale.y * 1.1f, 0), new Vector3(scale.x * .08f, scale.y * .8f, scale.z * .8f), Gold, .02f);
        }

        private void RegisterEnemy(Transform actor)
        {
            var old = actor.GetComponent<Renderer>();
            if (old != null) old.enabled = false;
            var root = new GameObject("EnemyVisual").transform;
            root.SetParent(actor, false);
            root.localScale = InverseScale(actor.localScale);
            var visual = new EnemyVisual { root = root, bodyRenderer = old };
            visual.infantry = BuildEnemyVariant(root, "Infantry", Enemy, false, false);
            visual.shooter = BuildEnemyVariant(root, "Shooter", shooterMaterial.color, true, false);
            visual.charger = BuildEnemyVariant(root, "Charger", Hex("#6E6350"), false, true);
            visual.elite = BuildEnemyVariant(root, "Elite", EnemyDark, true, true);
            visual.infantryRig = visual.infantry.GetComponent<CombatCharacterRig>();
            visual.shooterRig = visual.shooter.GetComponent<CombatCharacterRig>();
            visual.chargerRig = visual.charger.GetComponent<CombatCharacterRig>();
            visual.eliteRig = visual.elite.GetComponent<CombatCharacterRig>();
            visual.chargerMarker = actor.Find("ChargerMarker_" + actor.name.Substring("Enemy_".Length));
            visual.ring = AddMarkerRing(root, EnemyDark, EnemyMarkerDiameter, "EnemyFootMarker");
            enemyVisuals.Add(actor, visual);
            previousEnemyActive[actor] = false;
            previousEnemyPositions[actor] = actor.position;
        }

        private GameObject BuildEnemyVariant(Transform parent, string name, Color clothColor, bool weapon, bool accent)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            MakeCapsule("Body", group.transform, new Vector3(0, .50f, 0), accent ? new Vector3(.52f, .72f, .40f) : new Vector3(.44f, .62f, .34f), clothColor);
            MakeSphere("Head", group.transform, new Vector3(0, 1.02f, 0), new Vector3(.27f, .27f, .27f), Skin);
            MakeCylinder("Cap", group.transform, new Vector3(0, 1.18f, 0), new Vector3(.31f, .09f, .31f), accent ? Coral : EnemyDark, 8);
            MakeCube("LegL", group.transform, new Vector3(-.13f, .12f, 0), new Vector3(.13f, .34f, .16f), EnemyDark, .04f);
            MakeCube("LegR", group.transform, new Vector3(.13f, .12f, 0), new Vector3(.13f, .34f, .16f), EnemyDark, .04f);
            if (weapon) BuildWeapon(group.transform, "Weapon", new Vector3(.28f, .65f, .13f), new Vector3(.62f, .08f, .08f), Metal, .10f);
            if (accent)
            {
                MakeCube("Shoulder", group.transform, new Vector3(.34f, .66f, 0), new Vector3(.18f, .18f, .42f), Coral, .05f);
                MakeCube("ChestPlate", group.transform, new Vector3(0, .63f, .18f), new Vector3(.42f, .24f, .08f), EnemyDark, .03f);
            }
            string modelName = name == "Infantry" ? "enemy_infantry" : name == "Shooter" ? "enemy_shooter" : name == "Charger" ? "enemy_charger" : "enemy_elite";
            if (AttachImportedModel(group.transform, modelName, name + "BlenderModel", new Vector3(0, -.8f, 0), Vector3.one * .96f))
                DisableChildren(group.transform, "Body", "Head", "Cap", "LegL", "LegR", "Shoulder", "ChestPlate");
            if (weapon && AttachImportedModel(group.transform.Find("Weapon"), name == "Shooter" ? "rifle" : "smg", name + "WeaponBlenderModel", Vector3.zero, Quaternion.Euler(0, -90, 0), Vector3.one * .58f))
                DisableChildren(group.transform.Find("Weapon"), "Stock", "Barrel", "Sight");
            var rig = group.AddComponent<CombatCharacterRig>();
            rig.Initialize(group.transform.Find(name + "BlenderModel"),
                weapon ? new[] { group.transform.Find("Weapon") } : Array.Empty<Transform>());
            group.SetActive(false);
            return group;
        }

        private GameObject BuildSupply(Transform crate)
        {
            var old = crate.GetComponent<Renderer>();
            if (old != null) old.enabled = false;
            var root = new GameObject("SupplyVisual").transform;
            root.SetParent(crate, false);
            MakeCube("Crate", root, new Vector3(0, .30f, 0), new Vector3(.86f, .58f, .72f), Wood, .06f);
            MakeCube("Lid", root, new Vector3(0, .63f, 0), new Vector3(.91f, .08f, .77f), Gold, .03f);
            MakeCube("BandX", root, new Vector3(0, .36f, 0), new Vector3(.10f, .70f, .77f), Gold, .02f);
            MakeCube("BandZ", root, new Vector3(0, .36f, 0), new Vector3(.91f, .70f, .10f), Gold, .02f);
            if (AttachImportedModel(root, "supply_crate", "SupplyBlenderModel", Vector3.zero, Vector3.one * 1.02f))
                DisableChildren(root, "Crate", "Lid", "BandX", "BandZ");
            AddMarkerRing(root, Gold, 1.25f, "SupplyRing");
            var glow = MakeSphere("Glow", root, new Vector3(0, .82f, 0), Vector3.one * .12f, Gold);
            glow.GetComponent<Renderer>().sharedMaterial = pickupMaterial;
            return root.gameObject;
        }

        private void DecorateCover(Transform cover)
        {
            var old = cover.GetComponent<Renderer>();
            if (old != null) old.enabled = false;
            var root = new GameObject("SandbagVisual").transform;
            root.SetParent(cover, false);
            // The cover cube is scaled to its collision size. Cancel that scale on
            // the art child so each imported sandbag retains its intended shape.
            root.localScale = InverseScale(cover.localScale);
            Vector3 size = cover.localScale;
            int count = Mathf.Max(1, Mathf.CeilToInt(size.x / 1.67f));
            float segmentWidth = size.x / count;
            float horizontalScale = segmentWidth / 1.67f;
            for (int i = 0; i < count; i++)
            {
                float x = -size.x * .5f + segmentWidth * (i + .5f);
                for (int layer = 0; layer < 2; layer++)
                {
                    var position = new Vector3(x, -.55f + layer * .52f, 0);
                    var modelScale = new Vector3(horizontalScale, 1f, Mathf.Min(1.15f, size.z / .56f));
                    if (AttachImportedModel(root, "sandbag", "SandbagBlenderModel", position, modelScale)) continue;
                    // A failed import must still leave visible cover art.
                    MakeCapsule("BagFallback", root, position + Vector3.up * .28f,
                        new Vector3(segmentWidth * .92f, .22f, Mathf.Min(size.z, .7f)), Sand);
                }
            }
        }

        private void BuildArenaProps()
        {
            var props = new GameObject("ArtProps").transform;
            props.SetParent(transform, false);
            // Edge foliage keeps the navigation lanes and line of sight open.
            var treeSpots = new[] { new Vector3(-15.4f, 0, -8.4f), new Vector3(15.2f, 0, -7.8f), new Vector3(-14.8f, 0, 7.6f), new Vector3(14.7f, 0, 8.1f), new Vector3(-7.2f, 0, 8.6f), new Vector3(8.5f, 0, -8.6f) };
            for (int i = 0; i < treeSpots.Length; i++) BuildTree(props, treeSpots[i], .85f + (i % 3) * .12f);
            BuildCrateStack(props, new Vector3(-12.5f, .0f, -7.4f));
            BuildCrateStack(props, new Vector3(11.9f, .0f, 6.8f));
            BuildTarp(props, new Vector3(-12f, .0f, 7.1f));
            var rocks = new[] { new Vector3(-5.8f, .1f, -8.0f), new Vector3(5.8f, .1f, 7.9f), new Vector3(13.2f, .1f, -1.7f), new Vector3(-13.2f, .1f, 1.6f) };
            foreach (var p in rocks)
            {
                var rock = new GameObject("Rock").transform;
                rock.SetParent(props, false);
                rock.localPosition = p;
                if (!AttachImportedModel(rock, "rock", "RockBlenderModel", Vector3.zero, Vector3.one))
                    MakeSphere("RockFallback", rock, Vector3.up * .14f, new Vector3(1.0f, .38f, .72f), Dirt);
            }
            for (int i = 0; i < 24; i++)
            {
                float a = i * 2.399f, r = 8.3f + (i % 4) * 1.6f;
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r * .55f);
                BuildBush(props, p, .7f + (i % 3) * .15f);
            }
            // A few painted dirt patches give the arena a hand-authored ground rhythm.
            var dirt = RuntimeMaterial("Art_Dirt", Dirt);
            foreach (var p in new[] { new Vector3(-4.4f, -.01f, -1.3f), new Vector3(6.6f, -.01f, 2.2f), new Vector3(-9.3f, -.01f, 5.6f) })
                MakeCylinder("DirtPatch", props, p, new Vector3(2.0f, .012f, 1.15f), dirt.color, 12).transform.rotation = Quaternion.Euler(0, 25, 0);
        }

        private void BuildTree(Transform parent, Vector3 p, float scale)
        {
            var root = new GameObject("Tree").transform;
            root.SetParent(parent, false); root.position = p; root.localScale = Vector3.one * scale;
            MakeCylinder("Trunk", root, new Vector3(0, .72f, 0), new Vector3(.22f, 1.45f, .22f), Wood, 7);
            MakeSphere("CrownA", root, new Vector3(0, 1.65f, 0), new Vector3(1.15f, .80f, 1.05f), Foliage);
            MakeSphere("CrownB", root, new Vector3(.25f, 2.0f, .12f), new Vector3(.75f, .66f, .72f), OliveDark);
            if (AttachImportedModel(root, "tree", "TreeBlenderModel", Vector3.zero, Vector3.one))
                DisableChildren(root, "Trunk", "CrownA", "CrownB");
        }

        private void BuildCrateStack(Transform parent, Vector3 p)
        {
            var a = MakeCube("WoodCrate_A", parent, p + new Vector3(0, .33f, 0), new Vector3(.75f, .66f, .75f), Wood, .06f);
            var b = MakeCube("WoodCrate_B", parent, p + new Vector3(.42f, .33f, .16f), new Vector3(.65f, .66f, .65f), Wood, .06f);
            var c = MakeCube("WoodCrate_C", parent, p + new Vector3(.16f, .98f, .05f), new Vector3(.62f, .58f, .62f), Wood, .06f);
            if (AttachImportedModel(parent, "crate", "CrateBlenderModel", p + new Vector3(0, .38f, 0), Vector3.one * .82f))
            {
                DisableChildren(parent, a.name, b.name, c.name);
                AttachImportedModel(parent, "crate", "CrateBlenderModel_2", p + new Vector3(.44f, .38f, .16f), Vector3.one * .70f);
                AttachImportedModel(parent, "crate", "CrateBlenderModel_3", p + new Vector3(.16f, 1.0f, .05f), Vector3.one * .68f);
            }
        }

        private void BuildTarp(Transform parent, Vector3 p)
        {
            var tarp = RuntimeMaterial("Art_Tarp", Hex("#405447"));
            MakeCube("TarpShelter", parent, p + new Vector3(0, 1.65f, 0), new Vector3(3.0f, .08f, 1.8f), tarp.color, .02f);
            MakeCylinder("Pole", parent, p + new Vector3(-1.2f, .9f, 0), new Vector3(.06f, 1.8f, .06f), Wood, 6);
            MakeCylinder("Pole", parent, p + new Vector3(1.2f, .9f, 0), new Vector3(.06f, 1.8f, .06f), Wood, 6);
            if (AttachImportedModel(parent, "tarp", "TarpBlenderModel", p + new Vector3(0, 0, 0), Vector3.one * 1.05f))
            {
                DisableChildren(parent, "TarpShelter", "Pole");
            }
        }

        private void BuildBush(Transform parent, Vector3 p, float scale)
        {
            var root = new GameObject("Bush").transform; root.SetParent(parent, false); root.position = p; root.localScale = Vector3.one * scale;
            MakeSphere("Leaf", root, new Vector3(0, .28f, 0), new Vector3(1.1f, .45f, .72f), Foliage);
            MakeSphere("LeafSmall", root, new Vector3(.35f, .42f, .08f), new Vector3(.55f, .35f, .48f), OliveDark);
            if (AttachImportedModel(root, "bush", "BushBlenderModel", Vector3.zero, Vector3.one))
                DisableChildren(root, "Leaf", "LeafSmall");
        }

        private void UpdatePlayer()
        {
            if (player == null || playerVisual == null) return;
            float dt = Mathf.Max(Time.unscaledDeltaTime, .0001f);
            Vector3 velocity = (player.position - lastPlayerPosition) / dt;
            lastPlayerPosition = player.position;
            if (game != null && game.Health < lastHealth - .01f) hitTimer = .18f;
            if (game != null) lastHealth = game.Health;
            hitTimer = Mathf.Max(0, hitTimer - dt);
            shotAimRemaining = Mathf.Max(0, shotAimRemaining - dt);
            playerVisual.transform.localScale = InverseScale(player.localScale);
            if (game.State == CombatState.Menu || game.WeaponCount == 0) visibleWeaponSlot = 0;
            else visibleWeaponSlot = Mathf.Clamp(visibleWeaponSlot, 0, game.WeaponCount - 1);
            int weaponIndex = WeaponVisualIndex(visibleWeaponSlot);
            if (playerRig != null)
            {
                playerRig.SelectWeapon(weaponIndex);
                Vector3 aim = shotAimRemaining > 0 ? lastShotDirection : game.PlayerAimDirection;
                if (aim.sqrMagnitude < .001f) aim = velocity;
                float remaining = game.ReloadAt(visibleWeaponSlot);
                var definition = game.WeaponAt(visibleWeaponSlot);
                float reload = remaining > 0 && definition != null
                    ? 1f - remaining / Mathf.Max(.01f, definition.ReloadSeconds * game.Stats.ReloadFactor) : -1f;
                playerRig.Tick(dt, velocity, aim, reload, hitTimer / .18f, game.Health <= 0 ? 1f : 0f);
            }
            if (heroMarker != null) heroMarker.localScale = MarkerScale(HeroMarkerDiameter, 1f + Mathf.Sin(time * 4f) * .035f);
        }

        private int WeaponVisualIndex(int slot)
        {
            var definition = game.State != CombatState.Menu ? game.WeaponAt(slot) : null;
            if (definition != null)
                return definition.Id == TienTuyen.Content.ContentIds.Smg ? 1 :
                    definition.Id == TienTuyen.Content.ContentIds.Shotgun ? 2 : 0;
            return game.SelectedWeapon == WeaponKind.Smg ? 1 : game.SelectedWeapon == WeaponKind.Shotgun ? 2 : 0;
        }

        private void OnWeaponFired(int slot, Vector3 start, Vector3 end)
        {
            if (!arenaBuilt || playerRig == null) return;
            visibleWeaponSlot = slot;
            lastShotDirection = end - start;
            lastShotDirection.y = 0;
            shotAimRemaining = .35f;
            presentationTracerEnd = end;
            playerRig.SnapAim(lastShotDirection);
            playerRig.Fire(WeaponVisualIndex(slot));
        }

        private void OnEnemyFired(Transform actor, Vector3 direction)
        {
            if (!enemyVisuals.TryGetValue(actor, out var visual)) return;
            visual.shotDirection = direction;
            visual.shotAimRemaining = .25f;
            // The event may arrive before this frame's variant selection in LateUpdate.
            var rig = actor.localScale.x > .85f ? visual.eliteRig : visual.shooterRig;
            if (rig != null)
            {
                rig.SnapAim(direction);
                rig.Fire();
            }
        }

        private void UpdateEnemies()
        {
            foreach (var pair in enemyVisuals)
            {
                Transform actor = pair.Key; EnemyVisual visual = pair.Value;
                bool active = actor.gameObject.activeSelf;
                bool wasActive = previousEnemyActive[actor];
                if (active != previousEnemyActive[actor] && !active && previousEnemyActive[actor])
                    SpawnBurst(previousEnemyPositions[actor], Coral);
                previousEnemyActive[actor] = active;
                float dt = Mathf.Max(Time.unscaledDeltaTime, .0001f);
                Vector3 velocity = active && wasActive ? (actor.position - previousEnemyPositions[actor]) / dt : Vector3.zero;
                previousEnemyPositions[actor] = actor.position;
                visual.root.localScale = InverseScale(actor.localScale);
                bool charger = visual.chargerMarker != null && visual.chargerMarker.gameObject.activeSelf;
                // Shooter material is assigned by CombatGame when a shooter activates.
                bool shooter = visual.bodyRenderer != null && visual.bodyRenderer.sharedMaterial != null && visual.bodyRenderer.sharedMaterial.name.IndexOf("Shooter", StringComparison.OrdinalIgnoreCase) >= 0;
                bool elite = actor.localScale.x > .85f;
                visual.infantry.SetActive(active && !shooter && !charger && !elite);
                visual.shooter.SetActive(active && shooter && !elite);
                visual.charger.SetActive(active && charger && !elite);
                visual.elite.SetActive(active && elite);
                if (visual.ring != null)
                {
                    visual.ring.SetActive(active);
                    visual.ring.transform.localScale = MarkerScale(EnemyMarkerDiameter, 1f + Mathf.Sin(time * 3f + actor.GetInstanceID() * .01f) * .04f);
                }
                if (active)
                {
                    if (!wasActive) visual.shotAimRemaining = 0;
                    visual.shotAimRemaining = Mathf.Max(0, visual.shotAimRemaining - dt);
                    Vector3 aim = visual.shotAimRemaining > 0 ? visual.shotDirection :
                        shooter && player != null ? player.position - actor.position : velocity;
                    aim.y = 0;
                    var rig = elite ? visual.eliteRig : charger ? visual.chargerRig : shooter ? visual.shooterRig : visual.infantryRig;
                    if (rig != null) rig.Tick(dt, velocity, aim);
                }
            }
        }

        private void UpdateWarnings()
        {
            foreach (var warning in warnings)
            {
                if (!warning.gameObject.activeSelf) continue;
                float pulse = .88f + Mathf.Abs(Mathf.Sin(time * 8f));
                warning.localScale = new Vector3(pulse, .02f, pulse);
                var renderer = warning.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = warningMaterial;
            }
        }

        private void UpdateSupply()
        {
            if (supply == null || supplyVisual == null) return;
            supplyVisual.SetActive(supply.gameObject.activeSelf);
            if (!supply.gameObject.activeSelf) return;
            supplyVisual.transform.localScale = Vector3.one * (1f + Mathf.Sin(time * 3f) * .045f);
            var ring = supplyVisual.transform.Find("SupplyRing");
            if (ring != null) ring.localScale = MarkerScale(1.25f, 1f + Mathf.Sin(time * 5f) * .07f);
        }

        private void StyleProjectile(Transform bullet)
        {
            bulletTransforms.Add(bullet);
            var renderer = bullet.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = projectileMaterial;
            bullet.localScale = Vector3.one * .17f;
        }

        private void StylePickup(Transform pickup)
        {
            var renderer = pickup.GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;
            var root = new GameObject("PickupVisual").transform; root.SetParent(pickup, false);
            MakeCube("Shard", root, Vector3.zero, new Vector3(.30f, .52f, .30f), Gold, .03f).transform.localRotation = Quaternion.Euler(0, 45, 0);
            AddMarkerRing(root, Gold, .68f, "PickupRing");
        }

        private void UpdateTracerAndBullets()
        {
            if (tracer != null && tracer.gameObject.activeSelf)
            {
                var renderer = tracer.GetComponent<Renderer>();
                if (renderer != null) { renderer.sharedMaterial = projectileMaterial; renderer.enabled = false; }
                float length = Mathf.Max(.28f, tracer.lossyScale.z);
                if (tracerBeam == null) EnsureTracerBeam();
                if (tracerBeam != null)
                {
                    tracerBeam.enabled = true;
                    Vector3 origin = playerRig != null && playerRig.MuzzleTransform != null
                        ? playerRig.MuzzleTransform.position : tracer.position - tracer.forward * length * .5f;
                    tracerBeam.SetPosition(0, origin);
                    tracerBeam.SetPosition(1, presentationTracerEnd);
                    float width = Mathf.Clamp(length * .008f, .025f, .045f);
                    tracerBeam.startWidth = width; tracerBeam.endWidth = width * .52f;
                }
            }
            else if (tracerBeam != null) tracerBeam.enabled = false;
            foreach (Transform child in bulletTransforms)
            {
                if (!child.gameObject.activeSelf)
                {
                    previousBulletPositions.Remove(child);
                    continue;
                }
                Vector3 previous;
                bool hadPrevious = previousBulletPositions.TryGetValue(child, out previous);
                Vector3 delta = child.position - previous;
                if (!hadPrevious || delta.sqrMagnitude < .000001f) delta = child.forward;
                if (delta.sqrMagnitude > .000001f) child.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
                child.localScale = new Vector3(.105f, .105f, .42f);
                previousBulletPositions[child] = child.position;
            }
        }

        private void EnsureTracerBeam()
        {
            if (tracerBeam != null) return;
            var go = new GameObject("TracerBeam");
            go.transform.SetParent(transform, false);
            tracerBeam = go.AddComponent<LineRenderer>();
            tracerBeam.positionCount = 2;
            tracerBeam.useWorldSpace = true;
            tracerBeam.alignment = LineAlignment.View;
            tracerBeam.textureMode = LineTextureMode.Stretch;
            tracerBeam.numCapVertices = 2;
            tracerBeam.sharedMaterial = projectileMaterial;
            tracerBeam.startWidth = .06f;
            tracerBeam.endWidth = .03f;
            tracerBeam.enabled = false;
        }

        private void SpawnBurst(Vector3 position, Color color)
        {
            var go = new GameObject("HitDeathBurst"); go.transform.SetParent(transform, true); go.transform.position = position + Vector3.up * .45f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.duration = .38f; main.startLifetime = .32f; main.startSpeed = 2.2f; main.startSize = .09f; main.maxParticles = 16; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission; emission.SetBursts(new[] { new ParticleSystem.Burst(0, 10) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .12f;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.material = RuntimeMaterial("Art_Burst", color, true);
            ps.Play();
            Destroy(go, 1.1f);
        }

        private static GameObject MakeCube(string name, Transform parent, Vector3 pos, Vector3 scale, Color color, float bevel)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = scale; ApplyMaterial(go, color); RemoveCollider(go); return go;
        }
        private static GameObject MakeSphere(string name, Transform parent, Vector3 pos, Vector3 scale, Color color)
        { var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = scale; ApplyMaterial(go, color); RemoveCollider(go); return go; }
        private static GameObject MakeCapsule(string name, Transform parent, Vector3 pos, Vector3 scale, Color color)
        { var go = GameObject.CreatePrimitive(PrimitiveType.Capsule); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = scale; ApplyMaterial(go, color); RemoveCollider(go); return go; }
        private static GameObject MakeCylinder(string name, Transform parent, Vector3 pos, Vector3 scale, Color color, int sides)
        { var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = scale; ApplyMaterial(go, color); RemoveCollider(go); return go; }
        private static void RemoveCollider(GameObject go) { var c = go.GetComponent<Collider>(); if (c != null) UnityEngine.Object.Destroy(c); }
        private static void ApplyMaterial(GameObject go, Color color) { var r = go.GetComponent<Renderer>(); if (r != null) r.sharedMaterial = RuntimeMaterial("Art_Runtime_" + color.ToString(), color); }
        private static Material RuntimeMaterial(string name, Color color, bool emission = false)
        {
            string key = name + "|" + color.ToString() + "|" + emission;
            if (MaterialCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name }; mat.color = color; mat.SetFloat("_Smoothness", .16f); mat.SetFloat("_Metallic", 0f);
            if (emission) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", color * .45f); }
            MaterialCache[key] = mat;
            return mat;
        }

        private static bool AttachImportedModel(Transform parent, string resourceName, string instanceName, Vector3 localPosition, Vector3 localScale)
        {
            return AttachImportedModel(parent, resourceName, instanceName, localPosition, Quaternion.identity, localScale);
        }

        private static bool AttachImportedModel(Transform parent, string resourceName, string instanceName, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            if (parent == null) return false;
            var prefab = Resources.Load<GameObject>("Models/" + resourceName);
            if (prefab == null)
            {
                Debug.LogWarning("Art model not found: Models/" + resourceName);
                return false;
            }
            var instance = UnityEngine.Object.Instantiate(prefab, parent);
            instance.name = instanceName;
            instance.transform.localPosition = localPosition;
            // The catalog is authored in Blender's Z-up coordinate system. Keep
            // the imported FBX files at their authored dimensions and convert
            // their local axes once at the presentation boundary. The caller's
            // -90° weapon yaw is composed after this correction, mapping source
            // +Y (the muzzle direction) to gameplay +X.
            instance.transform.localRotation = localRotation * Quaternion.Euler(-90f, 0f, 0f);
            instance.transform.localScale = localScale;
            bool hasVisibleMesh = false;
            bool hasInvalidRenderer = false;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !IsActiveInsideModel(renderer.transform, instance.transform)) continue;
                Mesh mesh = null;
                var meshRenderer = renderer as MeshRenderer;
                if (meshRenderer != null)
                {
                    var filter = meshRenderer.GetComponent<MeshFilter>();
                    if (filter != null) mesh = filter.sharedMesh;
                }
                else
                {
                    var skinned = renderer as SkinnedMeshRenderer;
                    if (skinned != null) mesh = skinned.sharedMesh;
                }
                if (mesh == null || mesh.vertexCount == 0 || mesh.bounds.size.sqrMagnitude < .000001f)
                {
                    hasInvalidRenderer = true;
                    continue;
                }
                var material = renderer.sharedMaterial;
                // Renderer.bounds can be empty while the gameplay parent is inactive
                // (for example SupplyCrate at startup). The mesh and its matrix are
                // still available and describe the art's actual size.
                if (material == null || material.shader == null || !material.shader.isSupported ||
                    renderer.localToWorldMatrix.MultiplyVector(mesh.bounds.size).sqrMagnitude < .000001f)
                {
                    hasInvalidRenderer = true;
                    continue;
                }
                hasVisibleMesh = true;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            if (hasVisibleMesh && !hasInvalidRenderer) return true;
            Debug.LogWarning("Art model has no visible mesh, valid bounds, or supported material: Models/" + resourceName, parent);
            instance.SetActive(false);
            UnityEngine.Object.Destroy(instance);
            return false;
        }

        private static bool IsActiveInsideModel(Transform node, Transform modelRoot)
        {
            for (var current = node; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == modelRoot) break;
            }
            return true;
        }

        private static void DisableChildren(Transform parent, params string[] names)
        {
            if (parent == null || names == null) return;
            foreach (string name in names)
            {
                var child = parent.Find(name);
                if (child != null) child.gameObject.SetActive(false);
            }
        }
        private static GameObject AddMarkerRing(Transform parent, Color color, float diameter, string name)
        {
            // Character actors sit at Y=.8. Keep their marker just above the
            // ground, not intersecting the articulated boots.
            float height = name == "HeroFootMarker" || name == "EnemyFootMarker" ? -.79f : -.67f;
            var ring = MakeCylinder(name, parent, new Vector3(0, height, 0), MarkerScale(diameter, 1f), color, 16);
            ring.GetComponent<Renderer>().sharedMaterial = RuntimeMaterial(name + "Mat", color, true);
            return ring;
        }
        // Pulse the footprint only; a uniform scale turns this flat cylinder into
        // a full-height column that hides the imported character's legs.
        private static Vector3 MarkerScale(float diameter, float pulse) =>
            new Vector3(diameter * pulse, MarkerThicknessScale, diameter * pulse);
        private static Vector3 InverseScale(Vector3 s) => new Vector3(Mathf.Abs(s.x) < .001f ? 1 : 1f / s.x, Mathf.Abs(s.y) < .001f ? 1 : 1f / s.y, Mathf.Abs(s.z) < .001f ? 1 : 1f / s.z);
        private static Color Hex(string value) { ColorUtility.TryParseHtmlString(value, out var c); return c; }
    }
}
