using System;
using System.Collections.Generic;

namespace TienTuyen.Content
{
    public static class ContentIds
    {
        public const string Rifle = "weapon.rifle";
        public const string Smg = "weapon.smg";
        public const string Shotgun = "weapon.shotgun";
        public const string Infantry = "enemy.infantry";
        public const string Charger = "enemy.charger";
        public const string Shooter = "enemy.shooter";
        public const string Elite = "enemy.elite";
    }

    public enum EnemyRole { Infantry, Shooter, Elite, Charger }

    // Data only: a run owns its own ammo, cooldown, health, and target state.
    public sealed class WeaponDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public float DamagePerHit { get; }
        public int PelletsPerShot { get; }
        public float ShotCooldownSeconds { get; }
        public int MagazineSize { get; }
        public float ReloadSeconds { get; }
        public float Range { get; }
        public int BasePrice { get; }

        public WeaponDefinition(string id, string displayName, float damagePerHit, int pelletsPerShot,
            float shotCooldownSeconds, int magazineSize, float reloadSeconds, float range, int basePrice)
        {
            Id = id;
            DisplayName = displayName;
            DamagePerHit = damagePerHit;
            PelletsPerShot = pelletsPerShot;
            ShotCooldownSeconds = shotCooldownSeconds;
            MagazineSize = magazineSize;
            ReloadSeconds = reloadSeconds;
            Range = range;
            BasePrice = basePrice;
        }
    }

    public sealed class EnemyDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public EnemyRole Role { get; }
        public float MaxHealth { get; }
        public float MoveSpeed { get; }
        public float AttackDamage { get; }
        public float AttackCooldownSeconds { get; }
        public float AttackRange { get; }
        public float TelegraphSeconds { get; }
        // Wave composition determines whether this role is eligible; weight only ranks eligible roles.
        public int SpawnWeight { get; }
        public int ExperienceReward { get; }
        public int SupplyReward { get; }

        public EnemyDefinition(string id, string displayName, EnemyRole role, float maxHealth,
            float moveSpeed, float attackDamage, float attackCooldownSeconds, float attackRange,
            float telegraphSeconds, int spawnWeight, int experienceReward, int supplyReward)
        {
            Id = id;
            DisplayName = displayName;
            Role = role;
            MaxHealth = maxHealth;
            MoveSpeed = moveSpeed;
            AttackDamage = attackDamage;
            AttackCooldownSeconds = attackCooldownSeconds;
            AttackRange = attackRange;
            TelegraphSeconds = telegraphSeconds;
            SpawnWeight = spawnWeight;
            ExperienceReward = experienceReward;
            SupplyReward = supplyReward;
        }
    }

    public sealed class ContentCatalog
    {
        private readonly Dictionary<string, WeaponDefinition> weaponsById =
            new Dictionary<string, WeaponDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, EnemyDefinition> enemiesById =
            new Dictionary<string, EnemyDefinition>(StringComparer.Ordinal);

        public IReadOnlyList<WeaponDefinition> Weapons { get; }
        public IReadOnlyList<EnemyDefinition> Enemies { get; }

        public static ContentCatalog P2 { get; } = new ContentCatalog(
            new[]
            {
                new WeaponDefinition(ContentIds.Rifle, "Súng trường", 12f, 1, 0.35f, 12, 1.5f, 10f, 20),
                new WeaponDefinition(ContentIds.Smg, "Tiểu liên", 5f, 1, 0.12f, 24, 1.4f, 6.5f, 18),
                new WeaponDefinition(ContentIds.Shotgun, "Súng tản đạn", 4f, 6, 0.85f, 5, 2f, 4.5f, 22)
            },
            new[]
            {
                new EnemyDefinition(ContentIds.Infantry, "Lính áp sát", EnemyRole.Infantry,
                    35f, 2.6f, 8f, 1.4f, 1.2f, 0.25f, 8, 1, 2),
                new EnemyDefinition(ContentIds.Charger, "Lính xung kích", EnemyRole.Charger,
                    20f, 3.7f, 11f, 2.6f, 4.5f, 0.7f, 3, 1, 2),
                new EnemyDefinition(ContentIds.Shooter, "Xạ thủ", EnemyRole.Shooter,
                    24f, 2.1f, 8f, 2.4f, 7f, 0.8f, 3, 1, 2),
                new EnemyDefinition(ContentIds.Elite, "Lính nặng tinh nhuệ", EnemyRole.Elite,
                    250f, 1.7f, 18f, 2.8f, 2.4f, 0.9f, 1, 10, 20)
            });

        public ContentCatalog(IEnumerable<WeaponDefinition> weapons, IEnumerable<EnemyDefinition> enemies)
        {
            if (weapons == null) throw new ArgumentNullException(nameof(weapons));
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));

            var weaponList = new List<WeaponDefinition>();
            var enemyList = new List<EnemyDefinition>();
            var allIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var weapon in weapons)
            {
                ValidateWeapon(weapon);
                if (!allIds.Add(weapon.Id)) throw new ArgumentException("Duplicate content ID: " + weapon.Id);
                weaponsById.Add(weapon.Id, weapon);
                weaponList.Add(weapon);
            }
            foreach (var enemy in enemies)
            {
                ValidateEnemy(enemy);
                if (!allIds.Add(enemy.Id)) throw new ArgumentException("Duplicate content ID: " + enemy.Id);
                enemiesById.Add(enemy.Id, enemy);
                enemyList.Add(enemy);
            }
            Weapons = weaponList.AsReadOnly();
            Enemies = enemyList.AsReadOnly();
        }

        public bool TryGetWeapon(string id, out WeaponDefinition weapon) =>
            weaponsById.TryGetValue(id ?? string.Empty, out weapon);

        public bool TryGetEnemy(string id, out EnemyDefinition enemy) =>
            enemiesById.TryGetValue(id ?? string.Empty, out enemy);

        public WeaponDefinition GetWeapon(string id)
        {
            if (TryGetWeapon(id, out var weapon)) return weapon;
            throw new KeyNotFoundException("Unknown weapon ID: " + (id ?? "<null>"));
        }

        public EnemyDefinition GetEnemy(string id)
        {
            if (TryGetEnemy(id, out var enemy)) return enemy;
            throw new KeyNotFoundException("Unknown enemy ID: " + (id ?? "<null>"));
        }

        private static void ValidateWeapon(WeaponDefinition weapon)
        {
            if (weapon == null) throw new ArgumentException("Weapon definition cannot be null.");
            ValidateIdentity(weapon.Id, weapon.DisplayName);
            Positive(weapon.DamagePerHit, "damage", weapon.Id, 1000);
            Positive(weapon.ShotCooldownSeconds, "cooldown", weapon.Id, 60);
            Positive(weapon.ReloadSeconds, "reload", weapon.Id, 60);
            Positive(weapon.Range, "range", weapon.Id, 100);
            if (weapon.PelletsPerShot < 1 || weapon.PelletsPerShot > 32)
                throw Invalid(weapon.Id, "pellet count must be 1..32");
            if (weapon.MagazineSize < 1 || weapon.MagazineSize > 500)
                throw Invalid(weapon.Id, "magazine size must be 1..500");
            if (weapon.BasePrice < 1 || weapon.BasePrice > 1000000)
                throw Invalid(weapon.Id, "price must be positive");
        }

        private static void ValidateEnemy(EnemyDefinition enemy)
        {
            if (enemy == null) throw new ArgumentException("Enemy definition cannot be null.");
            ValidateIdentity(enemy.Id, enemy.DisplayName);
            if (!Enum.IsDefined(typeof(EnemyRole), enemy.Role)) throw Invalid(enemy.Id, "role is invalid");
            Positive(enemy.MaxHealth, "health", enemy.Id, 100000);
            Positive(enemy.MoveSpeed, "move speed", enemy.Id, 100);
            Positive(enemy.AttackDamage, "attack damage", enemy.Id, 10000);
            Positive(enemy.AttackCooldownSeconds, "attack cooldown", enemy.Id, 60);
            Positive(enemy.AttackRange, "attack range", enemy.Id, 100);
            Positive(enemy.TelegraphSeconds, "telegraph", enemy.Id, 10);
            if (enemy.SpawnWeight < 1 || enemy.SpawnWeight > 1000000) throw Invalid(enemy.Id, "spawn weight must be 1..1000000");
            if (enemy.ExperienceReward < 0 || enemy.ExperienceReward > 1000000000 || enemy.SupplyReward < 0 || enemy.SupplyReward > 1000000000)
                throw Invalid(enemy.Id, "rewards must be within 0..1000000000");
        }

        private static void ValidateIdentity(string id, string displayName)
        {
            if (string.IsNullOrWhiteSpace(id) || id != id.Trim())
                throw new ArgumentException("Content ID must be non-empty and have no outer whitespace.");
            if (string.IsNullOrWhiteSpace(displayName)) throw Invalid(id, "display name is empty");
        }

        private static void Positive(float value, string field, string id, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0 || value > max)
                throw Invalid(id, field + " must be finite and within a safe positive range");
        }

        private static ArgumentException Invalid(string id, string reason) =>
            new ArgumentException("Invalid content " + id + ": " + reason);
    }
}
