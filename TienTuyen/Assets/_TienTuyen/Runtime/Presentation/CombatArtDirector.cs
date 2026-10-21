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
        /// <summary>
        /// Seconds the presentation animates this frame: real time in play, but the game's fixed step while a film
        /// captures (Time.captureDeltaTime), so bodies keep pace with the simulation however slowly frames render.
        /// </summary>
        public static float FrameSeconds => Time.captureDeltaTime > 0f ? Time.deltaTime : Time.unscaledDeltaTime;

        private static readonly Color Olive = Hex("#728064");
        private static readonly Color OliveDark = Hex("#3F5143");
        private static readonly Color Cloth = Hex("#53634C");
        private static readonly Color Skin = Hex("#B98966");
        private static readonly Color Metal = Hex("#39433E");
        private static readonly Color Wood = Hex("#79553D");
        private static readonly Color Leather = Hex("#3B2E22"), LeatherEdge = Hex("#2C2219");
        private static readonly Color Sand = Hex("#9D8A67");
        private static readonly Color Enemy = Hex("#666158");
        private static readonly Color EnemyDark = Hex("#454A45");
        private static readonly Color Coral = Hex("#E57A5B");
        private static readonly Color Gold = Hex("#E2B65A");
        private static readonly Color Ground = Hex("#5E674F");
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
        private Transform menuStep;
        private float menuStepTop;
        private Vector3 lastShotDirection = Vector3.forward, presentationTracerEnd;
        private float shotAimRemaining;
        private readonly List<Transform> bulletTransforms = new List<Transform>();
        private readonly List<Transform> pickupVisuals = new List<Transform>();
        private CombatThirdPersonCamera thirdPersonCamera;
        private CombatFirstPersonView firstPersonView;
        // Where first-person shots leave the viewmodel, as a transform for the muzzle provider.
        private Transform firstPersonMuzzle;
        private Material tracerMaterial, bulletTrailMaterial, swishMaterial;
        // Seconds of the strike before and after contact during which the knife leaves a swish.
        private const float SwishLead = .2f, SwishFollow = .12f;
        private CombatCorpsePool corpses;
        private object presentedRun;

        private sealed class EnemyVisual
        {
            public Transform root;
            public GameObject infantry, shooter, charger, elite, sniper;
            public GameObject ring;
            public Renderer bodyRenderer;
            public Transform chargerMarker;
            public CombatCharacterRig infantryRig, shooterRig, chargerRig, eliteRig, sniperRig;
            // Swish on the knife tip of the melee bodies, emitting through the strike.
            public TrailRenderer infantrySwish, chargerSwish;
            public Vector3 shotDirection;
            public float shotAimRemaining, hitRemaining;
            // Action clip clocks; negative is idle. Gameplay supplies the timing, these only
            // carry follow-through past the damage tick and the dash cycle.
            public float meleeTime = -1f, recoveryTime = -1f, dashTime;
            public bool struck, followThrough, dashing;
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
            game.EnemyDamaged += OnEnemyDamaged;
            game.EnemyMeleeStruck += OnEnemyMeleeStruck;
        }

        private void OnDisable()
        {
            if (game == null) return;
            game.WeaponFired -= OnWeaponFired;
            game.EnemyFired -= OnEnemyFired;
            game.EnemyDamaged -= OnEnemyDamaged;
            game.EnemyMeleeStruck -= OnEnemyMeleeStruck;
        }

        private void LateUpdate()
        {
            if (!arenaBuilt) return;
            if (game != null && game.State == CombatState.Paused)
            {
                // A first encounter pauses on the activation tick; reveal its variant without advancing animation.
                foreach (var pair in enemyVisuals)
                {
                    bool active = pair.Key.gameObject.activeSelf;
                    if (!game.TryGetEnemyAction(pair.Key, out var state)) active = false;
                    pair.Value.infantry.SetActive(active && state.Role == TienTuyen.Content.EnemyRole.Infantry);
                    pair.Value.charger.SetActive(active && state.Role == TienTuyen.Content.EnemyRole.Charger);
                    pair.Value.shooter.SetActive(active && state.Role == TienTuyen.Content.EnemyRole.Shooter);
                    pair.Value.elite.SetActive(active && state.Role == TienTuyen.Content.EnemyRole.Elite);
                    pair.Value.sniper.SetActive(active && state.Role == TienTuyen.Content.EnemyRole.Sniper);
                }
                return;
            }
            if (!ReferenceEquals(presentedRun, game.Run))
            {
                presentedRun = game.Run;
                playerRig?.ResetPose();
                corpses?.Clear();
                hitTimer = shotAimRemaining = 0;
                lastHealth = game.Health;
                if (player != null) lastPlayerPosition = player.position;
            }
            time += FrameSeconds;
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
            corpses?.Tick(FrameSeconds);
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
            projectileMaterial = RuntimeMaterial("Art_Projectile", Hex("#FFB25A"), true);
            projectileMaterial.SetColor("_EmissionColor", Hex("#FF9A3C") * 4.5f);
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
                {
                    // The generated clearing replaces the flat slab; keep its collider for the camera.
                    var renderer = child.GetComponent<Renderer>();
                    renderer.sharedMaterial = groundMaterial;
                    renderer.enabled = false;
                }
            }
            // Cover emplacements and all scenery are built by CombatEnvironment.
            if (player != null) lastPlayerPosition = player.position;
            if (playerVisual != null) heroMarker = playerVisual.transform.Find("HeroFootMarker");
            // Seen from behind the shoulder the hero needs no selection ring.
            if (heroMarker != null) heroMarker.GetComponent<Renderer>().enabled = false;
            if (game != null) lastHealth = game.Health;
            presentedRun = game.Run;
            EnsureTracerBeam();
            BuildWorldAndCamera();
            arenaBuilt = true;
        }

        private void BuildWorldAndCamera()
        {
            var view = Camera.main;
            if (view != null && player != null)
            {
                thirdPersonCamera = view.GetComponent<CombatThirdPersonCamera>() ?? view.gameObject.AddComponent<CombatThirdPersonCamera>();
                thirdPersonCamera.Initialize(game, player);
                firstPersonView = view.GetComponent<CombatFirstPersonView>();
            }
            (GetComponent<CombatEnvironment>() ?? gameObject.AddComponent<CombatEnvironment>()).Build(view);
            var effects = GetComponent<CombatEffects>() ?? gameObject.AddComponent<CombatEffects>();
            effects.Initialize(game, player, PlayerMuzzle, thirdPersonCamera);
        }

        /// <summary>The muzzle shots appear to leave: the viewmodel's in first person, otherwise the hero's weapon.</summary>
        private Transform PlayerMuzzle()
        {
            if (firstPersonView != null && firstPersonView.TryGetMuzzle(out Vector3 position))
            {
                if (firstPersonMuzzle == null)
                {
                    firstPersonMuzzle = new GameObject("FirstPersonMuzzle").transform;
                    firstPersonMuzzle.SetParent(transform, false);
                }
                firstPersonMuzzle.position = position;
                return firstPersonMuzzle;
            }
            return playerRig != null ? playerRig.MuzzleTransform : null;
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
            if (AttachImportedModel(root.Find("RifleVisual"), "rifle", "RifleBlenderModel", Vector3.zero, Quaternion.Euler(0, -90, 0), Vector3.one * .72f))
                DisableChildren(root.Find("RifleVisual"), "Stock", "Barrel", "Sight");
            if (AttachImportedModel(root.Find("SmgVisual"), "smg", "SmgBlenderModel", Vector3.zero, Quaternion.Euler(0, -90, 0), Vector3.one * .72f))
                DisableChildren(root.Find("SmgVisual"), "Stock", "Barrel", "Sight");
            if (AttachImportedModel(root.Find("ShotgunVisual"), "shotgun", "ShotgunBlenderModel", Vector3.zero, Quaternion.Euler(0, -90, 0), Vector3.one * .72f))
                DisableChildren(root.Find("ShotgunVisual"), "Stock", "Barrel", "Sight");
            root.localScale = InverseScale(actor.localScale);
            playerRig = root.gameObject.AddComponent<CombatCharacterRig>();
            var weapons = new[]
            {
                root.Find("RifleVisual"), root.Find("SmgVisual"), root.Find("ShotgunVisual")
            };
            // The hero on the ValveBiped bind (HeroValveImporter); the plain hero model until it is imported.
            var profile = Resources.Load<SkinnedCharacterProfile>("Characters/PAVN/PAVN_ValveProfile");
            playerRig.StandAtEase = true;
            if (playerRig.InitializeSkinned(profile, weapons, new Vector3(0, -.8f, 0)))
            {
                DisableChildren(root, "Torso", "Backpack", "Head", "Helmet", "LegL", "LegR");
                BuildHeroKnife(root);
                BuildMenuStep(root);
            }
            else
            {
                if (AttachImportedModel(root, "hero", "HeroBlenderModel", new Vector3(0, -.8f, 0), Vector3.one))
                    DisableChildren(root, "Torso", "Backpack", "Head", "Helmet", "LegL", "LegR");
                playerRig.Initialize(root.Find("HeroBlenderModel"), weapons);
            }
            return root.gameObject;
        }

        /// <summary>The knife's slash clip runs faster than authored so its blow lands on the game's contact.</summary>
        private static float KnifeClipRate(SkinnedCharacterProfile profile) =>
            profile != null && profile.meleeContactTime > 0f ? profile.meleeContactTime / CombatRules.KnifeContactSeconds : 1f;

        /// <summary>The hero's CS2 knife and its sheath on the hip (the rig places both every frame).</summary>
        private void BuildHeroKnife(Transform root)
        {
            var model = Resources.Load<GameObject>("Models/hero_knife");
            if (model == null) return;
            var knife = UnityEngine.Object.Instantiate(model, root);
            knife.name = "HeroKnife";
            var steel = Resources.Load<Material>("Models/hero_knife_mat");
            foreach (var renderer in knife.GetComponentsInChildren<Renderer>())
            {
                if (steel != null) renderer.sharedMaterial = steel;
                renderer.shadowCastingMode = ShadowCastingMode.On;
            }
            // A leather scabbard round the blade, from the guard to past the tip (knife frame: +X the blade, +Y its edge).
            var sheath = new GameObject("HeroSheath").transform;
            sheath.SetParent(root, false);
            MakeCube("Scabbard", sheath, new Vector3(.155f, -.004f, 0f), new Vector3(.19f, .052f, .022f), Leather, .008f);
            MakeCube("Throat", sheath, new Vector3(.068f, -.004f, 0f), new Vector3(.018f, .058f, .028f), LeatherEdge, .004f);
            playerRig.AttachKnife(knife.transform, sheath);
        }

        /// <summary>The sandbags the menu hero sets his raised foot on: two bags stacked along his foot, its
        /// crown at the step's top (the rig turns it with the hero and shows it only while the foot is up).</summary>
        private void BuildMenuStep(Transform root)
        {
            var parts = new List<CombineInstance>();
            var materials = new List<Material>();
            void Bag(string part, Vector3 at, float yaw, Vector3 size)
            {
                var bag = PropLibrary.Get("sandbag", part);
                if (bag == null) return;
                var frame = Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), new Vector3(size.x / .62f, size.y / .2f, size.z / .37f)) * bag.Local;
                for (int i = 0; i < bag.Mesh.subMeshCount; i++)
                {
                    parts.Add(new CombineInstance { mesh = bag.Mesh, subMeshIndex = i, transform = frame });
                    materials.Add(bag.Materials[Mathf.Min(i, bag.Materials.Length - 1)]);
                }
            }
            Bag("bag_a_ground", new Vector3(0f, -.01f, 0f), 92f, new Vector3(.62f, .2f, .38f));
            Bag("bag_b", new Vector3(.01f, .17f, -.01f), 86f, new Vector3(.56f, .2f, .36f));
            if (parts.Count == 0) return;
            var mesh = new Mesh { name = "MenuStep" };
            var merged = new CombineInstance[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                var single = new Mesh();
                single.CombineMeshes(new[] { parts[i] }, true, true);
                merged[i] = new CombineInstance { mesh = single };
            }
            mesh.CombineMeshes(merged, false, false);
            mesh.RecalculateBounds();
            var step = new GameObject("MenuStep");
            step.transform.SetParent(root, false);
            step.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = step.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            // The top bag's crown takes the sole, pressed in a few millimetres.
            menuStepTop = mesh.bounds.max.y - .018f;
            menuStep = step.transform;
            step.SetActive(false);
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

        /// <summary>Reuse the existing rifle geometry, adding a scope and extended barrel for a readable sniper silhouette.</summary>
        private void BuildSniper(Transform parent)
        {
            var weapon = new GameObject("Weapon").transform;
            weapon.SetParent(parent, false);
            weapon.localPosition = new Vector3(.28f, .65f, .13f);
            var model = new GameObject("ScopedSniperRifle").transform;
            model.SetParent(weapon, false);
            if (!AttachImportedModel(model, "rifle", "ScopedRifleGeometry", Vector3.zero,
                    Quaternion.Euler(0, -90, 0), Vector3.one * .58f))
                BuildWeapon(model, "RifleGeometry", Vector3.zero, new Vector3(.85f, .08f, .08f), Metal, .04f);
            var steel = Hex("#343B3F");
            MakeCube("ScopeRearMount", model, new Vector3(-.07f, .11f, 0), new Vector3(.035f, .10f, .045f), steel, .005f);
            MakeCube("ScopeFrontMount", model, new Vector3(.12f, .11f, 0), new Vector3(.035f, .10f, .045f), steel, .005f);
            var scope = MakeCylinder("Scope", model, new Vector3(.03f, .19f, 0), new Vector3(.07f, .34f, .07f), steel, 12);
            scope.transform.localRotation = Quaternion.Euler(0, 0, 90);
            var lens = MakeCylinder("ScopeLens", model, new Vector3(.205f, .19f, 0), new Vector3(.065f, .012f, .065f), Hex("#365B5C"), 12);
            lens.transform.localRotation = Quaternion.Euler(0, 0, 90);
            var barrel = MakeCylinder("ExtendedBarrel", model, new Vector3(.68f, .035f, 0), new Vector3(.025f, .32f, .025f), steel, 10);
            barrel.transform.localRotation = Quaternion.Euler(0, 0, 90);
            MakeCube("SniperMuzzle", model, new Vector3(.85f, .035f, 0), new Vector3(.018f, .03f, .03f), Hex("#101719"), .003f);
        }

        private void RegisterEnemy(Transform actor)
        {
            var old = actor.GetComponent<Renderer>();
            if (old != null) old.enabled = false;
            var root = new GameObject("EnemyVisual").transform;
            root.SetParent(actor, false);
            root.localScale = InverseScale(actor.localScale);
            var visual = new EnemyVisual { root = root, bodyRenderer = old };
            visual.infantry = BuildEnemyVariant(root, "Infantry", Enemy, true, false);
            visual.shooter = BuildEnemyVariant(root, "Shooter", shooterMaterial.color, true, false);
            visual.sniper = BuildEnemyVariant(root, "Sniper", Hex("#827354"), true, false);
            visual.sniperRig = visual.sniper.GetComponent<CombatCharacterRig>();
            visual.charger = BuildEnemyVariant(root, "Charger", Hex("#6E6350"), true, true);
            visual.elite = BuildEnemyVariant(root, "Elite", EnemyDark, true, true);
            visual.infantryRig = visual.infantry.GetComponent<CombatCharacterRig>();
            visual.shooterRig = visual.shooter.GetComponent<CombatCharacterRig>();
            visual.chargerRig = visual.charger.GetComponent<CombatCharacterRig>();
            visual.infantrySwish = visual.infantry.GetComponentInChildren<TrailRenderer>(true);
            visual.chargerSwish = visual.charger.GetComponentInChildren<TrailRenderer>(true);
            visual.eliteRig = visual.elite.GetComponent<CombatCharacterRig>();
            visual.chargerMarker = actor.Find("ChargerMarker_" + actor.name.Substring("Enemy_".Length));
            // Only a role flag here: the gameplay cube would sit in the Charger's chest.
            if (visual.chargerMarker != null) visual.chargerMarker.GetComponent<Renderer>().enabled = false;
            visual.ring = AddMarkerRing(root, EnemyDark, EnemyMarkerDiameter, "EnemyFootMarker");
            enemyVisuals.Add(actor, visual);
            previousEnemyActive[actor] = false;
            previousEnemyPositions[actor] = actor.position;
        }

        private GameObject BuildEnemyVariant(Transform parent, string name, Color clothColor, bool weapon, bool accent)
        {
            bool sniper = name == "Sniper";
            bool ranged = name != "Infantry" && name != "Charger";
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            MakeCapsule("Body", group.transform, new Vector3(0, .50f, 0), accent ? new Vector3(.52f, .72f, .40f) : new Vector3(.44f, .62f, .34f), clothColor);
            MakeSphere("Head", group.transform, new Vector3(0, 1.02f, 0), new Vector3(.27f, .27f, .27f), Skin);
            MakeCylinder("Cap", group.transform, new Vector3(0, 1.18f, 0), new Vector3(.31f, .09f, .31f), accent ? Coral : EnemyDark, 8);
            MakeCube("LegL", group.transform, new Vector3(-.13f, .12f, 0), new Vector3(.13f, .34f, .16f), EnemyDark, .04f);
            MakeCube("LegR", group.transform, new Vector3(.13f, .12f, 0), new Vector3(.13f, .34f, .16f), EnemyDark, .04f);
            if (weapon)
            {
                if (sniper) BuildSniper(group.transform);
                else if (ranged) BuildWeapon(group.transform, "Weapon", new Vector3(.28f, .65f, .13f), new Vector3(.62f, .08f, .08f), Metal, .10f);
                else BuildDagger(group.transform, "Weapon", new Vector3(.27f, .61f, .12f));
            }
            if (accent)
            {
                MakeCube("Shoulder", group.transform, new Vector3(.34f, .66f, 0), new Vector3(.18f, .18f, .42f), Coral, .05f);
                MakeCube("ChestPlate", group.transform, new Vector3(0, .63f, .18f), new Vector3(.42f, .24f, .08f), EnemyDark, .03f);
            }
            string modelName = name == "Infantry" ? "enemy_infantry" : (name == "Shooter" || sniper) ? "enemy_shooter" : name == "Charger" ? "enemy_charger" : "enemy_elite";
            // The Shooter carries the SMG (its profile's MP5-SD clip set), the Elite the rifle (the AK).
            string gun = name == "Shooter" ? "smg" : "rifle";
            if (weapon && ranged && !sniper && AttachImportedModel(group.transform.Find("Weapon"), gun, name == "Shooter" ? "SmgWeaponBlenderModel" : "RifleWeaponBlenderModel",
                    Vector3.zero, Quaternion.Euler(0, -90, 0), Vector3.one * .58f))
                DisableChildren(group.transform.Find("Weapon"), "Stock", "Barrel", "Sight");
            var rig = group.AddComponent<CombatCharacterRig>();
            var weapons = weapon ? new[] { group.transform.Find("Weapon") } : Array.Empty<Transform>();
            // Reuse the existing shooter body and animation; the sniper keeps its own geometry and fitted hand sockets.
            var profile = Resources.Load<SkinnedCharacterProfile>("Characters/Enemies/" + (sniper ? "Shooter" : name) + "ValveProfile");
            if (rig.InitializeSkinned(profile, weapons, new Vector3(0, -.8f, 0)))
                DisableChildren(group.transform, "Body", "Head", "Cap", "LegL", "LegR", "Shoulder", "ChestPlate");
            else
            {
                if (AttachImportedModel(group.transform, modelName, name + "BlenderModel", new Vector3(0, -.8f, 0), Vector3.one * .96f))
                    DisableChildren(group.transform, "Body", "Head", "Cap", "LegL", "LegR", "Shoulder", "ChestPlate");
                rig.Initialize(group.transform.Find(name + "BlenderModel"), weapons);
            }
            group.SetActive(false);
            return group;
        }

        /// <summary>
        /// A USMC Ka-Bar (Wenedi, CC BY): native +X is the blade, spine up; its handle renderer is named Grip so the
        /// rig closes the fist on its centre.
        /// </summary>
        private void BuildDagger(Transform parent, string name, Vector3 position)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            root.transform.localRotation = Quaternion.Euler(0, -90, 0);
            // The FBX is in centimetres with the blade along -Z, spine +Y.
            AttachImportedModel(root.transform, "knife", "KnifeModel", Vector3.zero, Quaternion.Euler(0, -90, 0) * Quaternion.Euler(90, 0, 0), Vector3.one * .01f);
            // At gameplay distance a 30 cm knife is a few pixels: a short steel swish carries the strike.
            var tip = new GameObject("DaggerSwish").transform;
            tip.SetParent(root.transform, false);
            tip.localPosition = new Vector3(.15f, 0, 0);
            if (swishMaterial == null) swishMaterial = ProceduralArt.Particle("Art_KnifeSwish", true, new Color(1.3f, 1.35f, 1.4f, .55f));
            var trail = tip.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = swishMaterial;
            trail.time = .09f;
            trail.startWidth = .05f;
            trail.endWidth = 0f;
            trail.minVertexDistance = .02f;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.emitting = false;
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
            // The model's pivot is its base: stand it, and the glow above its lid, on the ground under the crate's
            // spawn height (.36).
            var ground = new Vector3(0, -.36f / crate.lossyScale.y, 0);
            if (AttachImportedModel(root, "supply_crate", "SupplyBlenderModel", ground, Vector3.one * 1.02f))
                DisableChildren(root, "Crate", "Lid", "BandX", "BandZ");
            AddGlowRing(root, Gold, 1.25f, "SupplyRing", .36f);
            var glow = MakeSphere("Glow", root, ground + new Vector3(0, .82f, 0), Vector3.one * .12f, Gold);
            glow.GetComponent<Renderer>().sharedMaterial = pickupMaterial;
            return root.gameObject;
        }

        private void UpdatePlayer()
        {
            if (player == null || playerVisual == null) return;
            float dt = Mathf.Max(FrameSeconds, .0001f);
            Vector3 velocity = (player.position - lastPlayerPosition) / dt;
            lastPlayerPosition = player.position;
            if (game != null && game.Health < lastHealth - .01f) hitTimer = .18f;
            if (game != null) lastHealth = game.Health;
            hitTimer = Mathf.Max(0, hitTimer - dt);
            shotAimRemaining = Mathf.Max(0, shotAimRemaining - dt);
            playerVisual.transform.localScale = InverseScale(player.localScale);
            // The weapon in hand is the game's active slot; during a draw that is already the new one.
            int slot = game.State == CombatState.Menu || game.WeaponCount == 0 ? 0 : Mathf.Clamp(game.ActiveWeaponSlot, 0, game.WeaponCount - 1);
            int weaponIndex = WeaponVisualIndex(slot);
            if (playerRig != null)
            {
                playerRig.ExternalLauncher=game.LauncherEquipped;
                playerRig.SelectWeapon(weaponIndex);
                playerRig.DrawProgress = game.State == CombatState.Playing && game.IsSwitching ? game.SwitchProgress : -1f;
                playerRig.Crouch = game.Crouch;
                playerRig.Walk = game.Walk;
                // On the menu shot the hero stands relaxed and turned to the camera; gameplay aim and fire stay out of it.
                bool menuShot = thirdPersonCamera != null && thirdPersonCamera.MenuBlend > .4f;
                playerRig.Relaxed = menuShot;
                bool knifeOut = game.State == CombatState.Playing && game.SelectedEntry == game.WeaponCount;
                playerRig.KnifeInHand = knifeOut;
                playerRig.KnifeSwing = game.State == CombatState.Playing && game.KnifeSwinging
                    ? game.KnifeSwingTime * KnifeClipRate(playerRig.Profile) : -1f;
                // The menu hero rests the weapon on his shoulder; the intro title over the valley keeps the low carry.
                playerRig.ShoulderRest = menuShot && thirdPersonCamera.TitleVista <= 0f;
                // Aiming down the sights shoulders the weapon with the bore on the crosshair; otherwise it rides the low-ready.
                playerRig.AimWeight = thirdPersonCamera != null ? thirdPersonCamera.AimBlend : 0f;
                playerRig.AimPoint = shotAimRemaining > 0 ? presentationTracerEnd :
                    thirdPersonCamera != null ? thirdPersonCamera.AimPoint : (Vector3?)null;
                Vector3 aim = menuShot ? CombatThirdPersonCamera.MenuHeroFacing :
                    shotAimRemaining > 0 ? lastShotDirection : game.PlayerAimDirection;
                if (aim.sqrMagnitude < .001f) aim = velocity;
                float remaining = game.ReloadAt(slot);
                playerRig.ReloadSeconds = game.ReloadDurationAt(slot);
                var rounds = game.WeaponAt(slot)?.RoundReload;
                playerRig.ReloadPhases = rounds.HasValue ? new Vector2(rounds.Value.StartSeconds, rounds.Value.EndSeconds) : Vector2.zero;
                float reload = remaining > 0 ? 1f - remaining / Mathf.Max(.01f, playerRig.ReloadSeconds) : -1f;
                playerRig.Tick(dt, velocity, aim, reload, hitTimer / .18f, game.Health <= 0 ? 1f : 0f);
                if (menuStep != null)
                {
                    bool stepping = playerRig.StepFrame(out Vector3 top, out Quaternion turn);
                    if (menuStep.gameObject.activeSelf != stepping) menuStep.gameObject.SetActive(stepping);
                    if (stepping) menuStep.SetPositionAndRotation(top - Vector3.up * menuStepTop, turn);
                }
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
            if(game.WeaponAt(slot)?.Id==TienTuyen.Content.ContentIds.Rpg18)return;
            if (!arenaBuilt || playerRig == null) return;
            lastShotDirection = end - start;
            lastShotDirection.y = 0;
            shotAimRemaining = .35f;
            presentationTracerEnd = end;
            // The shouldered bore follows the shot actually taken (auto-aim can differ from the crosshair).
            playerRig.AimPoint = end;
            playerRig.SnapAim(lastShotDirection);
            playerRig.Fire(WeaponVisualIndex(slot));
        }

        private void OnEnemyFired(Transform actor, Vector3 direction)
        {
            if (!enemyVisuals.TryGetValue(actor, out var visual)) return;
            visual.shotDirection = direction;
            visual.shotAimRemaining = .25f;
            // The event may arrive before this frame's variant selection in LateUpdate.
            var rig = game.TryGetEnemyAction(actor, out var shotAction) && shotAction.Role == TienTuyen.Content.EnemyRole.Sniper
                ? visual.sniperRig : actor.localScale.x > .85f ? visual.eliteRig : visual.shooterRig;
            if (rig != null)
            {
                if (shotAction.Role == TienTuyen.Content.EnemyRole.Sniper) rig.FixedMuzzlePosition = shotAction.ShotOrigin;
                rig.SnapAim(direction);
                rig.Fire();
            }
        }

        private void UpdateEnemies()
        {
            foreach (var pair in enemyVisuals)
            {
                Transform actor = pair.Key; EnemyVisual visual = pair.Value;
                if(game.IsCommanderActor(actor)){visual.root.gameObject.SetActive(false);continue;}
                visual.root.gameObject.SetActive(true);
                bool active = actor.gameObject.activeSelf;
                bool wasActive = previousEnemyActive[actor];
                previousEnemyActive[actor] = active;
                float dt = Mathf.Max(FrameSeconds, .0001f);
                Vector3 velocity = active && wasActive ? (actor.position - previousEnemyPositions[actor]) / dt : Vector3.zero;
                previousEnemyPositions[actor] = actor.position;
                visual.root.localScale = InverseScale(actor.localScale);
                bool charger = visual.chargerMarker != null && visual.chargerMarker.gameObject.activeSelf;
                // Shooter material is assigned by CombatGame when a shooter activates.
                bool shooter = visual.bodyRenderer != null && visual.bodyRenderer.sharedMaterial != null && visual.bodyRenderer.sharedMaterial.name.IndexOf("Shooter", StringComparison.OrdinalIgnoreCase) >= 0;
                bool sniper = game.TryGetEnemyAction(actor, out var enemyState) && enemyState.Role == TienTuyen.Content.EnemyRole.Sniper;
                bool elite = actor.localScale.x > .85f;
                visual.infantry.SetActive(active && !shooter && !charger && !elite);
                visual.shooter.SetActive(active && shooter && !elite && !sniper);
                visual.sniper.SetActive(active && sniper);
                visual.charger.SetActive(active && charger && !elite);
                visual.elite.SetActive(active && elite);
                if (visual.ring != null)
                {
                    visual.ring.SetActive(active);
                    visual.ring.transform.localScale = MarkerScale(EnemyMarkerDiameter, 1f + Mathf.Sin(time * 3f + actor.GetInstanceID() * .01f) * .04f);
                }
                if (active)
                {
                    if (!wasActive)
                    {
                        visual.shotAimRemaining = visual.hitRemaining = 0;
                        visual.meleeTime = visual.recoveryTime = -1f;
                        visual.dashTime = 0;
                        visual.struck = visual.followThrough = visual.dashing = false;
                        if (visual.infantrySwish != null) visual.infantrySwish.Clear();
                        if (visual.chargerSwish != null) visual.chargerSwish.Clear();
                    }
                    visual.shotAimRemaining = Mathf.Max(0, visual.shotAimRemaining - dt);
                    visual.hitRemaining = Mathf.Max(0, visual.hitRemaining - dt);
                    Vector3 aim = visual.shotAimRemaining > 0 ? visual.shotDirection :
                        shooter && player != null ? player.position - actor.position : velocity;
                    if (shooter && enemyState.Aiming) aim = enemyState.AimDirection;
                    aim.y = 0;
                    var rig = sniper ? visual.sniperRig : elite ? visual.eliteRig : charger ? visual.chargerRig : shooter ? visual.shooterRig : visual.infantryRig;
                    if (rig == null) continue;
                    var action = shooter ? default : EnemyAction(actor, visual, rig.Profile, dt, velocity.magnitude, ref aim);
                    // Ranged enemies change magazines on the game's reload timer.
                    float reload = -1f;
                    if (shooter && game.TryGetEnemyAction(actor, out var ranged) && ranged.ReloadRemaining > 0f)
                    {
                        rig.ReloadSeconds = ranged.ReloadSeconds;
                        reload = 1f - ranged.ReloadRemaining / Mathf.Max(.01f, ranged.ReloadSeconds);
                    }
                    if (sniper)
                    {
                        rig.AimWeight = enemyState.Aiming ? 1f : 0f;
                        rig.FixedMuzzlePosition = enemyState.Aiming || visual.shotAimRemaining > 0 ? enemyState.ShotOrigin : (Vector3?)null;
                    }
                    rig.Tick(dt, velocity, aim, reload, visual.hitRemaining / .18f, 0f, action);
                    var swish = rig == visual.infantryRig ? visual.infantrySwish : rig == visual.chargerRig ? visual.chargerSwish : null;
                    if (swish != null)
                        swish.emitting = action.kind == CharacterActionKind.Melee &&
                            action.time >= rig.Profile.meleeContactTime - SwishLead && action.time <= rig.Profile.meleeContactTime + SwishFollow;
                }
            }
        }

        private void OnEnemyMeleeStruck(Transform actor)
        {
            if (enemyVisuals.TryGetValue(actor, out var visual)) visual.struck = true;
        }

        /// <summary>
        /// Clip and time of the melee or charge action this tick. The windup is read from the
        /// gameplay cooldown so the contact frame lands on the damage tick; the telegraph from its
        /// progress; the dash cycle from the distance actually travelled.
        /// </summary>
        private CharacterAction EnemyAction(Transform actor, EnemyVisual visual, SkinnedCharacterProfile profile, float dt, float speed, ref Vector3 aim)
        {
            if (profile == null || !game.TryGetEnemyAction(actor, out var state)) return default;
            if (state.Kind == EnemyActionKind.Melee && profile.meleeAttack != null)
            {
                float contact = profile.meleeContactTime;
                if (visual.struck)
                {
                    visual.struck = false;
                    visual.followThrough = true;
                    visual.meleeTime = contact;
                }
                else if (visual.followThrough)
                {
                    visual.meleeTime += dt;
                    if (visual.meleeTime >= profile.meleeAttack.length) visual.followThrough = false;
                }
                if (!visual.followThrough)
                    visual.meleeTime = state.InMeleeReach && state.AttackCooldownRemaining <= contact ? contact - state.AttackCooldownRemaining : -1f;
                // In reach the guard already faces the player, so the windup never starts with a turn.
                if (player != null && (state.InMeleeReach || visual.meleeTime >= 0f)) aim = player.position - actor.position;
                if (visual.meleeTime < 0f) return default;
                return new CharacterAction(CharacterActionKind.Melee, visual.meleeTime);
            }
            if (state.Kind == EnemyActionKind.ChargeTelegraph || state.Kind == EnemyActionKind.ChargeDash)
            {
                aim = state.ChargeDirection;
                visual.recoveryTime = -1f;
                if (state.Kind == EnemyActionKind.ChargeTelegraph)
                {
                    visual.dashing = false;
                    visual.dashTime = 0;
                    return profile.chargeTelegraph != null
                        ? new CharacterAction(CharacterActionKind.ChargeTelegraph, state.TelegraphProgress * profile.chargeTelegraph.length) : default;
                }
                visual.dashing = true;
                if (profile.chargeDash == null || profile.chargeDashCycleDistance <= 0f) return default;
                visual.dashTime += speed * dt / profile.chargeDashCycleDistance * profile.chargeDash.length;
                return new CharacterAction(CharacterActionKind.ChargeDash, visual.dashTime);
            }
            if (visual.dashing)
            {
                visual.dashing = false;
                visual.recoveryTime = 0f;
            }
            if (visual.recoveryTime < 0f || profile.chargeRecovery == null) return default;
            // Walking off cancels the recovery; locomotion takes over. The first frame still
            // measures the last dash step, so it never cancels.
            if (speed > .5f && visual.recoveryTime > 0f || visual.recoveryTime >= profile.chargeRecovery.length)
            {
                visual.recoveryTime = -1f;
                return default;
            }
            var recovery = new CharacterAction(CharacterActionKind.ChargeRecovery, visual.recoveryTime);
            visual.recoveryTime += dt;
            return recovery;
        }

        private void OnEnemyDamaged(Transform actor, float damage, bool critical, bool killed)
        {
            if(game.IsCommanderActor(actor))return;
            if (!enemyVisuals.TryGetValue(actor, out var visual)) return;
            visual.hitRemaining = .18f;
            if (!killed) return;
            // The event is emitted before the actor is returned to the gameplay pool.
            // Copy presentation data, never retain the pooled actor as a corpse parent.
            bool shooter = visual.bodyRenderer != null && visual.bodyRenderer.sharedMaterial != null &&
                visual.bodyRenderer.sharedMaterial.name.IndexOf("Shooter", StringComparison.OrdinalIgnoreCase) >= 0;
            bool sniper = game.TryGetEnemyAction(actor, out var deadState) && deadState.Role == TienTuyen.Content.EnemyRole.Sniper;
            var rig = sniper ? visual.sniperRig : actor.localScale.x > .85f ? visual.eliteRig : shooter ? visual.shooterRig :
                visual.chargerMarker != null && visual.chargerMarker.gameObject.activeSelf ? visual.chargerRig : visual.infantryRig;
            if (rig == null || !rig.IsSkinned) return;
            if (corpses == null) corpses = gameObject.AddComponent<CombatCorpsePool>();
            Vector3 facing = rig.PoseRoot != null ? rig.PoseRoot.forward : actor.forward;
            corpses.Spawn(rig.Profile, actor.position, facing);
        }

        private void UpdateWarnings()
        {
            foreach (var warning in warnings)
            {
                if (!warning.gameObject.activeSelf || game.IsCommanderWarning(warning)) continue;
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
            if (renderer != null)
            {
                renderer.sharedMaterial = projectileMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            bullet.localScale = Vector3.one * .17f;
            if (bulletTrailMaterial == null) bulletTrailMaterial = ProceduralArt.Particle("Art_BulletTrail", true, new Color(4f, 1.6f, .5f, 1f));
            var trail = bullet.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = bulletTrailMaterial;
            trail.time = .16f;
            trail.startWidth = .06f;
            trail.endWidth = 0f;
            trail.minVertexDistance = .05f;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.emitting = false;
        }

        private void StylePickup(Transform pickup)
        {
            var renderer = pickup.GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;
            var root = new GameObject("PickupVisual").transform; root.SetParent(pickup, false);
            pickupVisuals.Add(root);
            MakeCube("Shard", root, Vector3.zero, new Vector3(.30f, .52f, .30f), Gold, .03f).transform.localRotation = Quaternion.Euler(0, 45, 0);
            AddGlowRing(root, Gold, .68f, "PickupRing", .23f);
        }

        private void UpdateTracerAndBullets()
        {
            foreach (var visual in pickupVisuals)
            {
                if (!visual.gameObject.activeInHierarchy) continue;
                visual.localRotation = Quaternion.Euler(0, time * 120f, 0);
                visual.localPosition = new Vector3(0, .12f + Mathf.Sin(time * 3.2f + visual.GetInstanceID()) * .06f, 0);
            }
            if (tracer != null && tracer.gameObject.activeSelf)
            {
                var renderer = tracer.GetComponent<Renderer>();
                if (renderer != null) { renderer.sharedMaterial = projectileMaterial; renderer.enabled = false; }
                float length = Mathf.Max(.28f, tracer.lossyScale.z);
                if (tracerBeam == null) EnsureTracerBeam();
                if (tracerBeam != null)
                {
                    tracerBeam.enabled = true;
                    Transform muzzle = PlayerMuzzle();
                    Vector3 origin = muzzle != null ? muzzle.position : tracer.position - tracer.forward * length * .5f;
                    tracerBeam.SetPosition(0, origin);
                    tracerBeam.SetPosition(1, presentationTracerEnd);
                    float width = Mathf.Clamp(length * .004f, .018f, .03f);
                    tracerBeam.startWidth = width; tracerBeam.endWidth = width * .6f;
                }
            }
            else if (tracerBeam != null) tracerBeam.enabled = false;
            foreach (Transform child in bulletTransforms)
            {
                var trail = child.GetComponent<TrailRenderer>();
                if (!child.gameObject.activeSelf)
                {
                    previousBulletPositions.Remove(child);
                    continue;
                }
                Vector3 previous;
                bool hadPrevious = previousBulletPositions.TryGetValue(child, out previous);
                if (trail != null && !hadPrevious)
                {
                    // Pooled bullets teleport on reuse; never streak from the old spot.
                    trail.Clear();
                    trail.emitting = true;
                }
                Vector3 delta = child.position - previous;
                if (!hadPrevious || delta.sqrMagnitude < .000001f) delta = child.forward;
                if (delta.sqrMagnitude > .000001f) child.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
                // A thin hot needle along the flight, not a ball: bloom swells a round head into a white blob.
                child.localScale = new Vector3(.05f, .05f, .7f);
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
            tracerMaterial = ProceduralArt.Particle("Art_TracerBeam", true, new Color(1.6f, 1.1f, .5f, 1f));   // near bloom threshold: a hot thread, not a glowing bar
            tracerBeam.sharedMaterial = tracerMaterial;
            tracerBeam.shadowCastingMode = ShadowCastingMode.Off;
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
            // Soft glowing dots: an opaque Lit material drew every particle as a flat square.
            string key = "Art_Burst|" + color;
            if (!MaterialCache.TryGetValue(key, out var burst) || burst == null) MaterialCache[key] = burst = ProceduralArt.Particle("Art_Burst", true, color * 2f);
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = burst;
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
        // Supply and pickup rings: a soft glowing rim over a faint fill, a quad at a marker cylinder's top face,
        // so the same scale pulses it. Lifted to rest just above the ground under a parent standing restHeight
        // above it (its spawn height); the -.67 of the foot markers sank it or left it z-fighting the ground.
        private static GameObject AddGlowRing(Transform parent, Color color, float diameter, string name, float restHeight)
        {
            var ring = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0, (.035f - restHeight) / parent.lossyScale.y - MarkerThicknessScale, 0);
            ring.transform.localScale = MarkerScale(diameter, 1f);
            if (glowRingQuad == null)
            {
                glowRingQuad = new Mesh { name = "GlowRingQuad" };
                glowRingQuad.SetVertices(new[] { new Vector3(-.5f, 1f, -.5f), new Vector3(-.5f, 1f, .5f), new Vector3(.5f, 1f, .5f), new Vector3(.5f, 1f, -.5f) });
                glowRingQuad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
                glowRingQuad.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
                glowRingQuad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
                glowRingQuad.RecalculateBounds();
            }
            ring.GetComponent<MeshFilter>().sharedMesh = glowRingQuad;
            string key = "GlowRing|" + color;
            if (!MaterialCache.TryGetValue(key, out var material) || material == null)
            {
                // Bright enough to stay the supply cue in the shade and at dusk, below the bloom threshold in full sun.
                MaterialCache[key] = material = ProceduralArt.Particle("Art_" + name, false, new Color(color.r * 1.5f, color.g * 1.4f, color.b * 1.1f, 1f));
                material.SetTexture("_BaseMap", GlowRingTexture());
                material.mainTexture = GlowRingTexture();
            }
            var renderer = ring.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ring;
        }
        private static Mesh glowRingQuad;
        private static Texture2D glowRing;
        private static Texture2D GlowRingTexture()
        {
            if (glowRing != null) return glowRing;
            const int size = 128;
            glowRing = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "GlowRing", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f) / size * 2f - 1f, dy = (y + .5f) / size * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                float rim = Mathf.Exp(-Mathf.Pow((r - .84f) / .06f, 2f));
                float halo = Mathf.Exp(-Mathf.Pow((r - .84f) / .15f, 2f)) * .35f;
                float fill = r < .84f ? .16f * Mathf.Pow(r / .84f, 1.5f) : 0f;
                float alpha = Mathf.Clamp01(Mathf.Max(rim, Mathf.Max(halo, fill))) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.95f, 1f, r)));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
            glowRing.SetPixels32(pixels);
            glowRing.Apply(true, true);
            return glowRing;
        }
        // Pulse the footprint only; a uniform scale turns this flat cylinder into
        // a full-height column that hides the imported character's legs.
        private static Vector3 MarkerScale(float diameter, float pulse) =>
            new Vector3(diameter * pulse, MarkerThicknessScale, diameter * pulse);
        private static Vector3 InverseScale(Vector3 s) => new Vector3(Mathf.Abs(s.x) < .001f ? 1 : 1f / s.x, Mathf.Abs(s.y) < .001f ? 1 : 1f / s.y, Mathf.Abs(s.z) < .001f ? 1 : 1f / s.z);
        private static Color Hex(string value) { ColorUtility.TryParseHtmlString(value, out var c); return c; }
    }
}
