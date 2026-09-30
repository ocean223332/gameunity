using System;
using TienTuyen.Content;
using TienTuyen.Progression;
using TienTuyen.Waves;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TienTuyen.Combat
{
    /// <summary>Self-contained graybox combat loop for the three-wave P1 milestone.</summary>
    public sealed class CombatGame : MonoBehaviour
    {
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
        /// <summary>World position of the attacker and the damage the player took.</summary>
        public event Action<Vector3, float> PlayerDamaged;

        /// <summary>True once a view controller has supplied manual aim; tests and tools keep auto-aim.</summary>
        public bool ManualAim => manualAim;
        /// <summary>Manual aim only: the crosshair ray currently rests on a reachable enemy.</summary>
        public bool AimOnTarget { get; private set; }
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
        public int MagazineSize => ProgressionRules.MagazineSize(CurrentWeaponDefinition.MagazineSize, Stats);
        public float ReloadDuration => CurrentWeaponDefinition.ReloadSeconds * Stats.ReloadFactor;
        public int Ammo => weaponAmmo[0];
        public float ReloadRemaining => weaponReload[0];
        public ProgressionRun Run { get; private set; }
        public ShopSession Shop { get; private set; }
        public ProgressionStats Stats => Run == null ? ProgressionRules.Aggregate(Array.Empty<StatModifier>()) : Run.Stats;
        public int WeaponCount => Run?.Weapons.Count ?? 0;
        public int UpgradeChoiceCount => Run?.CurrentChoices.Count ?? 0;
        public bool SupplyVisible { get; private set; }
        public float SupplyProgress { get; private set; }
        public int ActiveChargerCount
        {
            get { var count = 0; foreach (var enemy in enemies) if (enemy.active && enemy.charger) count++; return count; }
        }
        public int PendingChargerCount
        {
            get { var count = 0; foreach (var enemy in enemies) if (enemy.pending && enemy.charger) count++; return count; }
        }
        public bool ChargerTelegraphVisible
        {
            get { foreach (var enemy in enemies) if (enemy.active && enemy.charger && enemy.aiming) return true; return false; }
        }
        public bool ChargerDashActive
        {
            get { foreach (var enemy in enemies) if (enemy.active && enemy.charger && enemy.charging) return true; return false; }
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
        private static readonly Vector2[] Directions = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };

        private struct Cover
        {
            public Vector2 center, half;
            public Cover(float x, float z, float width, float depth)
            { center = new Vector2(x, z); half = new Vector2(width, depth) * 0.5f; }
        }

        private sealed class Enemy
        {
            public GameObject body, warning, chargerMarker;
            public bool active, pending, shooter, elite, charger, aiming, charging, chargeHit;
            public EnemyDefinition definition;
            public float hp, attackTimer, aimTimer, warningTimer, pathTimer, chargeRemaining;
            public Vector2 aimDirection;
        }

        private sealed class Bullet
        {
            public GameObject body;
            public bool active;
            public Vector3 velocity;
            public float remaining;
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
        private int targetIndex = -1;
        private readonly int[] weaponAmmo = new int[ProgressionRun.MaxWeaponSlots];
        private readonly float[] weaponReload = new float[ProgressionRun.MaxWeaponSlots];
        private readonly float[] weaponFireTimer = new float[ProgressionRun.MaxWeaponSlots];
        private long nextPickupId;
        private bool manualAim, triggerHeld;
        private Vector3 aimDirection = Vector3.forward;
        private float viewYaw;
        private bool bandageUsed;
        private System.Random rng;
        private System.Random shopRng;
        private SupplyEvent supplyEvent;
        private GameObject supplyCrate;

        private WeaponDefinition CurrentWeaponDefinition => Run != null && Run.Weapons.Count > 0 ? WeaponAt(0) :
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
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                TogglePause();
            if (State != CombatState.Playing) return;
            Vector2 movement = Vector2.zero;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) movement.x -= 1;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) movement.x += 1;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) movement.y -= 1;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) movement.y += 1;
                if (keyboard.rKey.wasPressedThisFrame) RequestReload();
            }
            if (Gamepad.current != null) movement += Gamepad.current.leftStick.ReadValue();
            // Third-person controls: forward is wherever the camera is looking.
            if (manualAim) movement = RotatePlanar(movement, viewYaw);
            StepSimulation(Time.deltaTime, movement);
        }

        /// <summary>
        /// Third-person view input. The yaw orients WASD; the aim point is where the
        /// crosshair meets the combat plane. Supplying it switches to manual fire.
        /// </summary>
        public void SetViewInput(float yawDegrees, Vector3 aimPoint, bool trigger)
        {
            manualAim = true;
            viewYaw = yawDegrees;
            triggerHeld = trigger;
            Vector3 direction = player != null ? aimPoint - player.position : Vector3.zero;
            direction.y = 0;
            // A crosshair point almost under the soldier has no stable direction.
            if (direction.sqrMagnitude < 1f) direction = Quaternion.Euler(0, yawDegrees, 0) * Vector3.forward;
            aimDirection = direction.normalized;
        }

        /// <summary>Starts reloading every weapon that is not already full.</summary>
        public void RequestReload()
        {
            if (State != CombatState.Playing) return;
            for (int slot = 0; slot < WeaponCount; slot++)
                if (weaponReload[slot] <= 0 && weaponAmmo[slot] < MagazineSizeAt(slot))
                    weaponReload[slot] = WeaponAt(slot).ReloadSeconds * Stats.ReloadFactor;
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
            player.gameObject.SetActive(true);
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
            State = CombatState.Menu;
            player.gameObject.SetActive(true);
            player.position = new Vector3(0, 0.8f, 0);
        }

        public void TogglePause()
        {
            if (State == CombatState.Playing) State = CombatState.Paused;
            else if (State == CombatState.Paused) State = CombatState.Playing;
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
            var result = Shop.TryPurchase(slot);
            return result;
        }

        public bool SetOfferLocked(int slot, bool locked) =>
            State == CombatState.Shop && Shop != null && Shop.SetLocked(slot, locked);

        public bool RerollShop() => State == CombatState.Shop && Shop != null &&
            Shop.TryReroll(new ShopOfferGenerator().RollOffers(Run, Wave, shopRng, Shop));

        public bool SellWeapon(int slot, out int refund)
        {
            refund = 0;
            return State == CombatState.Shop && Run != null && Run.TrySellWeapon(slot, out refund);
        }

        public bool CombineWeapons(int first, int second) => State == CombatState.Shop &&
            Run != null && Run.TryCombineWeapons(first, second);

        public bool CombineFirstMatchingWeapons()
        {
            if (State != CombatState.Shop) return false;
            for (int first = 0; first < WeaponCount; first++)
                for (int second = first + 1; second < WeaponCount; second++)
                    if (Run.TryCombineWeapons(first, second)) return true;
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
            if (State != CombatState.Playing || rawDamage <= 0 || invulnerability > 0) return;
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
            while (deltaTime > 0 && State == CombatState.Playing)
            {
                float step = Mathf.Min(deltaTime, 1f / 30f);
                Simulate(step, movement);
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
            MovePlayer(dt, movement);
            UpdateNavigation();
            UpdateSpawn(dt);
            UpdateEnemies(dt);
            if (State != CombatState.Playing) return;
            UpdateBullets(dt);
            if (State != CombatState.Playing) return;
            UpdatePickups();
            FireWeapon();
            RunElapsed += dt;
            WaveRemaining = Mathf.Max(0, WaveRemaining - dt);
            UpdateSupply(dt, WaveRemaining <= 0);
            if (WaveRemaining <= 0) FinishWave();
        }

        private void ResetWave()
        {
            HideTransientObjects();
            AliveEnemies = 0;
            WaveRemaining = WaveDuration(Wave);
            spawnTimer = 1.1f;
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
        }

        private void FinishWave()
        {
            // Death has already been handled earlier in the same step.
            if (State != CombatState.Playing) return;
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

        private void MovePlayer(float dt, Vector2 movement)
        {
            if (movement.sqrMagnitude > 1) movement.Normalize();
            var displacement = new Vector3(movement.x, 0, movement.y) * (CombatRules.PlayerSpeed * Stats.SpeedFactor * dt);
            MoveWithCover(player, displacement, PlayerRadius);
            var p = player.position;
            player.position = new Vector3(Mathf.Clamp(p.x, -HalfWidth, HalfWidth), 0.8f, Mathf.Clamp(p.z, -HalfHeight, HalfHeight));
            playerRenderer.material.color = invulnerability > 0 && Mathf.FloorToInt(invulnerability * 20) % 2 == 0
                ? Color.white : (PlayerMaterial != null ? PlayerMaterial.color : new Color(.55f, .65f, .4f));
        }

        private void UpdateSpawn(float dt)
        {
            spawnTimer -= dt;
            if (spawnTimer > 0 || AliveEnemies >= CombatRules.EnemyCap) return;
            spawnTimer = Mathf.Max(0.58f, 1.35f - (Wave - 1) * 0.18f);
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
                slot.shooter = slot.definition.Role == EnemyRole.Shooter;
                slot.charger = slot.definition.Role == EnemyRole.Charger;
                return;
            }
        }

        private EnemyDefinition ChooseEnemyDefinition()
        {
            var catalog = ContentCatalog.P2;
            if (Wave >= 5 && rng.NextDouble() < 0.14)
                return catalog.GetEnemy(ContentIds.Elite);
            int total = 0;
            foreach (var definition in catalog.Enemies)
                if (IsEnemyRoleEligibleForWave(definition.Role, Wave)) total += definition.SpawnWeight;
            int choice = rng.Next(total);
            foreach (var definition in catalog.Enemies)
            {
                if (!IsEnemyRoleEligibleForWave(definition.Role, Wave)) continue;
                choice -= definition.SpawnWeight;
                if (choice < 0) return definition;
            }
            throw new InvalidOperationException("No eligible regular enemy for wave " + Wave);
        }

        public static bool IsEnemyRoleEligibleForWave(EnemyRole role, int wave) =>
            (role == EnemyRole.Infantry && wave >= 1) || (role == EnemyRole.Charger && wave >= 2) ||
            (role == EnemyRole.Shooter && wave >= 3);

        private void UpdateEnemies(float dt)
        {
            int activeShooters = 0;
            foreach (var enemy in enemies) if (enemy.active && enemy.shooter && enemy.aiming) activeShooters++;
            foreach (var enemy in enemies)
            {
                if (enemy.pending)
                {
                    enemy.warningTimer -= dt;
                    if (enemy.warningTimer <= 0 && AliveEnemies < CombatRules.EnemyCap)
                    {
                        enemy.pending = false;
                        enemy.warning.SetActive(false);
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
                        enemy.pathTimer = 0;
                        AliveEnemies++;
                    }
                }
                if (!enemy.active) continue;
                enemy.attackTimer = Mathf.Max(0, enemy.attackTimer - dt);
                Vector3 toPlayer = player.position - enemy.body.transform.position;
                toPlayer.y = 0;
                float distance = toPlayer.magnitude;
                if (enemy.shooter)
                {
                    if (enemy.aiming)
                    {
                        enemy.aimTimer -= dt;
                        DrawAimLine(enemy.body.transform.position, enemy.aimDirection, enemy.warning);
                        if (enemy.aimTimer <= 0)
                        {
                            enemy.aiming = false;
                            enemy.warning.SetActive(false);
                            FireEnemyBullet(enemy.body.transform, enemy.aimDirection);
                            enemy.attackTimer = 2.2f;
                        }
                    }
                    else if (distance < 10f && distance > 2.5f && HasLineOfSight(enemy.body.transform.position, player.position) && enemy.attackTimer <= 0 && activeShooters < 4)
                    {
                        enemy.aiming = true;
                        enemy.aimTimer = 0.8f;
                        // Combat is planar XZ; implicit Vector3 -> Vector2 would drop Z.
                        enemy.aimDirection = new Vector2(toPlayer.x, toPlayer.z).normalized;
                        enemy.warning.SetActive(true);
                        activeShooters++;
                    }
                    else if (distance > 6.5f || !HasLineOfSight(enemy.body.transform.position, player.position))
                        MoveEnemy(enemy, dt, 2.05f);
                }
                else if (enemy.charger)
                    UpdateCharger(enemy, dt, toPlayer, distance);
                else if (distance <= (enemy.elite ? 1.35f : 1.12f))
                {
                    if (enemy.attackTimer <= 0)
                    {
                        enemy.attackTimer = enemy.definition.AttackCooldownSeconds;
                        DamagePlayer(enemy.definition.AttackDamage, enemy.body.transform.position);
                        // A killing blow hides every enemy and clears its definition.
                        if (State != CombatState.Playing) return;
                    }
                }
                else MoveEnemy(enemy, dt, enemy.definition.MoveSpeed);
                if (State != CombatState.Playing) return;
            }
        }

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
                    enemy.charging = true;
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

        private void UpdateBullets(float dt)
        {
            foreach (var bullet in bullets)
            {
                if (!bullet.active) continue;
                Vector3 start = bullet.body.transform.position;
                Vector3 delta = bullet.velocity * dt;
                float length = delta.magnitude;
                if (RayHitsCover(start, delta.normalized, length, out _)) { Deactivate(bullet); continue; }
                var point = ClosestPointOnSegment(player.position, start, start + delta);
                if ((point - player.position).sqrMagnitude <= 0.47f * 0.47f)
                {
                    DamagePlayer(7f, start - bullet.velocity);
                    Deactivate(bullet);
                    if (State != CombatState.Playing) return;
                    continue;
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
                Run.TryCollectPickup(pickup.id, pickup.valueMinor);
                pickup.active = false;
                pickup.body.SetActive(false);
            }
        }

        private void FireWeapon()
        {
            if (manualAim)
            {
                FireManualWeapons();
                return;
            }
            for (int slot = 0; slot < WeaponCount; slot++)
            {
                if (weaponReload[slot] > 0 || weaponFireTimer[slot] > 0) continue;
                WeaponDefinition weapon = WeaponAt(slot);
                if (weaponAmmo[slot] <= 0) { weaponReload[slot] = weapon.ReloadSeconds * Stats.ReloadFactor; continue; }
                int target = FindTarget(weapon.Range * Stats.RangeFactor);
                if (target < 0) continue;
                var enemy = enemies[target];
                Vector3 start = player.position + Vector3.up * .08f;
                Vector3 end = enemy.body.transform.position + Vector3.up * .08f;
                Vector3 direction = end - start;
                direction.y = 0;
                if (RayHitsCover(start, direction.normalized, direction.magnitude, out _)) { targetIndex = -1; continue; }
                weaponAmmo[slot]--;
                weaponFireTimer[slot] = weapon.ShotCooldownSeconds / Mathf.Max(.1f, 1 + Stats.AttackSpeedBonus);
                tracerStart = start;
                tracerEnd = end;
                ShowTracer();
                WeaponFired?.Invoke(slot, start, end);
                ShotImpact?.Invoke(end, true);
                ApplyShotDamage(enemy, ShotDamage(slot, weapon, out bool critical), critical);
                if (weaponAmmo[slot] == 0) weaponReload[slot] = weapon.ReloadSeconds * Stats.ReloadFactor;
            }
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
            float longest = 0;
            for (int slot = 0; slot < WeaponCount; slot++) longest = Mathf.Max(longest, WeaponAt(slot).Range);
            AimOnTarget = WeaponCount > 0 && FindAimTarget(longest * Stats.RangeFactor * ManualRangeFactor) >= 0;
            if (!triggerHeld) return;
            for (int slot = 0; slot < WeaponCount; slot++)
            {
                if (weaponReload[slot] > 0 || weaponFireTimer[slot] > 0) continue;
                WeaponDefinition weapon = WeaponAt(slot);
                if (weaponAmmo[slot] <= 0) { weaponReload[slot] = weapon.ReloadSeconds * Stats.ReloadFactor; continue; }
                float range = weapon.Range * Stats.RangeFactor * ManualRangeFactor;
                int target = FindAimTarget(range);
                Vector3 start = player.position + Vector3.up * .08f;
                Vector3 end;
                if (target >= 0)
                {
                    // Keep the hit on the aim ray so the tracer lands where the player pointed.
                    Vector3 toEnemy = enemies[target].body.transform.position - player.position;
                    toEnemy.y = 0;
                    end = start + aimDirection * Vector3.Dot(toEnemy, aimDirection);
                }
                else
                {
                    float distance = RayHitsCover(start, aimDirection, range, out float hit) ? hit : range;
                    end = start + aimDirection * distance;
                }
                weaponAmmo[slot]--;
                weaponFireTimer[slot] = weapon.ShotCooldownSeconds / Mathf.Max(.1f, 1 + Stats.AttackSpeedBonus);
                tracerStart = start;
                tracerEnd = end;
                ShowTracer();
                WeaponFired?.Invoke(slot, start, end);
                ShotImpact?.Invoke(end, target >= 0);
                if (target >= 0) ApplyShotDamage(enemies[target], ShotDamage(slot, weapon, out bool critical), critical);
                if (weaponAmmo[slot] == 0) weaponReload[slot] = weapon.ReloadSeconds * Stats.ReloadFactor;
            }
        }

        /// <summary>First enemy whose body meets the aim ray before cover, within range.</summary>
        private int FindAimTarget(float range)
        {
            Vector3 origin = player.position;
            float reach = RayHitsCover(origin, aimDirection, range, out float coverDistance) ? coverDistance : range;
            int best = -1;
            float bestAlong = reach;
            for (int i = 0; i < enemies.Length; i++)
            {
                if (!enemies[i].active) continue;
                Vector3 offset = enemies[i].body.transform.position - origin;
                offset.y = 0;
                float along = Vector3.Dot(offset, aimDirection);
                if (along <= 0 || along >= bestAlong) continue;
                float radius = ManualAimRadius * (enemies[i].elite ? 1.3f : 1f);
                if ((offset - aimDirection * along).sqrMagnitude > radius * radius) continue;
                best = i;
                bestAlong = along;
            }
            targetIndex = best;
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
        private int MagazineSizeAt(int slot) => ProgressionRules.MagazineSize(WeaponAt(slot).MagazineSize, Stats);

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

        private void ApplyShotDamage(Enemy enemy, float damage, bool critical)
        {
            if (!enemy.active || enemy.definition == null || damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            float applied = Mathf.Min(damage, enemy.hp);
            enemy.hp -= damage;
            DamageDealt += applied;
            EnemyDamaged?.Invoke(enemy.body.transform, applied, critical, enemy.hp <= 0);
            if (enemy.hp > 0) return;
            enemy.active = false;
            enemy.aiming = false;
            enemy.body.SetActive(false);
            enemy.warning.SetActive(false);
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
                }
            }
            SupplyProgress = supplyEvent.RecoverySeconds <= 0 ? 0 :
                Mathf.Clamp01(supplyEvent.ProgressSeconds / supplyEvent.RecoverySeconds);
            if (supplyEvent.IsClaimable && supplyEvent.TryClaim(Health > 0, out var reward))
            {
                Run.AddCurrency(reward.CurrencyMinorUnits);
                Health = Mathf.Min(MaxHealth, Health + reward.Heal);
                SupplyVisible = false;
                SupplyProgress = 1;
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

        private void FireEnemyBullet(Transform actor, Vector2 direction)
        {
            foreach (var bullet in bullets)
            {
                if (bullet.active) continue;
                bullet.active = true;
                bullet.remaining = 3f;
                bullet.velocity = new Vector3(direction.x, 0, direction.y) * 5.2f;
                bullet.body.transform.position = actor.position;
                bullet.body.SetActive(true);
                EnemyFired?.Invoke(actor, new Vector3(direction.x, 0, direction.y));
                return;
            }
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
                    new Vector3(cover.center.x, .55f, cover.center.y),
                    new Vector3(cover.half.x * 2, 1.1f, cover.half.y * 2), CoverMaterial);
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
                enemies[i] = new Enemy { body = body, warning = warning, chargerMarker = marker };
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
            foreach (var enemy in enemies)
            {
                enemy.active = enemy.pending = enemy.shooter = enemy.elite = enemy.charger =
                    enemy.aiming = enemy.charging = enemy.chargeHit = false;
                enemy.definition = null;
                enemy.hp = enemy.attackTimer = enemy.aimTimer = enemy.warningTimer =
                    enemy.pathTimer = enemy.chargeRemaining = 0;
                enemy.aimDirection = Vector2.zero;
                enemy.body.SetActive(false);
                enemy.warning.SetActive(false);
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
            // A segment/axis-aligned box sweep in XZ avoids layer dependence and includes the cover edge.
            foreach (var cover in covers)
            {
                float near = 0, far = distance;
                if (!ClipAxis(origin.x, direction.x, cover.center.x - cover.half.x, cover.center.x + cover.half.x, ref near, ref far)) continue;
                if (!ClipAxis(origin.z, direction.z, cover.center.y - cover.half.y, cover.center.y + cover.half.y, ref near, ref far)) continue;
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
}
