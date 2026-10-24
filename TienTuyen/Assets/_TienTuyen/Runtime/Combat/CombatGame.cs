using System;
using TienTuyen.Content;
using TienTuyen.Progression;
using TienTuyen.Waves;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TienTuyen.Combat
{
    /// <summary>Self-contained graybox combat loop for the three-wave P1 milestone.</summary>
    public sealed partial class CombatGame : MonoBehaviour
    {
        private const float GroundY = 0.8f;
        private const float JumpHeight = 1.2f;
        private const float Gravity = -20f;

        private float verticalVelocity;

        public Material GroundMaterial, CoverMaterial, PlayerMaterial, EnemyMaterial;
        public Material ShooterMaterial, ProjectileMaterial, PickupMaterial, WarningMaterial;

        // Presentation observations only: these never determine hit tests or damage.
        // Emit once for every accepted shot, including shots while the old tracer is visible.
        public event Action<int, Vector3, Vector3> WeaponFired;
        public event Action<Transform, Vector3> EnemyFired;
        /// <summary>Where an accepted player shot ended and whether it struck an enemy.</summary>
        public event Action<Vector3, bool> ShotImpact;
        /// <summary>Enemy actor, damage applied, critical hit, killed.</summary>
        public event Action<Transform, float, bool, bool> EnemyDamaged;
        /// <summary>World hit position, actual HP removed, headshot, random critical.</summary>
        public event Action<Vector3, float, bool, bool> DamageFeedback;
        /// <summary>World position of the attacker and the damage the player took.</summary>
        public event Action<Vector3, float> PlayerDamaged;
        /// <summary>A melee enemy swung: raised on every attack, also when invulnerability absorbs the damage.</summary>
        public event Action<Transform> EnemyMeleeStruck;
        public event Action<Transform> EnemyMeleeSwing;
        public event Action<Transform, EnemyActionKind> EnemyChargeStarted;
        public event Action<Vector3, bool> SurfaceImpact;
        public event Action<Vector3> BulletNearMiss;
        public event Action<Vector3> PickupCollected;
        public event Action<Vector3> SupplySpawned, SupplyClaimed;
        public event Action<int> SupplyMilestone;
        public event Action<int, float, int> ReloadStarted;
        public event Action<Transform, float> EnemyReloadStarted;
        /// <summary>Actors were cleared by a wave transition, death, menu or restart; never a hit or collection.</summary>
        public event Action TransientsCleared;

        /// <summary>True once a view controller has supplied manual aim; tests and tools keep auto-aim.</summary>
        public bool ManualAim => manualAim;
        /// <summary>Manual aim only: the crosshair ray currently rests on a reachable enemy.</summary>
        public bool AimOnTarget { get; private set; }
        /// <summary>The crouch input is held (player only).</summary>
        public bool Crouching => crouchHeld;
        /// <summary>How far down the player is, 0 standing to 1 crouched, eased over <see cref="CombatRules.CrouchSeconds"/>.</summary>
        public float Crouch { get; private set; }
        public float PlayerHeight => Mathf.Lerp(CombatRules.PlayerStandHeight, CombatRules.PlayerCrouchHeight, Crouch);
        /// <summary>How far the player has slowed to a walk, 0 running to 1 walking, eased over <see cref="CombatRules.WalkSeconds"/>; a crouch wins.</summary>
        public float Walk { get; private set; }
        private bool walkHeld;
        public Vector3 PlayerAimDirection
        {
            get
            {
                if (manualAim) return aimDirection;
                if (player == null || targetIndex < 0 || !enemies[targetIndex].active) return Vector3.zero;
                Vector3 direction = enemies[targetIndex].body.transform.position - player.position;
                direction.y = 0;
                return direction.normalized;
            }
        }

        public CombatState State { get; private set; } = CombatState.Menu;
        public WeaponKind SelectedWeapon { get; private set; } = WeaponKind.Rifle;
        public float Health { get; private set; } = CombatRules.PlayerMaxHealth;
        public float MaxHealth => CombatRules.PlayerMaxHealth + (Run == null ? 0 : Run.Stats.MaxHealth - 100f);
        public int Wave { get; private set; } = 1;
        public float WaveRemaining { get; private set; } = CombatRules.WaveSeconds;
        public float WaveLength => WaveDuration(Wave);
        public int MaxWave => CombatRules.MaxWaves;
        public float RunElapsed { get; private set; }
        public int Currency => (int)((Run?.CurrencyMinor ?? 0) / 100);
        public long CurrencyMinorUnits => Run?.CurrencyMinor ?? 0;
        public int Level => Run?.Level ?? 1;
        public int Experience => (int)(Run?.Experience ?? 0);
        public int NextLevelExperience => CombatRules.ExperienceThreshold(Level);
        public int Kills { get; private set; }
        public float DamageDealt { get; private set; }
        public int Seed { get; private set; }
        public int AliveEnemies { get; private set; }
        public int MagazineSize => CurrentWeaponDefinition.Id==ContentIds.Rpg18 ? 1 : ProgressionRules.MagazineSize(CurrentWeaponDefinition.MagazineSize, Stats);
        /// <summary>Length of the reload in progress on the weapon in hand, else of an empty-magazine reload.</summary>
        public float ReloadDuration => weaponReload[ActiveWeaponSlot] > 0 ? weaponReloadTotal[ActiveWeaponSlot] :
            CurrentWeaponDefinition.ReloadSeconds * Stats.ReloadFactor;
        public int Ammo => weaponAmmo[ActiveWeaponSlot];
        public float ReloadRemaining => weaponReload[ActiveWeaponSlot];
        /// <summary>The slot in hand: the only weapon that fires and reloads. Read it every frame;
        /// restart and wave start set it without raising <see cref="WeaponSwitched"/>.</summary>
        public int ActiveWeaponSlot { get; private set; }
        public WeaponDefinition ActiveWeapon => WeaponAt(ActiveWeaponSlot);
        /// <summary>Seconds of the current (or last) draw.</summary>
        public float SwitchDuration { get; private set; } = CombatRules.WeaponDrawSeconds;
        /// <summary>0 when a draw starts, 1 once the weapon is ready; 1 when not switching.</summary>
        public float SwitchProgress => switchRemaining <= 0 ? 1f : Mathf.Clamp01(1f - switchRemaining / SwitchDuration);
        public bool IsSwitching => switchRemaining > 0;
        /// <summary>From slot, to slot: raised when a draw starts.</summary>
        public event Action<int, int> WeaponSwitched;
        public ProgressionRun Run { get; private set; }
        public ShopSession Shop { get; private set; }
        public ProgressionStats Stats => Run == null ? ProgressionRules.Aggregate(Array.Empty<StatModifier>()) : Run.Stats;
        public int WeaponCount => Run?.Weapons.Count ?? 0;
        /// <summary>The knife is in hand: key 5, the entry after the guns in the scroll and Q cycle. The gun slot it
        /// was drawn from stays <see cref="ActiveWeaponSlot"/>.</summary>
        public bool KnifeOut { get; private set; }
        /// <summary>Seconds into the slash in progress, or -1.</summary>
        public float KnifeSwingTime { get; private set; } = -1f;
        public bool KnifeSwinging => KnifeSwingTime >= 0;
        /// <summary>The slash in progress is a quick slash (F) from a gun, which comes back when it ends.</summary>
        public bool QuickKnife { get; private set; }
        /// <summary>The hand's entry in the weapon list: a gun slot, or <see cref="WeaponCount"/> for the knife.</summary>
        public int SelectedEntry => KnifeOut || QuickKnife && KnifeSwinging ? WeaponCount : ActiveWeaponSlot;
        public event Action KnifeSwung;
        public event Action<Transform> KnifeStruck;
        public int UpgradeChoiceCount => Run?.CurrentChoices.Count ?? 0;
        public bool SupplyVisible { get; private set; }
        public float SupplyProgress { get; private set; }
        public int ActiveChargerCount
        {
            get { if (enemies == null) return 0; var count = 0; foreach (var enemy in enemies) if (enemy != null && enemy.active && enemy.charger) count++; return count; }
        }
        public int PendingChargerCount
        {
            get { if (enemies == null) return 0; var count = 0; foreach (var enemy in enemies) if (enemy != null && enemy.pending && enemy.charger) count++; return count; }
        }
        public bool ChargerTelegraphVisible
        {
            get { if (enemies == null) return false; foreach (var enemy in enemies) if (enemy != null && enemy.active && enemy.charger && enemy.aiming) return true; return false; }
        }
        public bool ChargerDashActive
        {
            get { if (enemies == null) return false; foreach (var enemy in enemies) if (enemy != null && enemy.active && enemy.charger && enemy.charging) return true; return false; }
        }

        /// <summary>Presentation read of one active enemy's attack state; false for inactive or unknown actors.</summary>
        // A ranged enemy's magazine change lasts its gun's reload take (AK 2.4333 s, MP5-SD 2.9333 s).
        private static float EnemyReloadSeconds(Enemy enemy) => enemy.sniper ? 2f : enemy.elite ? 2.4333f : 2.9333f;
        // Each aim fires a burst of EnemyBurst rounds at the gun's cyclic rate (AK ~600, MP5-SD ~750 rounds a minute);
        // a magazine holds the same 10 (Elite) and 8 (Shooter) aims as before bursts.
        private const int EnemyBurst = 3;
        private static float EnemyBurstSpacing(Enemy enemy) => enemy.elite ? .1f : .08f;
        private static int EnemyMagazine(Enemy enemy) => enemy.sniper ? 5 : enemy.elite ? 30 : 24;

        private void FireEnemyRound(Enemy enemy)
        {
            FireEnemyBullet(enemy.body.transform, enemy.aimDirection, enemy.definition.AttackDamage, enemy.sniper ? 0f : enemy.elite ? 3f : 4f);
            enemy.ammo = Mathf.Max(0, enemy.ammo - 1);
            enemy.burstLeft--;
            enemy.burstTimer = EnemyBurstSpacing(enemy);
            if (enemy.burstLeft > 0) return;
            enemy.attackTimer = enemy.definition.AttackCooldownSeconds;
            if (enemy.ammo == 0)
            {
                enemy.reloadTimer = EnemyReloadSeconds(enemy);
                EnemyReloadStarted?.Invoke(enemy.body.transform, enemy.reloadTimer);
            }
        }

        public bool TryGetEnemyAction(Transform actor, out EnemyActionState state)
        {
            state = default;
            foreach (var enemy in enemies)
            {
                if (enemy == null || !enemy.active || enemy.body.transform != actor) continue;
                state.Role = enemy.definition.Role;
                state.AttackCooldownRemaining = enemy.attackTimer;
                if (enemy.charger)
                {
                    state.Kind = enemy.charging ? EnemyActionKind.ChargeDash :
                        enemy.aiming ? EnemyActionKind.ChargeTelegraph : EnemyActionKind.None;
                    if (enemy.aiming)
                        state.TelegraphProgress = Mathf.Clamp01(1f - enemy.aimTimer / enemy.definition.TelegraphSeconds);
                    if (enemy.aiming || enemy.charging)
                        state.ChargeDirection = new Vector3(enemy.aimDirection.x, 0, enemy.aimDirection.y);
                }
                else if (enemy.shooter)
                {
                    state.Aiming = enemy.aiming;
                    state.AimDirection = new Vector3(enemy.aimDirection.x, 0, enemy.aimDirection.y);
                    state.ShotOrigin = SniperOrigin(enemy);
                    state.ReloadSeconds = EnemyReloadSeconds(enemy);
                    state.ReloadRemaining = enemy.reloadTimer;
                }
                else
                {
                    state.Kind = EnemyActionKind.Melee;
                    Vector3 toPlayer = player.position - actor.position;
                    toPlayer.y = 0;
                    state.InMeleeReach = toPlayer.magnitude <= MeleeReach(enemy);
                }
                return true;
            }
            return false;
        }

        private const float HalfWidth = 16.55f, HalfHeight = 9.55f;
        private const float PlayerRadius = 0.38f, EnemyRadius = 0.37f;
        private const int GridWidth = 35, GridHeight = 21, GridSize = GridWidth * GridHeight;
        private const int EnemyPoolSize = CombatRules.EnemyCap, BulletPoolSize = 48, PickupPoolSize = 48;
        private const float SpawnWarning = 0.6f;
        private const float ChargerDashSpeed = 10f, ChargerDashSeconds = 0.45f;
        // Hand aiming replaces nearest-target selection, so give it a little more reach
        // and a forgiving hit radius around the crosshair ray.
        private const float ManualRangeFactor = 1.5f, ManualAimRadius = 0.62f;
        // How tall an enemy is for the crosshair ray, from his feet; generous over the visible models.
        private const float BodyHeight = 2.0f, EliteBodyHeight = 2.6f;
        // The imported commander is 1.85 m tall and displayed at 1.3 times its original size.
        private const float CommanderBodyHeight = 1.85f * 1.3f;
        private const float CommanderHeadRadius = ManualAimRadius * 0.65f;
        private const float CoverHeight = 1.1f;

        private float EnemyBodyHeight(Enemy enemy) => ReferenceEquals(enemy, commander)
            ? CommanderBodyHeight : enemy.elite ? EliteBodyHeight : BodyHeight;

        private float EnemyAimRadius(Enemy enemy, float height = 0f) =>
            ReferenceEquals(enemy, commander)
                ? (CombatRules.IsHeadshot(height, CommanderBodyHeight) ? CommanderHeadRadius : ManualAimRadius * 1.3f)
                : ManualAimRadius * (enemy.elite ? 1.3f : 1f);
        private static readonly Vector2[] Directions = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };

        private struct Cover
        {
            public Vector2 center, half;
            public Cover(float x, float z, float width, float depth)
            { center = new Vector2(x, z); half = new Vector2(width, depth) * 0.5f; }
        }

        private sealed class Enemy
        {
            public GameObject body, warning, chargerMarker, laser;
            public bool sniper => definition != null && definition.Role == EnemyRole.Sniper;
            public bool active, pending, shooter, elite, charger, aiming, charging, chargeHit;
            public EnemyDefinition definition;
            public float hp, attackTimer, aimTimer, warningTimer, pathTimer, chargeRemaining, reloadTimer, burstTimer;
            public int ammo, burstLeft;
            public Vector2 aimDirection;
            public bool swingAnnounced;
        }

        private sealed class Bullet
        {
            public GameObject body;
            public bool active;
            public bool nearMissPlayed, highShot;
            public Vector3 velocity;
            public float remaining, damage;
        }

        private sealed class Pickup
        {
            public GameObject body;
            public bool active;
            public long id, valueMinor;
        }

        private readonly Cover[] covers =
        {
            new Cover(-8f, -3.4f, 2.4f, 1.35f), new Cover(-3.2f, 3.7f, 2.7f, 1.2f),
            new Cover(4.2f, -3.6f, 3f, 1.25f), new Cover(9f, 3.1f, 2.3f, 1.3f),
            new Cover(-10.7f, 4.2f, 1.4f, 1.5f), new Cover(10.6f, -3.7f, 1.4f, 1.5f)
        };
        private readonly Enemy[] enemies = new Enemy[EnemyPoolSize];
        private readonly Bullet[] bullets = new Bullet[BulletPoolSize];
        private readonly Pickup[] pickups = new Pickup[PickupPoolSize];
        private readonly bool[] blocked = new bool[GridSize];
        private readonly int[] distances = new int[GridSize], queue = new int[GridSize];
        private Transform player;
        private Renderer playerRenderer;
        private GameObject tracer;
        private Vector3 tracerStart, tracerEnd;
        private float tracerRemaining, spawnTimer, invulnerability;
        private int squadLeft;
        private int targetIndex = -1;
        private int elitesThisWave;
        private readonly int[] weaponAmmo = new int[ProgressionRun.MaxWeaponSlots];
        private readonly float[] weaponReload = new float[ProgressionRun.MaxWeaponSlots];
        private readonly float[] weaponReloadTotal = new float[ProgressionRun.MaxWeaponSlots];
        private readonly float[] weaponFireTimer = new float[ProgressionRun.MaxWeaponSlots];
        private float switchRemaining;
        private int lastWeaponSlot = -1;
        // The weapon in hand by identity, so a wave after the shop keeps it when slots shift.
        private OwnedWeapon heldWeapon;
        private long nextPickupId;
        private bool manualAim, triggerHeld, crouchHeld;
        private bool sniperTutorialSeen;
        private EnemyDefinition forcedEnemyDefinition;
        private Material sniperLaserMaterial;
        public bool SniperTutorialVisible { get; private set; }
        public bool DebugInvulnerable { get; private set; }
        public bool DebugCheatsUsed { get; private set; }
        private const float SniperShotHeight = 1.45f;
        private const int SniperCap = 2;
        private Vector3 aimDirection = Vector3.forward;
        private bool hasViewRay;
        private Vector3 viewRayOrigin, viewRayDirection;
        private float viewYaw;
        private bool bandageUsed;
        private System.Random rng;
        private System.Random shopRng;
        private SupplyEvent supplyEvent;
        private GameObject supplyCrate;

        private WeaponDefinition CurrentWeaponDefinition => Run != null && Run.Weapons.Count > 0 ? WeaponAt(ActiveWeaponSlot) :
            ContentCatalog.P2.GetWeapon(SelectedWeapon == WeaponKind.Rifle ? ContentIds.Rifle :
                SelectedWeapon == WeaponKind.Smg ? ContentIds.Smg : ContentIds.Shotgun);

        private void Awake()
        {
            BuildArena();
            BuildPools();
            BuildNavigation();
            HideTransientObjects();
        }

        private void Update()
        {
            var debugKeyboard = Keyboard.current;
            if (debugKeyboard != null)
            {
                if (debugKeyboard.f6Key.wasPressedThisFrame) ExecuteCheat("god");
                if (debugKeyboard.f7Key.wasPressedThisFrame) ExecuteCheat("nextwave");
                if (debugKeyboard.f8Key.wasPressedThisFrame) ExecuteCheat("sniper");
                if (debugKeyboard.f9Key.wasPressedThisFrame) ExecuteCheat("boss");
                if (debugKeyboard.f10Key.wasPressedThisFrame) ExecuteCheat("money");
            }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                TogglePause();
            if (State != CombatState.Playing) return;
            if(CommanderCinematic)
            {
                skipCommanderHold=debugKeyboard!=null && debugKeyboard.spaceKey.isPressed ? skipCommanderHold+Time.deltaTime : 0;
                if(skipCommanderHold>=.6f) commanderClock=10;
                StepSimulation(Time.deltaTime,Vector2.zero);return;
            }
            Vector2 movement = Vector2.zero;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.spaceKey.wasPressedThisFrame &&
                    player.position.y <= GroundY + 0.001f &&
                    verticalVelocity <= 0f)
                {
                    verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                }

                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) movement.x -= 1;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) movement.x += 1;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) movement.y -= 1;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) movement.y += 1;
                if (keyboard.rKey.wasPressedThisFrame) RequestReload();
                if (keyboard.digit1Key.wasPressedThisFrame) SwitchWeapon(0);
                if (keyboard.digit2Key.wasPressedThisFrame) SwitchWeapon(1);
                if (keyboard.digit3Key.wasPressedThisFrame) SwitchWeapon(2);
                if (keyboard.digit4Key.wasPressedThisFrame) SwitchWeapon(3);
                if (keyboard.digit5Key.wasPressedThisFrame) DrawKnife();
                if (keyboard.fKey.wasPressedThisFrame) Slash();
                if (keyboard.qKey.wasPressedThisFrame) LastWeapon();
            }
            crouchHeld = (keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed)) ||
                (Gamepad.current != null && Gamepad.current.rightStickButton.isPressed);
            walkHeld = keyboard != null && keyboard.shiftKey.isPressed;
            if (Mouse.current != null)
            {
                float wheel = Mouse.current.scroll.ReadValue().y;
                if (wheel > 0) PreviousWeapon();
                else if (wheel < 0) NextWeapon();
            }
            if (Gamepad.current != null)
            {
                movement += Gamepad.current.leftStick.ReadValue();
                if (Gamepad.current.buttonNorth.wasPressedThisFrame) NextWeapon();
            }
            // Third-person controls: forward is wherever the camera is looking.
            if (manualAim) movement = RotatePlanar(movement, viewYaw);
            StepSimulation(Time.deltaTime, movement);
        }

        /// <summary>
        /// Third-person view input. The yaw orients WASD; the aim point is where the
        /// crosshair meets the combat plane. Supplying it switches to manual fire.
        /// </summary>
        public void SetViewInput(float yawDegrees, Vector3 aimPoint, bool trigger) =>
            SetViewInput(yawDegrees, aimPoint, trigger, Vector3.zero, Vector3.zero);

        /// <summary>As above, with the crosshair ray (camera position and look direction) that decides what a shot
        /// hits, height included. A zero direction falls back to a level ray from the hero.</summary>
        public void SetViewInput(float yawDegrees, Vector3 aimPoint, bool trigger, Vector3 rayOrigin, Vector3 rayDirection)
        {
            hasViewRay = rayDirection.sqrMagnitude > 0;
            viewRayOrigin = rayOrigin;
            viewRayDirection = hasViewRay ? rayDirection.normalized : Vector3.zero;
            manualAim = true;
            viewYaw = yawDegrees;
            triggerHeld = trigger;
            // The crosshair can meet the ground behind the hero when the camera is close to cover.
            // In that case the view still looks forward; never turn the gun toward that ground point.
            Vector3 direction = hasViewRay ? viewRayDirection :
                player != null ? aimPoint - player.position : Vector3.zero;
            direction.y = 0;
            // A crosshair point almost under the soldier has no stable direction.
            if (direction.sqrMagnitude < 1f) direction = Quaternion.Euler(0, yawDegrees, 0) * Vector3.forward;
            aimDirection = direction.normalized;
        }

        /// <summary>Starts reloading the weapon in hand unless it is full or still being drawn.</summary>
        public void RequestReload()
        {
            int slot = ActiveWeaponSlot;
            if (State != CombatState.Playing || switchRemaining > 0 || slot >= WeaponCount || KnifeOut || KnifeSwinging) return;
            if (weaponReload[slot] <= 0 && weaponAmmo[slot] < MagazineSizeAt(slot))
                StartReload(slot);
        }

        /// <summary>Its length is fixed now, from the rounds missing now; the magazine fills when it ends.</summary>
        private void StartReload(int slot)
        {
            float seconds = WeaponAt(slot).ReloadSecondsFor(MagazineSizeAt(slot) - weaponAmmo[slot]) * Stats.ReloadFactor * (WeaponAt(slot).Id==ContentIds.Rpg18 ? Run.RocketReloadFactor : 1f);
            weaponReload[slot] = weaponReloadTotal[slot] = seconds;
            ReloadStarted?.Invoke(slot, seconds, MagazineSizeAt(slot) - weaponAmmo[slot]);
        }

        /// <summary>
        /// Draws another owned weapon. The holstered one keeps its rounds but loses a reload in
        /// progress; switching again mid-draw restarts the draw for the new slot.
        /// </summary>
        public bool SwitchWeapon(int slot)
        {
            if (State != CombatState.Playing || slot < 0 || slot >= WeaponCount || slot == ActiveWeaponSlot && !KnifeOut ||
                KnifeSwinging) return false;
            int from = SelectedEntry;
            weaponReload[ActiveWeaponSlot] = 0;
            lastWeaponSlot = from;
            KnifeOut = false;
            ActiveWeaponSlot = slot;
            heldWeapon = Run.Weapons[slot];
            SwitchDuration = CombatRules.WeaponDrawSeconds;
            switchRemaining = SwitchDuration;
            WeaponSwitched?.Invoke(from, slot);
            return true;
        }

        /// <summary>Puts the gun away and draws the knife (one draw, like a gun).</summary>
        public bool DrawKnife()
        {
            if (State != CombatState.Playing || WeaponCount == 0 || KnifeOut || KnifeSwinging) return false;
            int from = ActiveWeaponSlot;
            weaponReload[from] = 0;
            lastWeaponSlot = from;
            KnifeOut = true;
            SwitchDuration = CombatRules.WeaponDrawSeconds;
            switchRemaining = SwitchDuration;
            WeaponSwitched?.Invoke(from, WeaponCount);
            return true;
        }

        /// <summary>A knife slash: from the knife in hand, or from any gun (F), which cancels its reload and comes back
        /// ready once the slash ends.</summary>
        public bool Slash()
        {
            if (State != CombatState.Playing || WeaponCount == 0 || KnifeSwinging || KnifeOut && switchRemaining > 0) return false;
            if (!KnifeOut)
            {
                weaponReload[ActiveWeaponSlot] = 0;
                switchRemaining = 0;
            }
            QuickKnife = !KnifeOut;
            KnifeSwingTime = 0;
            KnifeSwung?.Invoke();
            return true;
        }

        private bool SelectEntry(int entry) => entry == WeaponCount ? DrawKnife() : SwitchWeapon(entry);
        public bool NextWeapon() => WeaponCount > 0 && SelectEntry((SelectedEntry + 1) % (WeaponCount + 1));
        public bool PreviousWeapon() => WeaponCount > 0 && SelectEntry((SelectedEntry + WeaponCount) % (WeaponCount + 1));
        public bool LastWeapon() => SelectEntry(lastWeaponSlot);

        /// <summary>Advances the slash: at contact it strikes every enemy in the frontal arc within reach.</summary>
        private void UpdateKnife(float dt)
        {
            if (!KnifeSwinging) return;
            float before = KnifeSwingTime;
            KnifeSwingTime += dt;
            if (before < CombatRules.KnifeContactSeconds && KnifeSwingTime >= CombatRules.KnifeContactSeconds)
            {
                float cos = Mathf.Cos(CombatRules.KnifeArcDegrees * Mathf.Deg2Rad);
                foreach (var enemy in enemies)
                {
                    if (enemy == null || !enemy.active) continue;
                    Vector3 to = enemy.body.transform.position - player.position;
                    to.y = 0;
                    if (to.sqrMagnitude > CombatRules.KnifeReach * CombatRules.KnifeReach ||
                        to.sqrMagnitude > .01f && Vector3.Dot(to.normalized, aimDirection) < cos) continue;
                    var body = enemy.body.transform;
                    ApplyShotDamage(enemy, CombatRules.KnifeDamage * Stats.DamageFactor * (enemy.elite ? CombatRules.KnifeEliteFactor : 1f), false);
                    KnifeStruck?.Invoke(body);
                }
            }
            if (KnifeSwingTime < CombatRules.KnifeSwingSeconds) return;
            KnifeSwingTime = -1f;
            QuickKnife = false;
        }

        private void PutKnifeAway()
        {
            KnifeOut = QuickKnife = false;
            KnifeSwingTime = -1f;
        }

        private static Vector2 RotatePlanar(Vector2 input, float yawDegrees)
        {
            float radians = yawDegrees * Mathf.Deg2Rad, sin = Mathf.Sin(radians), cos = Mathf.Cos(radians);
            // Unity yaw turns clockwise from +Z when seen from above.
            return new Vector2(input.x * cos + input.y * sin, -input.x * sin + input.y * cos);
        }

        public void SelectWeapon(WeaponKind kind)
        {
            if (State != CombatState.Menu || (kind != WeaponKind.Rifle && kind != WeaponKind.Smg && kind != WeaponKind.Shotgun)) return;
            SelectedWeapon = kind;
            weaponAmmo[0] = MagazineSize;
        }

        public void BeginRun()
        {
            Seed = UnityEngine.Random.Range(1, int.MaxValue);
            rng = new System.Random(Seed);
            shopRng = new System.Random(unchecked(Seed ^ (int)0x59A32E11));
            Wave = 1;
            Run = new ProgressionRun(SelectedWeapon == WeaponKind.Rifle ? ContentIds.Rifle :
                SelectedWeapon == WeaponKind.Smg ? ContentIds.Smg : ContentIds.Shotgun);
            Shop = null;
            heldWeapon = null;
            bandageUsed = false;
            nextPickupId = 0;
            Health = MaxHealth;
            RunElapsed = 0;
            Kills = 0;
            DamageDealt = 0;
            supplyEvent = new SupplyEvent();
            SupplyVisible = false;
            SupplyProgress = 0;
            player.position = new Vector3(0, 0.8f, 0);
            verticalVelocity = 0f;
            player.gameObject.SetActive(true);
            crouchHeld = false;
            sniperTutorialSeen = SniperTutorialVisible = false;
            DebugInvulnerable = DebugCheatsUsed = false;
            Crouch = 0;
            walkHeld = false;
            Walk = 0;
            ResetWave();
            State = CombatState.Playing;
        }

        public void Restart() => BeginRun();

        public void ReturnToMenu()
        {
            HideTransientObjects();
            supplyEvent = null;
            Shop = null;
            Run = null;
            heldWeapon = null;
            ActiveWeaponSlot = 0;
            PutKnifeAway();
            lastWeaponSlot = -1;
            switchRemaining = 0;
            State = CombatState.Menu;
            player.gameObject.SetActive(true);
            player.position = new Vector3(0, 0.8f, 0);
            verticalVelocity = 0f;
        }

        public void TogglePause()
        {
            if (State == CombatState.Playing) State = CombatState.Paused;
            else if (State == CombatState.Paused)
            {
                SniperTutorialVisible = false;
                State = CombatState.Playing;
            }
        }

        public void ContinueWave()
        {
            if (State != CombatState.Shop) return;
            Wave++;
            ResetWave();
            State = CombatState.Playing;
        }

        public bool ChooseUpgrade(int index)
        {
            if (State != CombatState.Upgrade || index < 0 || index >= UpgradeChoiceCount) return false;
            if (!Run.TryChooseUpgrade(Run.CurrentChoices[index])) return false;
            if (Run.OpenNextUpgrade(shopRng)) return true;
            OpenShop();
            return true;
        }

        public PurchaseResult PurchaseOffer(int slot)
        {
            if (State != CombatState.Shop || Shop == null) return PurchaseResult.InvalidSlot;
            int owned = WeaponCount;
            var result = Shop.TryPurchase(slot);
            // A bought weapon arrives loaded in the new last slot.
            if (WeaponCount > owned)
            {
                weaponAmmo[owned] = MagazineSizeAt(owned);
                weaponReload[owned] = weaponFireTimer[owned] = 0;
            }
            return result;
        }

        public bool SetOfferLocked(int slot, bool locked) =>
            State == CombatState.Shop && Shop != null && Shop.SetLocked(slot, locked);

        public bool RerollShop() => State == CombatState.Shop && Shop != null &&
            Shop.TryReroll(new ShopOfferGenerator().RollOffers(Run, Wave, shopRng, Shop));

        public bool SellWeapon(int slot, out int refund)
        {
            refund = 0;
            if (State != CombatState.Shop || Run == null || !Run.TrySellWeapon(slot, out refund)) return false;
            RemoveSlotState(slot);
            KeepHeldWeaponInHand();
            return true;
        }

        public bool CombineWeapons(int first, int second)
        {
            if (State != CombatState.Shop || Run == null || !Run.TryCombineWeapons(first, second)) return false;
            // The combined weapon takes the lower slot; the higher one is removed.
            RemoveSlotState(Mathf.Max(first, second));
            KeepHeldWeaponInHand();
            return true;
        }

        public bool CombineFirstMatchingWeapons()
        {
            if (State != CombatState.Shop) return false;
            for (int first = 0; first < WeaponCount; first++)
                for (int second = first + 1; second < WeaponCount; second++)
                    if (CombineWeapons(first, second)) return true;
            return false;
        }

        public bool BandageUsed => bandageUsed;
        public bool BuyBandage()
        {
            if (State != CombatState.Shop || bandageUsed || Health >= MaxHealth || !Run.TrySpend(15)) return false;
            bandageUsed = true;
            Health = Mathf.Min(MaxHealth, Health + 25f);
            return true;
        }

        public void ApplyPlayerDamage(float rawDamage) =>
            DamagePlayer(rawDamage, player != null ? player.position : Vector3.zero);

        private void DamagePlayer(float rawDamage, Vector3 source)
        {
            if (CommanderCinematic || State != CombatState.Playing || DebugInvulnerable || rawDamage <= 0 || invulnerability > 0) return;
            float reduced = rawDamage * 100f / (100f + Stats.Armor);
            Health = Mathf.Max(0, Health - Mathf.Max(1f, reduced));
            invulnerability = 0.35f;
            PlayerDamaged?.Invoke(source, Mathf.Max(1f, reduced));
            if (Health <= 0)
            {
                float elapsed = WaveDuration(Wave) - WaveRemaining;
                supplyEvent?.Advance(Wave, Mathf.Max(0, elapsed), 0, false, false, false);
                State = CombatState.Defeat;
                HideTransientObjects();
            }
        }

        /// <summary>One bounded simulation step, also used by PlayMode tests.</summary>
        public void StepSimulation(float deltaTime, Vector2 movement)
        {
            if (State != CombatState.Playing || deltaTime <= 0) return;
            // Bound long frame stalls without dropping elapsed time during deterministic tests.
            if(CommanderCinematic) {AdvanceCommanderCinematic(deltaTime);return;}
            while (deltaTime > 0 && State == CombatState.Playing)
            {
                float step = Mathf.Min(deltaTime, 1f / 30f);
                Simulate(step, movement);
                if(CommanderCinematic) break;
                deltaTime -= step;
            }
        }

        private void Simulate(float dt, Vector2 movement)
        {
            invulnerability = Mathf.Max(0, invulnerability - dt);
            if (Health > 0 && Health < MaxHealth && Stats.Regen > 0)
                Health = Mathf.Min(MaxHealth, Health + Stats.Regen * dt);
            tracerRemaining = Mathf.Max(0, tracerRemaining - dt);
            tracer.SetActive(tracerRemaining > 0);
            for (int slot = 0; slot < WeaponCount; slot++)
            {
                weaponFireTimer[slot] = Mathf.Max(0, weaponFireTimer[slot] - dt);
                if (weaponReload[slot] <= 0) continue;
                weaponReload[slot] = Mathf.Max(0, weaponReload[slot] - dt);
                if (weaponReload[slot] == 0) weaponAmmo[slot] = MagazineSizeAt(slot);
            }
            if (switchRemaining > 0)
            {
                switchRemaining = Mathf.Max(0, switchRemaining - dt);
                // An empty weapon starts its reload as soon as it is drawn.
                if (switchRemaining == 0 && !KnifeOut && weaponAmmo[ActiveWeaponSlot] <= 0 && weaponReload[ActiveWeaponSlot] <= 0)
                    StartReload(ActiveWeaponSlot);
            }
            MovePlayer(dt, movement);
            UpdateNavigation();
            UpdateSpawn(dt);
            UpdateCommander(dt);
            UpdateRockets(dt);
            if(State!=CombatState.Playing || CommanderCinematic) return;
            UpdateEnemies(dt);
            if (State != CombatState.Playing) return;
            UpdateBullets(dt);
            if (State != CombatState.Playing) return;
            UpdatePickups();
            UpdateKnife(dt);
            if (State != CombatState.Playing) return;
            FireWeapon();
            RunElapsed += dt;
            WaveRemaining = Mathf.Max(0, WaveRemaining - dt);
            UpdateSupply(dt, WaveRemaining <= 0);
            if (WaveRemaining <= 0 && !CommanderActive) FinishWave();
        }

        private void ResetWave()
        {
            HideTransientObjects();
            AliveEnemies = 0;
            WaveRemaining = WaveDuration(Wave);
            spawnTimer = CombatRules.FirstSquadSeconds;
            squadLeft = 0;
            elitesThisWave = 0;
            KeepHeldWeaponInHand();
            PutKnifeAway();
            lastWeaponSlot = -1;
            switchRemaining = 0;
            for (int slot = 0; slot < ProgressionRun.MaxWeaponSlots; slot++)
            {
                weaponFireTimer[slot] = 0;
                weaponReload[slot] = 0;
                weaponAmmo[slot] = slot < WeaponCount ? MagazineSizeAt(slot) : 0;
            }
            invulnerability = 0;
            targetIndex = -1;
            AimOnTarget = false;
            UpdateNavigation();
            if(Wave==5) StartCommander();
        }

        /// <summary>Drops a removed slot's ammo and timers so later slots keep their own state as they move down.</summary>
        private void RemoveSlotState(int removed)
        {
            for (int slot = removed; slot < ProgressionRun.MaxWeaponSlots - 1; slot++)
            {
                weaponAmmo[slot] = weaponAmmo[slot + 1];
                weaponReload[slot] = weaponReload[slot + 1];
                weaponReloadTotal[slot] = weaponReloadTotal[slot + 1];
                weaponFireTimer[slot] = weaponFireTimer[slot + 1];
            }
            int last = ProgressionRun.MaxWeaponSlots - 1;
            weaponAmmo[last] = 0;
            weaponReload[last] = weaponFireTimer[last] = 0;
        }

        /// <summary>The weapon in hand stays drawn if still owned (a combined one by kind), else the first slot.</summary>
        private void KeepHeldWeaponInHand()
        {
            int held = -1;
            for (int slot = 0; slot < WeaponCount && held < 0; slot++)
                if (ReferenceEquals(Run.Weapons[slot], heldWeapon)) held = slot;
            for (int slot = 0; slot < WeaponCount && held < 0 && heldWeapon != null; slot++)
                if (Run.Weapons[slot].Id == heldWeapon.Id) held = slot;
            ActiveWeaponSlot = Mathf.Max(0, held);
            heldWeapon = WeaponCount > 0 ? Run.Weapons[ActiveWeaponSlot] : null;
        }

        private void FinishWave()
        {
            // Death has already been handled earlier in the same step.
            if (State != CombatState.Playing || CommanderActive) return;
            long remaining = 0;
            foreach (var pickup in pickups) if (pickup.active) remaining += pickup.valueMinor;
            Run.TryResolveWave(Wave, remaining, out _);
            HideTransientObjects();
            AliveEnemies = 0;
            if (Wave == CombatRules.MaxWaves) { State = CombatState.Victory; return; }
            if (Run.OpenNextUpgrade(shopRng)) State = CombatState.Upgrade;
            else OpenShop();
        }

        private void OpenShop()
        {
            var offers = new ShopOfferGenerator().RollOffers(Run, Wave, shopRng, Shop);
            Shop = Shop == null ? new ShopSession(Run, Wave, offers) : Shop.CreateNextShop(Wave, offers);
            bandageUsed = false;
            State = CombatState.Shop;
        }

        /// <summary>Hold-to-crouch, as the keyboard and gamepad set it each frame; also used by PlayMode tests.</summary>
        public void SetCrouchInput(bool held) => crouchHeld = held;

        /// <summary>Hold-to-walk, as the keyboard sets it each frame; also used by PlayMode tests.</summary>
        public void SetWalkInput(bool held) => walkHeld = held;

        private void MovePlayer(float dt, Vector2 movement)
        {
            if (movement.sqrMagnitude > 1) movement.Normalize();
            Crouch = Mathf.MoveTowards(Crouch, crouchHeld ? 1f : 0f, dt / CombatRules.CrouchSeconds);
            Walk = Mathf.MoveTowards(Walk, walkHeld ? 1f : 0f, dt / CombatRules.WalkSeconds);
            float speed = CombatRules.PlayerSpeed * Stats.SpeedFactor * Mathf.Lerp(1f, CombatRules.CrouchSpeedFactor, Crouch) *
                Mathf.Lerp(1f, CombatRules.WalkSpeedFactor, Walk * (1f - Crouch));
            var displacement = new Vector3(movement.x, 0, movement.y) * (speed * dt);
            MoveWithCover(player, displacement, PlayerRadius);
            var p = player.position;
            //player.position = new Vector3(Mathf.Clamp(p.x, -HalfWidth, HalfWidth), 0.8f, Mathf.Clamp(p.z, -HalfHeight, HalfHeight));
            verticalVelocity += Gravity * dt;
            float nextY = p.y + verticalVelocity * dt;

            if (nextY <= GroundY)
            {
                nextY = GroundY;
                verticalVelocity = 0f;
            }

            player.position = new Vector3(
                Mathf.Clamp(p.x, -HalfWidth, HalfWidth),
                nextY,
                Mathf.Clamp(p.z, -HalfHeight, HalfHeight));
            playerRenderer.material.color = invulnerability > 0 && Mathf.FloorToInt(invulnerability * 20) % 2 == 0
                ? Color.white : (PlayerMaterial != null ? PlayerMaterial.color : new Color(.55f, .65f, .4f));
        }

        private void UpdateSpawn(float dt)
        {
            spawnTimer -= dt;
            if (spawnTimer > 0 || AliveEnemies >= CombatRules.EnemyCap) return;
            if (squadLeft <= 0) squadLeft = CombatRules.SquadSize(Wave);
            squadLeft--;
            // The next member follows closely; after the last one the lull runs to the next squad's start.
            spawnTimer = squadLeft > 0 ? CombatRules.SquadSpacingSeconds :
                CombatRules.SquadIntervalSeconds(Wave) - (CombatRules.SquadSize(Wave) - 1) * CombatRules.SquadSpacingSeconds;
            Enemy slot = null;
            foreach (var enemy in enemies) if (!enemy.active && !enemy.pending) { slot = enemy; break; }
            if (slot == null) return;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int edge = rng.Next(4);
                float x = edge < 2 ? (edge == 0 ? -15.6f : 15.6f) : (float)(rng.NextDouble() * 30 - 15);
                float z = edge >= 2 ? (edge == 2 ? -8.6f : 8.6f) : (float)(rng.NextDouble() * 17 - 8.5f);
                var point = new Vector3(x, .8f, z);
                if (!CombatRules.IsSpawnSafe(point, player.position) || IsInsideCover(new Vector2(x, z), EnemyRadius)) continue;
                slot.body.transform.position = point;
                slot.warning.transform.position = new Vector3(x, .035f, z);
                slot.warning.transform.localScale = new Vector3(1.15f, .015f, 1.15f);
                slot.warning.transform.rotation = Quaternion.identity;
                slot.warning.SetActive(true);
                slot.pending = true;
                slot.warningTimer = SpawnWarning;
                slot.definition = ChooseEnemyDefinition();
                slot.elite = slot.definition.Role == EnemyRole.Elite;
                // Both ranged roles use the same real projectile path; Elite only differs in
                // magazine, spread, range and damage.
                slot.shooter = slot.definition.Role == EnemyRole.Shooter || slot.definition.Role == EnemyRole.Elite || slot.sniper;
                slot.charger = slot.definition.Role == EnemyRole.Charger;
                return;
            }
        }

        private EnemyDefinition ChooseEnemyDefinition()
        {
            var catalog = ContentCatalog.P2;
            if (forcedEnemyDefinition != null)
            {
                var forced = forcedEnemyDefinition;
                forcedEnemyDefinition = null;
                return forced;
            }
            if (IsEliteDue())
            {
                elitesThisWave++;
                return catalog.GetEnemy(ContentIds.Elite);
            }
            int total = 0;
            foreach (var definition in catalog.Enemies)
                if (IsEnemyRoleEligibleForWave(definition.Role, Wave) && (definition.Role != EnemyRole.Sniper || SnipersInPlay() < SniperCap)) total += definition.SpawnWeight;
            int choice = rng.Next(total);
            foreach (var definition in catalog.Enemies)
            {
                if (!IsEnemyRoleEligibleForWave(definition.Role, Wave) || (definition.Role == EnemyRole.Sniper && SnipersInPlay() >= SniperCap)) continue;
                choice -= definition.SpawnWeight;
                if (choice < 0) return definition;
            }
            throw new InvalidOperationException("No eligible regular enemy for wave " + Wave);
        }

        public static bool IsEnemyRoleEligibleForWave(EnemyRole role, int wave) =>
            (role == EnemyRole.Infantry && wave >= 1) || (role == EnemyRole.Charger && wave >= 2) ||
            (role == EnemyRole.Shooter && wave >= 3) || (role == EnemyRole.Sniper && wave >= 2);

        /// <summary>The Elite is outside the weighted pool: from this wave each spawn first checks <see cref="IsEliteDue"/>.</summary>
        public static bool IsEliteEligibleForWave(int wave) => wave >= 3;

        /// <summary>Elites fielded in the wave when it has a fixed number (waves 3-4); zero where spawns roll instead.</summary>
        public static int EliteQuota(int wave) => wave == 3 ? 3 : wave == 4 ? 6 : 0;

        /// <summary>Elites alive or announced at once: one in wave 3, otherwise unlimited.</summary>
        public static int MaxElitesInPlay(int wave) => wave == 3 ? 1 : int.MaxValue;

        // A quota wave spreads its Elites from 8 s to 28 s in; later waves roll 12% (wave 5) or 14% of spawns.
        private bool IsEliteDue()
        {
            if (!IsEliteEligibleForWave(Wave) || ElitesInPlay() >= MaxElitesInPlay(Wave)) return false;
            int quota = EliteQuota(Wave);
            if (quota == 0) return rng.NextDouble() < (Wave == 5 ? .12 : .14);
            float due = 8f + elitesThisWave * 20f / Mathf.Max(1, quota - 1);
            return elitesThisWave < quota && WaveDuration(Wave) - WaveRemaining >= due;
        }

        private int SnipersInPlay()
        {
            int count = 0;
            foreach (var enemy in enemies)
                if ((enemy.active || enemy.pending) && enemy.sniper) count++;
            return count;
        }

        /// <summary>Local test controls: F6 god, F7 nextwave, F8 sniper. No rewards are granted by skipping.</summary>
        public void ExecuteCheat(string command)
        {
            if(command=="boss" && State!=CombatState.Playing && State!=CombatState.Paused) BeginRun();
            if (State != CombatState.Playing && State != CombatState.Paused) return;
            switch (command)
            {
                case "boss":
                    if(CommanderActive) return;
                    Wave=5;ResetWave();State=CombatState.Playing;break;
                case "money": Run.AddCurrency(100000);break;
                case "god": DebugInvulnerable = !DebugInvulnerable; break;
                case "nextwave":
                    if(CommanderActive) return;
                    SniperTutorialVisible = false;
                    if (Wave >= MaxWave) { State = CombatState.Victory; HideTransientObjects(); AliveEnemies = 0; }
                    else { Wave++; ResetWave(); State = CombatState.Playing; }
                    break;
                case "sniper":
                    if (Wave < 2 || SnipersInPlay() >= SniperCap || AliveEnemies >= CombatRules.EnemyCap) return;
                    forcedEnemyDefinition = ContentCatalog.P2.GetEnemy(ContentIds.Sniper);
                    // An extra enemy: the squad schedule carries on as it was.
                    float timer = spawnTimer;
                    int left = squadLeft;
                    spawnTimer = 0;
                    squadLeft = 1;
                    UpdateSpawn(0);
                    spawnTimer = timer;
                    squadLeft = left;
                    forcedEnemyDefinition = null;
                    break;
                default: return;
            }
            DebugCheatsUsed = true;
        }

        private int ElitesInPlay()
        {
            int count = 0;
            foreach (var enemy in enemies)
                if ((enemy.active || enemy.pending) && enemy.elite) count++;
            return count;
        }

        private void UpdateEnemies(float dt)
        {
            int activeShooters = 0;
            foreach (var enemy in enemies) if (enemy != null && enemy.active && enemy.shooter && enemy.aiming) activeShooters++;
            foreach (var enemy in enemies)
            {
                if (enemy == null || ReferenceEquals(enemy,commander)) continue;
                if (enemy.pending)
                {
                    enemy.warningTimer -= dt;
                    if (enemy.warningTimer <= 0 && AliveEnemies < CombatRules.EnemyCap)
                    {
                        enemy.pending = false;
                        enemy.warning.SetActive(false);
                        enemy.laser.SetActive(false);
                        enemy.active = true;
                        enemy.body.SetActive(true);
                        enemy.body.GetComponent<Renderer>().sharedMaterial = enemy.shooter ? ShooterMaterial : EnemyMaterial;
                        enemy.body.transform.localScale = enemy.elite ? new Vector3(.95f, 1.05f, .95f) :
                            enemy.charger ? new Vector3(.55f, .65f, .55f) : new Vector3(.7f, .75f, .7f);
                        enemy.chargerMarker.SetActive(enemy.charger);
                        enemy.hp = enemy.definition.MaxHealth;
                        enemy.attackTimer = 0.5f;
                        enemy.aiming = false;
                        enemy.charging = false;
                        enemy.chargeHit = false;
                        enemy.chargeRemaining = 0;
                        enemy.aimTimer = 0;
                        enemy.aimDirection = Vector2.zero;
                        enemy.ammo = EnemyMagazine(enemy);
                        enemy.burstLeft = 0;
                        enemy.reloadTimer = 0;
                        enemy.pathTimer = 0;
                        AliveEnemies++;
                        if (enemy.sniper && !sniperTutorialSeen)
                        {
                            sniperTutorialSeen = SniperTutorialVisible = true;
                            State = CombatState.Paused;
                            return;
                        }
                    }
                }
                if (!enemy.active) continue;
                enemy.attackTimer = Mathf.Max(0, enemy.attackTimer - dt);
                if (enemy.shooter && enemy.reloadTimer > 0)
                {
                    enemy.reloadTimer = Mathf.Max(0, enemy.reloadTimer - dt);
                    if (enemy.reloadTimer <= 0) enemy.ammo = EnemyMagazine(enemy);
                }
                Vector3 toPlayer = player.position - enemy.body.transform.position;
                toPlayer.y = 0;
                float distance = toPlayer.magnitude;
                if (enemy.shooter)
                {
                    // Test seams and freshly promoted pooled actors may not have gone through
                    // the spawn activation branch; treat an empty, idle magazine as loaded.
                    if (enemy.ammo <= 0 && enemy.reloadTimer <= 0)
                        enemy.ammo = EnemyMagazine(enemy);
                    if (enemy.burstLeft > 0)
                    {
                        enemy.burstTimer -= dt;
                        if (enemy.burstTimer <= 0) FireEnemyRound(enemy);
                    }
                    else if (enemy.aiming)
                    {
                        enemy.aimTimer -= dt;
                        if (enemy.sniper) DrawSniperLaser(enemy);
                        else DrawAimLine(enemy.body.transform.position, enemy.aimDirection, enemy.warning);
                        if (enemy.aimTimer <= 0)
                        {
                            enemy.aiming = false;
                            enemy.warning.SetActive(false);
                            enemy.laser.SetActive(false);
                            enemy.burstLeft = Mathf.Min(enemy.sniper ? 1 : EnemyBurst, enemy.ammo);
                            FireEnemyRound(enemy);
                        }
                    }
                    else if (enemy.reloadTimer <= 0 && enemy.ammo > 0 &&
                        distance <= (enemy.sniper ? enemy.definition.AttackRange : enemy.elite ? 12f : 10f) &&
                        distance >= (enemy.sniper ? 8f : enemy.elite ? 6.5f : 4f) &&
                        HasLineOfSight(enemy.body.transform.position, player.position) && enemy.attackTimer <= 0 && activeShooters < 4)
                    {
                        enemy.aiming = true;
                        enemy.aimTimer = enemy.sniper ? enemy.definition.TelegraphSeconds : 0.8f;
                        // Combat is planar XZ; implicit Vector3 -> Vector2 would drop Z.
                        enemy.aimDirection = new Vector2(toPlayer.x, toPlayer.z).normalized;
                        if (enemy.sniper) DrawSniperLaser(enemy);
                        else enemy.warning.SetActive(true);
                        activeShooters++;
                    }
                    else if (distance < (enemy.sniper ? 8f : enemy.elite ? 6.5f : 4f))
                        MoveEnemyAway(enemy, dt, 2.05f, toPlayer);
                    else if (distance > (enemy.sniper ? 10f : enemy.elite ? 9f : 8f) ||
                        !HasLineOfSight(enemy.body.transform.position, player.position))
                        MoveEnemy(enemy, dt, 2.05f);
                }
                else if (enemy.charger)
                    UpdateCharger(enemy, dt, toPlayer, distance);
                else if (distance <= MeleeReach(enemy))
                {
                    if (!enemy.swingAnnounced && enemy.attackTimer <= MeleeWindup(enemy))
                    {
                        enemy.swingAnnounced = true;
                        EnemyMeleeSwing?.Invoke(enemy.body.transform);
                    }
                    if (enemy.attackTimer <= 0)
                    {
                        enemy.attackTimer = enemy.definition.AttackCooldownSeconds;
                        enemy.swingAnnounced = false;
                        EnemyMeleeStruck?.Invoke(enemy.body.transform);
                        DamagePlayer(enemy.definition.AttackDamage, enemy.body.transform.position);
                        // A killing blow hides every enemy and clears its definition.
                        if (State != CombatState.Playing) return;
                    }
                }
                else
                {
                    // Out of reach the attack stays at least a windup away, so a strike on arrival
                    // is always telegraphed by the full swing.
                    enemy.swingAnnounced = false;
                    enemy.attackTimer = Mathf.Max(enemy.attackTimer, MeleeWindup(enemy));
                    MoveEnemy(enemy, dt, enemy.definition.MoveSpeed);
                }
                if (State != CombatState.Playing) return;
            }
        }

        private static float MeleeReach(Enemy enemy) => enemy.elite ? 1.35f : 1.12f;
        /// <summary>Arrival to first strike; matches the swing clip's contact frame (Elite_Chop 12/30, Infantry_Melee 8/30).</summary>
        private static float MeleeWindup(Enemy enemy) => enemy.elite ? 12f / 30f : 8f / 30f;

        private void UpdateCharger(Enemy enemy, float dt, Vector3 toPlayer, float distance)
        {
            if (enemy.charging)
            {
                Vector3 start = enemy.body.transform.position;
                Vector3 direction = new Vector3(enemy.aimDirection.x, 0, enemy.aimDirection.y);
                MoveWithCover(enemy.body.transform, direction * ChargerDashSpeed * dt, EnemyRadius);
                Vector3 end = enemy.body.transform.position;
                end.x = Mathf.Clamp(end.x, -HalfWidth, HalfWidth);
                end.z = Mathf.Clamp(end.z, -HalfHeight, HalfHeight);
                enemy.body.transform.position = end;
                Vector3 actualMotion = end - start;
                Vector3 closest = ClosestPointOnSegment(player.position, start, end);
                if (!enemy.chargeHit && actualMotion.sqrMagnitude > 0.0001f &&
                    PlanarDistanceSquared(closest, player.position) <= 1.1f * 1.1f &&
                    !RayHitsCover(start, actualMotion.normalized, actualMotion.magnitude, out _))
                {
                    enemy.chargeHit = true;
                    EnemyMeleeStruck?.Invoke(enemy.body.transform);
                    DamagePlayer(enemy.definition.AttackDamage, end);
                    if (State != CombatState.Playing) return;
                }
                enemy.chargeRemaining -= dt;
                if (enemy.chargeRemaining <= 0 || PlanarDistanceSquared(start, end) < 0.0001f)
                {
                    enemy.charging = false;
                    enemy.chargeRemaining = 0;
                    enemy.attackTimer = enemy.definition.AttackCooldownSeconds;
                }
                return;
            }
            if (enemy.aiming)
            {
                enemy.aimTimer -= dt;
                DrawChargeLane(enemy.body.transform.position, enemy.aimDirection, enemy.warning,
                    enemy.definition.AttackRange);
                if (enemy.aimTimer <= 0)
                {
                    enemy.aiming = false;
                    enemy.warning.SetActive(false);
                    enemy.laser.SetActive(false);
                    enemy.charging = true;
                    EnemyChargeStarted?.Invoke(enemy.body.transform, EnemyActionKind.ChargeDash);
                    enemy.chargeRemaining = ChargerDashSeconds;
                    enemy.chargeHit = false;
                }
                return;
            }
            if (distance <= enemy.definition.AttackRange && distance > 1.2f &&
                enemy.attackTimer <= 0 && HasLineOfSight(enemy.body.transform.position, player.position))
            {
                enemy.aiming = true;
                enemy.aimTimer = enemy.definition.TelegraphSeconds;
                EnemyChargeStarted?.Invoke(enemy.body.transform, EnemyActionKind.ChargeTelegraph);
                enemy.aimDirection = new Vector2(toPlayer.x, toPlayer.z).normalized;
                DrawChargeLane(enemy.body.transform.position, enemy.aimDirection, enemy.warning,
                    enemy.definition.AttackRange);
                enemy.warning.SetActive(true);
                return;
            }
            if (distance > enemy.definition.AttackRange || !HasLineOfSight(enemy.body.transform.position, player.position))
                MoveEnemy(enemy, dt, enemy.definition.MoveSpeed);
            else if (distance <= 1.2f && distance > 0.001f)
            {
                MoveWithCover(enemy.body.transform, -toPlayer.normalized * enemy.definition.MoveSpeed * dt,
                    EnemyRadius);
                var position = enemy.body.transform.position;
                position.x = Mathf.Clamp(position.x, -HalfWidth, HalfWidth);
                position.z = Mathf.Clamp(position.z, -HalfHeight, HalfHeight);
                enemy.body.transform.position = position;
            }
        }

        private void MoveEnemy(Enemy enemy, float dt, float speed)
        {
            Vector3 pos = enemy.body.transform.position;
            Vector3 target = player.position;
            int cell = CellOf(pos);
            if (distances[cell] > 0 && distances[cell] < int.MaxValue)
            {
                int best = cell;
                int x = cell % GridWidth, z = cell / GridWidth;
                foreach (var d in Directions)
                {
                    int nx = x + (int)d.x, nz = z + (int)d.y;
                    if (nx < 0 || nz < 0 || nx >= GridWidth || nz >= GridHeight) continue;
                    int next = nz * GridWidth + nx;
                    if (distances[next] < distances[best]) best = next;
                }
                if (best != cell) target = CellPosition(best);
            }
            Vector3 direction = target - pos;
            direction.y = 0;
            if (direction.sqrMagnitude < 0.04f) return;
            MoveWithCover(enemy.body.transform, direction.normalized * speed * dt, EnemyRadius);
        }

        private void MoveEnemyAway(Enemy enemy, float dt, float speed, Vector3 toPlayer)
        {
            if (toPlayer.sqrMagnitude < .01f) return;
            MoveWithCover(enemy.body.transform, -toPlayer.normalized * speed * dt, EnemyRadius);
        }

        private void UpdateBullets(float dt)
        {
            foreach (var bullet in bullets)
            {
                if (!bullet.active) continue;
                Vector3 start = bullet.body.transform.position;
                Vector3 delta = bullet.velocity * dt;
                float length = delta.magnitude;
                if (RayHitsCover(start, delta.normalized, length, out float coverHit))
                {
                    SurfaceImpact?.Invoke(start + delta.normalized * coverHit, true);
                    Deactivate(bullet);
                    continue;
                }
                var point = ClosestPointOnSegment(player.position, start, start + delta);
                var playerFeet = player.position.y - GroundY;
                var planarPlayer = new Vector3(player.position.x, start.y, player.position.z);
                var highPoint = ClosestPointOnSegment(planarPlayer, start, start + delta);
                bool sniperHit = bullet.highShot && PlanarDistanceSquared(highPoint, player.position) <= .47f * .47f &&
                    highPoint.y >= playerFeet && highPoint.y <= playerFeet + PlayerHeight;
                if (sniperHit || (!bullet.highShot && (point - player.position).sqrMagnitude <= 0.47f * 0.47f))
                {
                    DamagePlayer(bullet.damage, start - bullet.velocity);
                    Deactivate(bullet);
                    if (State != CombatState.Playing) return;
                    continue;
                }
                // Wait until the segment passes the player, rather than announcing an approaching bullet.
                if (!bullet.nearMissPlayed && (point - player.position).sqrMagnitude <= 1.5f * 1.5f &&
                    Vector3.Dot(player.position - start - delta, bullet.velocity) <= 0f)
                {
                    bullet.nearMissPlayed = true;
                    BulletNearMiss?.Invoke(point);
                }
                bullet.body.transform.position = start + delta;
                bullet.remaining -= dt;
                if (bullet.remaining <= 0 || Mathf.Abs(bullet.body.transform.position.x) > 17 || Mathf.Abs(bullet.body.transform.position.z) > 10)
                    Deactivate(bullet);
            }
        }

        private void UpdatePickups()
        {
            foreach (var pickup in pickups)
            {
                if (!pickup.active) continue;
                Vector3 difference = player.position - pickup.body.transform.position;
                difference.y = 0;
                if (difference.sqrMagnitude > Stats.PickupRadius * Stats.PickupRadius) continue;
                if (Run.TryCollectPickup(pickup.id, pickup.valueMinor))
                    PickupCollected?.Invoke(pickup.body.transform.position);
                pickup.active = false;
                pickup.body.SetActive(false);
            }
        }

        private void FireWeapon()
        {
            if(LauncherEquipped) {FireLauncher();return;}
            if (KnifeOut || KnifeSwinging)
            {
                AimOnTarget = false;
                // The knife in hand slashes on the trigger, or by itself at an enemy within reach.
                if (KnifeOut && (manualAim ? triggerHeld : FindTarget(CombatRules.KnifeReach) >= 0)) Slash();
                return;
            }
            if (manualAim)
            {
                FireManualWeapons();
                return;
            }
            int slot = ActiveWeaponSlot;
            if (!ActiveWeaponReady()) return;
            WeaponDefinition weapon = WeaponAt(slot);
            int target = FindTarget(weapon.Range * Stats.RangeFactor);
            if (target < 0) return;
            var enemy = enemies[target];
            Vector3 start = player.position + Vector3.up * .08f;
            Vector3 end = enemy.body.transform.position + Vector3.up * .08f;
            Vector3 direction = end - start;
            direction.y = 0;
            if (RayHitsCover(start, direction.normalized, direction.magnitude, out _)) { targetIndex = -1; return; }
            weaponAmmo[slot]--;
            weaponFireTimer[slot] = weapon.ShotCooldownSeconds / Mathf.Max(.1f, 1 + Stats.AttackSpeedBonus);
            tracerStart = start;
            tracerEnd = end;
            ShowTracer();
            WeaponFired?.Invoke(slot, start, end);
            ShotImpact?.Invoke(end, true);
            ApplyShotDamage(enemy, ShotDamage(slot, weapon, out bool critical), critical);
            if (weaponAmmo[slot] == 0) StartReload(slot);
        }

        /// <summary>The weapon in hand is drawn, loaded and off cooldown; an empty one starts reloading.</summary>
        private bool ActiveWeaponReady()
        {
            int slot = ActiveWeaponSlot;
            if (slot >= WeaponCount || switchRemaining > 0 || weaponReload[slot] > 0 || weaponFireTimer[slot] > 0) return false;
            if (weaponAmmo[slot] > 0) return true;
            StartReload(slot);
            return false;
        }

        private float ShotDamage(int slot, WeaponDefinition weapon, out bool critical)
        {
            int tier = Run.Weapons[slot].Tier;
            float tierFactor = tier == 1 ? 1f : tier == 2 ? 1.35f : 1.8f;
            critical = rng.NextDouble() < Stats.CriticalChance;
            return weapon.DamagePerHit * weapon.PelletsPerShot * tierFactor * Stats.DamageFactor * (critical ? 1.5f : 1f);
        }

        /// <summary>Trigger-driven fire along the crosshair ray; misses still spend ammunition.</summary>
        private void FireManualWeapons()
        {
            int slot = ActiveWeaponSlot;
            WeaponDefinition weapon = WeaponAt(slot);
            float range = weapon == null ? 0 : weapon.Range * Stats.RangeFactor * ManualRangeFactor;
            AimOnTarget = weapon != null && FindAimTarget(range) >= 0;
            if (!triggerHeld || !ActiveWeaponReady()) return;
            int target = FindAimTarget(range);
            Vector3 start = player.position + Vector3.up * .08f;
            Vector3 end;
            bool surfaceHit = false, hitCover = false;
            if (target >= 0)
            {
                // Keep the hit on the view ray so the tracer and impact share the actual
                // head/chest point under the crosshair, including pitched aim.
                Vector3 rayOrigin = hasViewRay ? ShotRayOrigin(viewRayOrigin, viewRayDirection) : start;
                Vector3 rayDirection = hasViewRay ? viewRayDirection : aimDirection;
                end = AimTargetPoint(target, rayOrigin, rayDirection);
            }
            else
            {
                Vector3 rayOrigin = hasViewRay ? ShotRayOrigin(viewRayOrigin, viewRayDirection) : start;
                Vector3 rayDirection = hasViewRay ? viewRayDirection : aimDirection;
                hitCover = RayHitsCover(rayOrigin, rayDirection, range, out float hit);
                float distance = hitCover ? hit : range;
                surfaceHit = hitCover;
                if (hasViewRay && RayHitsGround(rayOrigin, rayDirection, range, out float ground))
                {
                    if (ground < distance)
                    {
                        distance = ground;
                        hitCover = false;
                    }
                    surfaceHit = true;
                }
                end = rayOrigin + rayDirection * distance;
            }
            weaponAmmo[slot]--;
            weaponFireTimer[slot] = weapon.ShotCooldownSeconds / Mathf.Max(.1f, 1 + Stats.AttackSpeedBonus);
            tracerStart = start;
            tracerEnd = end;
            ShowTracer();
            WeaponFired?.Invoke(slot, start, end);
            ShotImpact?.Invoke(end, target >= 0);
            if (surfaceHit) SurfaceImpact?.Invoke(end, hitCover);
            if (target >= 0)
            {
                var enemy = enemies[target];
                bool headshot = false;
                if (hasViewRay)
                {
                    Vector3 position = enemy.body.transform.position;
                    var planar = new Vector2(viewRayDirection.x, viewRayDirection.z);
                    float alongRay = Vector2.Dot(new Vector2(position.x - viewRayOrigin.x,
                        position.z - viewRayOrigin.z), planar) / Mathf.Max(.000001f, planar.sqrMagnitude);
                    float height = viewRayOrigin.y + viewRayDirection.y * alongRay - (position.y - .8f);
                    headshot = CombatRules.IsHeadshot(height, EnemyBodyHeight(enemy));
                }
                float damage = ShotDamage(slot, weapon, out bool critical);
                float headshotMultiplier = ReferenceEquals(enemy, commander)
                    ? CombatRules.CommanderHeadshotMultiplier
                    : CombatRules.HeadshotMultiplier;
                ApplyDetailedShotDamage(enemy, damage * (headshot ? headshotMultiplier : 1f), critical, headshot, end);
            }
            if (weaponAmmo[slot] == 0) StartReload(slot);
        }

        /// <summary>What a shot hits: the enemy under the crosshair, if it is in range and no cover stands
        /// between the hero and it along the aim.</summary>
        private int FindAimTarget(float range)
        {
            Vector3 origin = hasViewRay ? ShotRayOrigin(viewRayOrigin, viewRayDirection) : player.position;
            Vector3 direction = hasViewRay ? viewRayDirection : aimDirection;
            int best = CrosshairEnemy(origin, direction, range);
            if (best >= 0)
            {
                float distance = Vector3.Distance(origin, AimTargetPoint(best, origin, direction));
                if (RayHitsCover(origin, direction, distance, out _)) best = -1;
            }
            targetIndex = best;
            return best;
        }

        // TPS camera rays start behind the soldier. Only their forward part can stop a shot.
        private Vector3 ShotRayOrigin(Vector3 origin, Vector3 direction)
        {
            var planar = new Vector3(direction.x, 0, direction.z);
            float along = planar.sqrMagnitude > .000001f
                ? Vector3.Dot(player.position - origin, planar) / planar.sqrMagnitude : 0;
            Vector3 forwardOrigin = origin + direction * Mathf.Max(0, along);
            // If the camera ray met the floor behind the hero, fire down from the gun instead.
            // Starting the projected ray underground would miss the ground's forward intersection.
            return forwardOrigin.y <= 0 ? player.position + Vector3.up * .08f : forwardOrigin;
        }

        private Vector3 AimTargetPoint(int target, Vector3 origin, Vector3 direction)
        {
            var enemy = enemies[target];
            if (ReferenceEquals(enemy, commander))
            {
                // Intersect the same finite body/head column used by targeting.
                Vector3 position = enemy.body.transform.position;
                var planar = new Vector2(direction.x, direction.z);
                float alongRay = Vector2.Dot(new Vector2(position.x - origin.x,
                    position.z - origin.z), planar) / Mathf.Max(.000001f, planar.sqrMagnitude);
                float rayHeight = origin.y + direction.y * alongRay - (position.y - .8f);
                float bodyRadius = EnemyAimRadius(enemy, rayHeight);
                Vector3 offset = origin - position;
                float a = direction.x * direction.x + direction.z * direction.z;
                float bodyB = offset.x * direction.x + offset.z * direction.z;
                float bodyC = offset.x * offset.x + offset.z * offset.z - bodyRadius * bodyRadius;
                float bodyDiscriminant = bodyB * bodyB - a * bodyC;
                if (a > .000001f && bodyDiscriminant >= 0)
                {
                    float near = Mathf.Max(0, (-bodyB - Mathf.Sqrt(bodyDiscriminant)) / a);
                    float far = (-bodyB + Mathf.Sqrt(bodyDiscriminant)) / a;
                    if (ClipAxis(origin.y, direction.y, position.y - .8f,
                        position.y - .8f + EnemyBodyHeight(enemy), ref near, ref far))
                        return origin + direction * near;
                }
            }
            Vector3 center = enemy.body.transform.position + Vector3.up * .45f;
            const float radius = .62f;
            float b = Vector3.Dot(origin - center, direction);
            float c = (origin - center).sqrMagnitude - radius * radius;
            float discriminant = b * b - c;
            if (discriminant >= 0)
            {
                float t = -b - Mathf.Sqrt(discriminant);
                if (t >= 0) return origin + direction * t;
                t = -b + Mathf.Sqrt(discriminant);
                if (t >= 0) return origin + direction * t;
            }
            return origin + direction * Mathf.Max(0, Vector3.Dot(center - origin, direction));
        }

        private static bool RayHitsGround(Vector3 origin, Vector3 direction, float distance, out float hit)
        {
            hit = float.MaxValue;
            if (direction.y >= -.0001f) return false;
            float t = -origin.y / direction.y;
            if (t < 0 || t > distance) return false;
            hit = t;
            return true;
        }

        /// <summary>The enemy under the crosshair by the rule shots use, ignoring range and cover; for the aim point.</summary>
        public bool TryGetCrosshairEnemy(Vector3 rayOrigin, Vector3 rayDirection, out Vector3 bodyPoint)
        {
            int enemy = player == null ? -1 : CrosshairEnemy(rayOrigin, rayDirection, float.PositiveInfinity);
            Vector3 position = enemy >= 0 ? enemies[enemy].body.transform.position : Vector3.zero;
            bodyPoint = new Vector3(position.x, .88f, position.z);
            return enemy >= 0;
        }

        /// <summary>
        /// The one crosshair rule, shared by hits and the HUD: the first active enemy in front of the hero whose body
        /// (a column ManualAimRadius wide, from the ground to BodyHeight) the ray passes through, closer to the hero
        /// along the aim than <paramref name="reach"/>.
        /// </summary>
        private int CrosshairEnemy(Vector3 origin, Vector3 direction, float reach)
        {
            var planar = new Vector2(direction.x, direction.z);
            float speed = planar.magnitude;
            if (speed < .0001f) return -1;
            planar /= speed;
            int best = -1;
            float bestAlong = float.MaxValue;
            for (int i = 0; i < enemies.Length; i++)
            {
                if (!enemies[i].active) continue;
                Vector3 position = enemies[i].body.transform.position;
                var offset = new Vector2(position.x - origin.x, position.z - origin.z);
                float along = Vector2.Dot(offset, planar);
                if (along <= 0 || along >= bestAlong) continue;
                // The ray's height above the enemy's feet where it passes him; the actor sits at capsule centre.
                float height = origin.y + direction.y * (along / speed) - (position.y - .8f);
                if (height < -.1f || height > EnemyBodyHeight(enemies[i])) continue;
                float radius = EnemyAimRadius(enemies[i], height);
                if ((offset - planar * along).sqrMagnitude > radius * radius) continue;
                var fromHero = new Vector2(position.x - player.position.x, position.z - player.position.z);
                if (Vector2.Dot(fromHero, planar) <= 0) continue;
                if (Vector2.Dot(fromHero, new Vector2(aimDirection.x, aimDirection.z)) >= reach) continue;
                best = i;
                bestAlong = along;
            }
            return best;
        }

        /// <summary>Presentation read-out for health bars; 1 for unknown or inactive actors.</summary>
        public float EnemyHealthFraction(Transform actor)
        {
            foreach (var enemy in enemies)
                if (enemy.body.transform == actor)
                    return enemy.active && enemy.definition != null ? Mathf.Clamp01(enemy.hp / enemy.definition.MaxHealth) : 1f;
            return 1f;
        }

        public WeaponDefinition WeaponAt(int slot) => Run != null && slot >= 0 && slot < WeaponCount
            ? ContentCatalog.P2.GetWeapon(Run.Weapons[slot].Id) : null;

        public int AmmoAt(int slot) => slot >= 0 && slot < WeaponCount ? weaponAmmo[slot] : 0;
        public float ReloadAt(int slot) => slot >= 0 && slot < WeaponCount ? weaponReload[slot] : 0;
        /// <summary>Length of the reload in progress in a slot (0 when it is not reloading).</summary>
        public float ReloadDurationAt(int slot) => ReloadAt(slot) > 0 ? weaponReloadTotal[slot] : 0;
        private int MagazineSizeAt(int slot) => WeaponAt(slot).Id==ContentIds.Rpg18 ? 1 : ProgressionRules.MagazineSize(WeaponAt(slot).MagazineSize, Stats);

        private int FindTarget(float range)
        {
            float bestDistance = range * range;
            int best = -1;
            if (targetIndex >= 0 && enemies[targetIndex].active)
            {
                float distance = PlanarDistanceSquared(player.position, enemies[targetIndex].body.transform.position);
                if (distance <= bestDistance && HasLineOfSight(player.position, enemies[targetIndex].body.transform.position))
                { best = targetIndex; bestDistance = distance * 1.2f; }
            }
            for (int i = 0; i < enemies.Length; i++)
            {
                if (!enemies[i].active) continue;
                float distance = PlanarDistanceSquared(player.position, enemies[i].body.transform.position);
                if (distance >= bestDistance || !HasLineOfSight(player.position, enemies[i].body.transform.position)) continue;
                best = i;
                bestDistance = distance;
            }
            targetIndex = best;
            return best;
        }

        private void DamageEnemy(Enemy enemy, float damage) => ApplyShotDamage(enemy, damage, false);

        private void ApplyShotDamage(Enemy enemy, float damage, bool critical) =>
            ApplyDetailedShotDamage(enemy, damage, critical, false, null);

        private void ApplyDetailedShotDamage(Enemy enemy, float damage, bool critical, bool headshot, Vector3? hitPoint)
        {
            if (CommanderCinematic || !enemy.active || enemy.definition == null || damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            float applied = Mathf.Min(damage, enemy.hp);
            enemy.hp -= damage;
            DamageDealt += applied;
            DamageFeedback?.Invoke(hitPoint ?? enemy.body.transform.position + Vector3.up * .65f,
                applied, headshot, critical);
            EnemyDamaged?.Invoke(enemy.body.transform, applied, critical, enemy.hp <= 0);
            if (enemy.hp > 0) return;
            if(ReferenceEquals(enemy,commander)) {CommanderDefeated();return;}
            enemy.active = false;
            enemy.aiming = false;
            enemy.body.SetActive(false);
            enemy.warning.SetActive(false);
            enemy.laser.SetActive(false);
            AliveEnemies--;
            Kills++;
            if (Run.TryRewardEnemy(Kills, enemy.definition.ExperienceReward, enemy.definition.SupplyReward, out long pickupMinor))
                SpawnPickup(enemy.body.transform.position, pickupMinor);
            enemy.elite = enemy.shooter = enemy.charger = enemy.charging = enemy.chargeHit = false;
            enemy.definition = null;
            enemy.chargeRemaining = enemy.aimTimer = 0;
            enemy.aimDirection = Vector2.zero;
            enemy.chargerMarker.SetActive(false);
        }

        private void UpdateSupply(float dt, bool waveComplete)
        {
            if (supplyEvent == null) return;
            float previousProgress = SupplyProgress;
            float elapsed = WaveDuration(Wave) - WaveRemaining;
            bool inRange = supplyCrate != null && supplyCrate.activeSelf &&
                PlanarDistanceSquared(player.position, supplyCrate.transform.position) <= 1.5f * 1.5f;
            // The first simulation slice of a wave can be shorter than the fixed dt
            // after floating-point subtraction; never claim more elapsed time than exists.
            supplyEvent.Advance(Wave, elapsed, Mathf.Min(Mathf.Max(0, dt), Mathf.Max(0, elapsed)),
                inRange, Health > 0, waveComplete);
            if (supplyEvent.Phase == SupplyEventPhase.Active || supplyEvent.Phase == SupplyEventPhase.Completed)
            {
                if (!SupplyVisible && supplyCrate != null)
                {
                    supplyCrate.transform.position = new Vector3(0, .36f, 7.4f);
                    supplyCrate.SetActive(true);
                    SupplyVisible = true;
                    SupplySpawned?.Invoke(supplyCrate.transform.position);
                }
            }
            SupplyProgress = supplyEvent.RecoverySeconds <= 0 ? 0 :
                Mathf.Clamp01(supplyEvent.ProgressSeconds / supplyEvent.RecoverySeconds);
            for (int milestone = 1; milestone < 4; milestone++)
                if (previousProgress < milestone * .25f && SupplyProgress >= milestone * .25f)
                    SupplyMilestone?.Invoke(milestone);
            if (supplyEvent.IsClaimable && supplyEvent.TryClaim(Health > 0, out var reward))
            {
                Run.AddCurrency(reward.CurrencyMinorUnits);
                Health = Mathf.Min(MaxHealth, Health + reward.Heal);
                SupplyVisible = false;
                SupplyProgress = 1;
                SupplyClaimed?.Invoke(supplyCrate != null ? supplyCrate.transform.position : player.position);
                if (supplyCrate != null) supplyCrate.SetActive(false);
            }
        }

        private static float WaveDuration(int wave) => wave >= 5 ? 50f : CombatRules.WaveSeconds;

        private void SpawnPickup(Vector3 at, long valueMinor)
        {
            foreach (var pickup in pickups)
            {
                if (pickup.active) continue;
                pickup.active = true;
                pickup.id = ++nextPickupId;
                pickup.valueMinor = valueMinor;
                pickup.body.transform.position = new Vector3(at.x, .23f, at.z);
                pickup.body.SetActive(true);
                return;
            }
            // The pool is bounded; coalesce value into an existing visible pickup.
            pickups[0].valueMinor += valueMinor;
        }

        private void FireEnemyBullet(Transform actor, Vector2 direction, float damage, float spreadDegrees)
        {
            foreach (var bullet in bullets)
            {
                if (bullet.active) continue;
                bullet.active = true;
                bullet.nearMissPlayed = false;
                bullet.remaining = 3f;
                float spread = UnityEngine.Random.Range(-spreadDegrees, spreadDegrees) * Mathf.Deg2Rad;
                Vector2 shot = new Vector2(direction.x * Mathf.Cos(spread) - direction.y * Mathf.Sin(spread),
                    direction.x * Mathf.Sin(spread) + direction.y * Mathf.Cos(spread));
                bullet.damage = damage;
                bool sniper = TryGetEnemyAction(actor, out var action) && action.Role == EnemyRole.Sniper;
                bullet.highShot = sniper;
                Vector3 origin = sniper ? action.ShotOrigin :
                    actor.position + Vector3.up * .35f;
                Vector3 target = player.position + Vector3.up * .45f;
                Vector3 trajectory = target - origin;
                float horizontal = new Vector2(trajectory.x, trajectory.z).magnitude;
                trajectory.x = shot.x * horizontal; trajectory.z = shot.y * horizontal;
                bullet.velocity = sniper ? new Vector3(shot.x, 0, shot.y) * 18f : trajectory.normalized * 18f;
                bullet.body.transform.position = origin;
                bullet.body.SetActive(true);
                EnemyFired?.Invoke(actor, new Vector3(direction.x, 0, direction.y));
                return;
            }
        }

        private static Vector3 SniperOrigin(Enemy enemy)
        {
            var position = enemy.body.transform.position;
            return new Vector3(position.x + enemy.aimDirection.x * 1f, SniperShotHeight,
                position.z + enemy.aimDirection.y * 1f);
        }

        private void DrawSniperLaser(Enemy enemy)
        {
            var origin = SniperOrigin(enemy);
            var direction = new Vector3(enemy.aimDirection.x, 0, enemy.aimDirection.y);
            // End near the player instead of extending behind the shoulder camera and filling its near plane.
            float range = Mathf.Min(enemy.definition.AttackRange,
                Mathf.Sqrt(PlanarDistanceSquared(origin, player.position)) + .5f);
            float length = RayHitsCover(origin, direction, range, out float hit) ? hit : range;
            enemy.laser.transform.position = origin + direction * (length * .5f);
            enemy.laser.transform.rotation = Quaternion.LookRotation(direction);
            enemy.laser.transform.localScale = new Vector3(.035f, .035f, length);
            enemy.laser.SetActive(true);
        }

        private void OnDestroy()
        {
            if (sniperLaserMaterial != null) Destroy(sniperLaserMaterial);
        }

        private void DrawAimLine(Vector3 origin, Vector2 direction, GameObject line)
        {
            line.transform.position = origin + new Vector3(direction.x, 0, direction.y) * 2.3f;
            line.transform.position = new Vector3(line.transform.position.x, .06f, line.transform.position.z);
            line.transform.localScale = new Vector3(.08f, .025f, 4.5f);
            line.transform.rotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.y));
        }

        private static void DrawChargeLane(Vector3 origin, Vector2 direction, GameObject lane, float length)
        {
            var forward = new Vector3(direction.x, 0, direction.y);
            lane.transform.position = origin + forward * (length * 0.5f);
            lane.transform.position = new Vector3(lane.transform.position.x, .06f, lane.transform.position.z);
            lane.transform.localScale = new Vector3(.7f, .025f, length);
            lane.transform.rotation = Quaternion.LookRotation(forward);
        }

        private void ShowTracer()
        {
            tracerRemaining = .09f;
            tracer.SetActive(true);
            Vector3 delta = tracerEnd - tracerStart;
            tracer.transform.position = (tracerStart + tracerEnd) * .5f;
            tracer.transform.localScale = new Vector3(.065f, .06f, delta.magnitude);
            tracer.transform.rotation = Quaternion.LookRotation(delta);
        }

        private void BuildArena()
        {
            var ground = Primitive("Ground", PrimitiveType.Cube, new Vector3(0, -.22f, 0), new Vector3(34, .4f, 20), GroundMaterial);
            ground.transform.SetParent(transform, true);
            foreach (var cover in covers)
            {
                var box = Primitive("Cover", PrimitiveType.Cube,
                    new Vector3(cover.center.x, CoverHeight * .5f, cover.center.y),
                    new Vector3(cover.half.x * 2, CoverHeight, cover.half.y * 2), CoverMaterial);
                box.transform.SetParent(transform, true);
            }
            var person = Primitive("Player", PrimitiveType.Capsule, new Vector3(0, .8f, 0), new Vector3(.72f, .8f, .72f), PlayerMaterial);
            person.transform.SetParent(transform, true);
            player = person.transform;
            playerRenderer = person.GetComponent<Renderer>();
            Destroy(person.GetComponent<Collider>());
            tracer = Primitive("Tracer", PrimitiveType.Cube, Vector3.zero, Vector3.one, ProjectileMaterial);
            tracer.transform.SetParent(transform, true);
            Destroy(tracer.GetComponent<Collider>());
            supplyCrate = Primitive("SupplyCrate", PrimitiveType.Cube, new Vector3(0, .36f, 7.4f),
                new Vector3(.8f, .7f, .8f), PickupMaterial);
            supplyCrate.transform.SetParent(transform, true);
            Destroy(supplyCrate.GetComponent<Collider>());
        }

        private void BuildPools()
        {
            sniperLaserMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            sniperLaserMaterial.name = "SniperLaserRed";
            sniperLaserMaterial.color = new Color(1f, .03f, .02f);
            for (int i = 0; i < enemies.Length; i++)
            {
                var body = Primitive("Enemy_" + i, PrimitiveType.Capsule, Vector3.zero, new Vector3(.7f, .75f, .7f), EnemyMaterial);
                body.transform.SetParent(transform, true);
                Destroy(body.GetComponent<Collider>());
                var warning = Primitive("EnemyWarning_" + i, PrimitiveType.Cylinder, Vector3.zero, new Vector3(1.15f, .015f, 1.15f), WarningMaterial);
                warning.transform.SetParent(transform, true);
                Destroy(warning.GetComponent<Collider>());
                var marker = Primitive("ChargerMarker_" + i, PrimitiveType.Cube, Vector3.zero,
                    new Vector3(1f, .12f, .22f), ShooterMaterial != null ? ShooterMaterial : EnemyMaterial);
                marker.transform.SetParent(body.transform, false);
                marker.transform.localPosition = new Vector3(0, .92f, 0);
                Destroy(marker.GetComponent<Collider>());
                marker.SetActive(false);
                var laser = Primitive("SniperLaser_" + i, PrimitiveType.Cube, Vector3.zero, Vector3.one, sniperLaserMaterial);
                laser.transform.SetParent(transform, true);
                Destroy(laser.GetComponent<Collider>());
                laser.SetActive(false);
                enemies[i] = new Enemy { body = body, warning = warning, chargerMarker = marker, laser = laser };
            }
            for (int i = 0; i < bullets.Length; i++)
            {
                var body = Primitive("EnemyBullet_" + i, PrimitiveType.Sphere, Vector3.zero, Vector3.one * .23f, ProjectileMaterial);
                body.transform.SetParent(transform, true);
                Destroy(body.GetComponent<Collider>());
                bullets[i] = new Bullet { body = body };
            }
            for (int i = 0; i < pickups.Length; i++)
            {
                var body = Primitive("Pickup_" + i, PrimitiveType.Cube, Vector3.zero, Vector3.one * .35f, PickupMaterial);
                body.transform.SetParent(transform, true);
                Destroy(body.GetComponent<Collider>());
                pickups[i] = new Pickup { body = body };
            }
        }

        private void HideTransientObjects()
        {
            ResetCommander();
            SniperTutorialVisible = false;
            TransientsCleared?.Invoke();
            foreach (var enemy in enemies)
            {
                enemy.active = enemy.pending = enemy.shooter = enemy.elite = enemy.charger =
                    enemy.aiming = enemy.charging = enemy.chargeHit = false;
                enemy.definition = null;
                enemy.hp = enemy.attackTimer = enemy.aimTimer = enemy.warningTimer =
                    enemy.pathTimer = enemy.chargeRemaining = enemy.reloadTimer = enemy.burstTimer = 0;
                enemy.ammo = enemy.burstLeft = 0;
                enemy.swingAnnounced = false;
                enemy.aimDirection = Vector2.zero;
                enemy.body.SetActive(false);
                enemy.warning.SetActive(false);
                enemy.laser.SetActive(false);
                enemy.chargerMarker.SetActive(false);
                enemy.body.transform.localScale = new Vector3(.7f, .75f, .7f);
            }
            foreach (var bullet in bullets) Deactivate(bullet);
            foreach (var pickup in pickups) { pickup.active = false; pickup.body.SetActive(false); pickup.valueMinor = 0; }
            tracer.SetActive(false);
            tracerRemaining = 0;
            if (supplyCrate != null) supplyCrate.SetActive(false);
            SupplyVisible = false;
            SupplyProgress = 0;
        }

        private void BuildNavigation()
        {
            for (int z = 0; z < GridHeight; z++)
            for (int x = 0; x < GridWidth; x++)
            {
                var point = new Vector2(x - 17, z - 10);
                blocked[z * GridWidth + x] = Mathf.Abs(point.x) > HalfWidth || Mathf.Abs(point.y) > HalfHeight || IsInsideCover(point, EnemyRadius + .1f);
            }
        }

        private void UpdateNavigation()
        {
            for (int i = 0; i < distances.Length; i++) distances[i] = int.MaxValue;
            int source = CellOf(player.position), head = 0, tail = 0;
            distances[source] = 0;
            queue[tail++] = source;
            while (head < tail)
            {
                int current = queue[head++], x = current % GridWidth, z = current / GridWidth;
                foreach (var direction in Directions)
                {
                    int nx = x + (int)direction.x, nz = z + (int)direction.y;
                    if (nx < 0 || nz < 0 || nx >= GridWidth || nz >= GridHeight) continue;
                    int next = nz * GridWidth + nx;
                    if (blocked[next] || distances[next] != int.MaxValue) continue;
                    distances[next] = distances[current] + 1;
                    queue[tail++] = next;
                }
            }
        }

        private int CellOf(Vector3 position)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(position.x + 17), 0, GridWidth - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(position.z + 10), 0, GridHeight - 1);
            return z * GridWidth + x;
        }

        private static Vector3 CellPosition(int cell) => new Vector3(cell % GridWidth - 17, .8f, cell / GridWidth - 10);

        private bool IsInsideCover(Vector2 point, float radius)
        {
            foreach (var cover in covers)
                if (Mathf.Abs(point.x - cover.center.x) < cover.half.x + radius && Mathf.Abs(point.y - cover.center.y) < cover.half.y + radius)
                    return true;
            return false;
        }

        private void MoveWithCover(Transform actor, Vector3 delta, float radius)
        {
            // Axis-separated swept motion slides along cover faces and prevents tunneling on long steps.
            Vector3 position = actor.position;
            MoveAxis(ref position, delta.x, true, radius);
            MoveAxis(ref position, delta.z, false, radius);
            actor.position = position;
        }

        private void MoveAxis(ref Vector3 position, float amount, bool horizontal, float radius)
        {
            if (amount == 0) return;
            float destination = (horizontal ? position.x : position.z) + amount;
            foreach (var cover in covers)
            {
                float cross = horizontal ? position.z : position.x;
                float otherCenter = horizontal ? cover.center.y : cover.center.x;
                float otherHalf = horizontal ? cover.half.y : cover.half.x;
                if (Mathf.Abs(cross - otherCenter) >= otherHalf + radius) continue;
                float center = horizontal ? cover.center.x : cover.center.y;
                float half = horizontal ? cover.half.x : cover.half.y;
                float near = center - half - radius, far = center + half + radius;
                float old = horizontal ? position.x : position.z;
                if (amount > 0 && old <= near && destination > near) destination = near;
                if (amount < 0 && old >= far && destination < far) destination = far;
            }
            if (horizontal) position.x = destination; else position.z = destination;
        }

        private bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            delta.y = 0;
            return !RayHitsCover(from, delta.normalized, delta.magnitude, out _);
        }

        private bool RayHitsCover(Vector3 origin, Vector3 direction, float distance, out float hitDistance)
        {
            hitDistance = float.MaxValue;
            if (distance <= 0) return false;
            // Sweep the actual finite cover box: a shot above the sandbags must clear it.
            foreach (var cover in covers)
            {
                float near = 0, far = distance;
                if (!ClipAxis(origin.x, direction.x, cover.center.x - cover.half.x, cover.center.x + cover.half.x, ref near, ref far)) continue;
                if (!ClipAxis(origin.z, direction.z, cover.center.y - cover.half.y, cover.center.y + cover.half.y, ref near, ref far)) continue;
                if (!ClipAxis(origin.y, direction.y, 0, CoverHeight, ref near, ref far)) continue;
                hitDistance = Mathf.Min(hitDistance, near);
            }
            return hitDistance <= distance;
        }

        private static bool ClipAxis(float origin, float direction, float min, float max, ref float near, ref float far)
        {
            if (Mathf.Abs(direction) < 0.0001f) return origin >= min && origin <= max;
            float a = (min - origin) / direction, b = (max - origin) / direction;
            if (a > b) { float temp = a; a = b; b = temp; }
            near = Mathf.Max(near, a);
            far = Mathf.Min(far, b);
            return near <= far;
        }

        private static float PlanarDistanceSquared(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z;
        }

        private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            return lengthSquared <= 0 ? start : start + segment * Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
        }

        private static void Deactivate(Bullet bullet) { bullet.active = false; bullet.body.SetActive(false); }

        private static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            gameObject.transform.position = position;
            gameObject.transform.localScale = scale;
            if (material != null) gameObject.GetComponent<Renderer>().sharedMaterial = material;
            return gameObject;
        }
    }

    public enum EnemyActionKind { None, Melee, ChargeTelegraph, ChargeDash }

    /// <summary>Read-only attack state of one enemy, see <see cref="CombatGame.TryGetEnemyAction"/>.</summary>
    public struct EnemyActionState
    {
        public EnemyRole Role;
        public bool Aiming;
        public Vector3 AimDirection, ShotOrigin;
        public EnemyActionKind Kind;
        /// <summary>Charger telegraph, 0 at its start to 1 when the dash begins.</summary>
        public float TelegraphProgress;
        /// <summary>Seconds until the enemy may attack again.</summary>
        public float AttackCooldownRemaining;
        /// <summary>Melee enemies: the player is close enough that the next ready attack lands.</summary>
        public bool InMeleeReach;
        /// <summary>Charger telegraph and dash: the planar lane direction.</summary>
        public Vector3 ChargeDirection;
        /// <summary>Ranged enemies: seconds a magazine change lasts, and seconds left of the current one (0 when not reloading).</summary>
        public float ReloadSeconds, ReloadRemaining;
    }
}
