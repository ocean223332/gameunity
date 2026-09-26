using System;
using System.Collections.Generic;

namespace TienTuyen.Progression
{
    /// <summary>Signed additive bonuses; percentages are fractions (0.1 means 10%).</summary>
    public struct StatModifier
    {
        public float MaxHealth;
        public float Speed;
        public float Armor;
        public float Damage;
        public float AttackSpeed;
        public float CriticalChance;
        public float Regen;
        public float PickupRadius;
        public float ReloadDuration;
        public float Range;
        public float Magazine;
        public float Income;
        public float Experience;
    }

    /// <summary>Safe, final values for one character build.</summary>
    public struct ProgressionStats
    {
        public float MaxHealth;
        public float SpeedFactor;
        public float Armor;
        public float DamageFactor;
        public float AttackSpeedBonus;
        public float CriticalChance;
        public float Regen;
        public float PickupRadius;
        public float ReloadFactor;
        public float RangeFactor;
        public float MagazineFactor;
        public float IncomeFactor;
        public float ExperienceFactor;
    }

    public enum UpgradeKind { Health, Damage, AttackSpeed, Armor, Speed, Regen }

    /// <summary>Pure progression prices, upgrade effects, and bounded stat aggregation.</summary>
    public static class ProgressionRules
    {
        public static int ExperienceThreshold(int level)
        {
            if (level < 1 || level > 1000000) throw new ArgumentOutOfRangeException(nameof(level));
            return checked(8 + 4 * (level - 1));
        }

        public static int OfferPrice(int basePrice, int tierFactor, int wave)
        {
            if (basePrice < 0 || tierFactor < 1 || wave < 1) throw new ArgumentOutOfRangeException();
            double price = Math.Ceiling((double)basePrice * tierFactor * (1 + 0.06 * (wave - 1)));
            if (price > int.MaxValue) throw new OverflowException();
            return (int)price;
        }

        public static int RerollPrice(int wave, int rerollsInShop)
        {
            if (wave < 1 || rerollsInShop < 0) throw new ArgumentOutOfRangeException();
            return checked(4 + 2 * rerollsInShop + (wave - 1) / 3);
        }

        public static StatModifier Upgrade(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.Health: return new StatModifier { MaxHealth = 10f };
                case UpgradeKind.Damage: return new StatModifier { Damage = 0.05f };
                case UpgradeKind.AttackSpeed: return new StatModifier { AttackSpeed = 0.05f };
                case UpgradeKind.Armor: return new StatModifier { Armor = 5f };
                case UpgradeKind.Speed: return new StatModifier { Speed = 0.05f };
                case UpgradeKind.Regen: return new StatModifier { Regen = 0.2f };
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>Aggregates bonuses before caps; non-finite inputs have no effect.</summary>
        public static ProgressionStats Aggregate(IEnumerable<StatModifier> modifiers)
        {
            if (modifiers == null) throw new ArgumentNullException(nameof(modifiers));
            double hp = 0, speed = 0, armor = 0, damage = 0, attack = 0, crit = 0;
            double regen = 0, pickup = 0, reload = 0, range = 0, magazine = 0, income = 0, xp = 0;
            foreach (StatModifier m in modifiers)
            {
                hp = AddFinite(hp, m.MaxHealth);
                speed = AddFinite(speed, m.Speed);
                armor = AddFinite(armor, m.Armor);
                damage = AddFinite(damage, m.Damage);
                attack = AddFinite(attack, m.AttackSpeed);
                crit = AddFinite(crit, m.CriticalChance);
                regen = AddFinite(regen, m.Regen);
                pickup = AddFinite(pickup, m.PickupRadius);
                reload = AddFinite(reload, m.ReloadDuration);
                range = AddFinite(range, m.Range);
                magazine = AddFinite(magazine, m.Magazine);
                income = AddFinite(income, m.Income);
                xp = AddFinite(xp, m.Experience);
            }

            return new ProgressionStats
            {
                MaxHealth = Cap(100 + hp, 1, 100000),
                SpeedFactor = Cap(1 + speed, 0.6, 1.8),
                Armor = Cap(armor, 0, 100),
                DamageFactor = Cap(1 + damage, 0, 100),
                AttackSpeedBonus = Cap(attack, -0.5, 1),
                CriticalChance = Cap(0.05 + crit, 0, 0.6),
                Regen = Cap(regen, 0, 1000),
                PickupRadius = Cap(1.5 + pickup, 0, 4),
                ReloadFactor = Cap(1 + reload, 0.5, 100),
                RangeFactor = Cap(1 + range, 0.7, 1.5),
                MagazineFactor = Cap(1 + magazine, 0, 100),
                IncomeFactor = Cap(1 + income, 0, 100),
                ExperienceFactor = Cap(1 + xp, 0, 100)
            };
        }

        public static int MagazineSize(int baseSize, ProgressionStats stats)
        {
            if (baseSize < 1) throw new ArgumentOutOfRangeException(nameof(baseSize));
            if (float.IsNaN(stats.MagazineFactor) || float.IsInfinity(stats.MagazineFactor) ||
                stats.MagazineFactor <= 0) return 1;
            return (int)Math.Max(1, Math.Min(int.MaxValue, Math.Floor((double)baseSize * stats.MagazineFactor)));
        }

        private static double AddFinite(double current, float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? current : current + value;
        }

        private static float Cap(double value, double min, double max)
        {
            return (float)Math.Max(min, Math.Min(max, value));
        }
    }
}
