using System;
using System.Collections.Generic;

namespace TienTuyen.Progression
{
    /// <summary>One weapon slot, including its actual paid price for later resale.</summary>
    public sealed class OwnedWeapon
    {
        public string Id { get; }
        public int Tier { get; }
        public int PaidPrice { get; }

        public OwnedWeapon(string id, int tier, int paidPrice)
        {
            if (string.IsNullOrEmpty(id) || tier < 1 || paidPrice < 0) throw new ArgumentException("Invalid weapon");
            Id = id;
            Tier = tier;
            PaidPrice = paidPrice;
        }
    }

    /// <summary>Run-owned currency, weapons, passives, XP, and queued upgrade choices.</summary>
    public sealed class ProgressionRun
    {
        public const int MaxWeaponSlots = 4;
        private readonly List<OwnedWeapon> weapons = new List<OwnedWeapon>();
        private readonly Dictionary<string, int> passiveCounts = new Dictionary<string, int>();
        private readonly List<StatModifier> modifiers = new List<StatModifier>();
        private readonly HashSet<long> rewardedEnemies = new HashSet<long>();
        private readonly HashSet<long> collectedPickups = new HashSet<long>();
        private readonly HashSet<int> resolvedWaves = new HashSet<int>();
        private UpgradeKind[] currentChoices;
        private long experience;

        public long CurrencyMinor { get; private set; }
        public int Level { get; private set; } = 1;
        public int PendingUpgrades { get; private set; }
        public long Experience => experience;
        public IReadOnlyList<OwnedWeapon> Weapons => weapons.AsReadOnly();
        public IReadOnlyList<UpgradeKind> CurrentChoices => currentChoices == null
            ? (IReadOnlyList<UpgradeKind>)Array.Empty<UpgradeKind>() : Array.AsReadOnly(currentChoices);
        public ProgressionStats Stats => ProgressionRules.Aggregate(modifiers);

        public ProgressionStats PreviewStats(StatModifier extra)
        {
            var preview = new List<StatModifier>(modifiers) { extra };
            return ProgressionRules.Aggregate(preview);
        }

        public ProgressionRun(string starterWeaponId, StatModifier characterTrait = default(StatModifier))
        {
            weapons.Add(new OwnedWeapon(starterWeaponId, 1, 0));
            modifiers.Add(characterTrait);
        }

        public void AddCurrency(long minor)
        {
            if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor));
            CurrencyMinor = checked(CurrencyMinor + minor);
        }

        /// <summary>Credits kill XP once and returns a pickup value, in 1/100 supply units.</summary>
        public bool TryRewardEnemy(long eventId, int experienceReward, int pickupSupply, out long pickupMinor)
        {
            pickupMinor = 0;
            if (experienceReward < 0 || pickupSupply < 0) throw new ArgumentOutOfRangeException();
            if (rewardedEnemies.Contains(eventId)) return false;
            double income = (double)pickupSupply * 100 * Stats.IncomeFactor;
            if (income > long.MaxValue) throw new OverflowException();
            long value = (long)Math.Floor(income);
            // Preflight the only potentially overflowing XP mutation before recording this event.
            checked { _ = experience + (long)experienceReward; }
            AddExperience(experienceReward);
            rewardedEnemies.Add(eventId);
            pickupMinor = value;
            return true;
        }

        /// <summary>Collects a particular pickup once; its value already includes income bonuses.</summary>
        public bool TryCollectPickup(long pickupId, long pickupMinor)
        {
            if (pickupMinor < 0) throw new ArgumentOutOfRangeException(nameof(pickupMinor));
            if (collectedPickups.Contains(pickupId)) return false;
            long next = checked(CurrencyMinor + pickupMinor);
            CurrencyMinor = next;
            collectedPickups.Add(pickupId);
            return true;
        }

        /// <summary>Survival award plus half of remaining pickup value, rounded down once to whole supply.</summary>
        public bool TryResolveWave(int wave, long remainingPickupMinor, out long awardedMinor)
        {
            awardedMinor = 0;
            if (wave < 1) throw new ArgumentOutOfRangeException(nameof(wave));
            if (remainingPickupMinor < 0) throw new ArgumentOutOfRangeException(nameof(remainingPickupMinor));
            if (resolvedWaves.Contains(wave)) return false;
            long groundAward = (remainingPickupMinor / 200) * 100;
            double survival = (10d + 2d * wave) * 100 * Stats.IncomeFactor;
            if (survival > long.MaxValue) throw new OverflowException();
            long survivalAward = (long)Math.Floor(survival);
            long total = checked(groundAward + survivalAward);
            long next = checked(CurrencyMinor + total);
            CurrencyMinor = next;
            resolvedWaves.Add(wave);
            awardedMinor = total;
            return true;
        }

        public int PassiveCount(string id)
        {
            return id != null && passiveCounts.TryGetValue(id, out int count) ? count : 0;
        }

        public void AddExperience(long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            experience = checked(experience + amount);
            while (Level < 1000000 && experience >= ProgressionRules.ExperienceThreshold(Level))
            {
                experience -= ProgressionRules.ExperienceThreshold(Level);
                Level++;
                PendingUpgrades++;
            }
        }

        /// <summary>Opens one of the pending level-up selections with three distinct options.</summary>
        public bool OpenNextUpgrade(Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (PendingUpgrades == 0 || currentChoices != null) return false;
            var pool = new List<UpgradeKind>
            {
                UpgradeKind.Health, UpgradeKind.Damage, UpgradeKind.AttackSpeed,
                UpgradeKind.Armor, UpgradeKind.Speed, UpgradeKind.Regen
            };
            currentChoices = new UpgradeKind[3];
            for (int i = 0; i < 3; i++)
            {
                int index = random.Next(pool.Count);
                currentChoices[i] = pool[index];
                pool.RemoveAt(index);
            }
            return true;
        }

        /// <summary>Consumes exactly one pending level-up only for an offered choice.</summary>
        public bool TryChooseUpgrade(UpgradeKind choice)
        {
            if (currentChoices == null) return false;
            bool offered = false;
            foreach (UpgradeKind option in currentChoices) offered |= option == choice;
            if (!offered) return false;
            modifiers.Add(ProgressionRules.Upgrade(choice));
            currentChoices = null;
            PendingUpgrades--;
            return true;
        }

        public bool TrySellWeapon(int slot, out int refund)
        {
            refund = 0;
            if (slot < 0 || slot >= weapons.Count || weapons.Count <= 1) return false;
            refund = weapons[slot].PaidPrice / 2;
            CurrencyMinor = checked(CurrencyMinor + (long)refund * 100);
            weapons.RemoveAt(slot);
            return true;
        }

        /// <summary>Combines two equal weapons without creating currency or paid value.</summary>
        public bool TryCombineWeapons(int first, int second)
        {
            if (first < 0 || second < 0 || first >= weapons.Count || second >= weapons.Count || first == second) return false;
            OwnedWeapon a = weapons[first], b = weapons[second];
            if (a.Id != b.Id || a.Tier != b.Tier || a.Tier >= 3) return false;
            int paid = checked(a.PaidPrice + b.PaidPrice);
            weapons[Math.Min(first, second)] = new OwnedWeapon(a.Id, a.Tier + 1, paid);
            weapons.RemoveAt(Math.Max(first, second));
            return true;
        }

        internal void Purchase(ShopOffer offer)
        {
            // Caller has checked every precondition; mutations start only here.
            CurrencyMinor -= (long)offer.Price * 100;
            if (offer.Kind == OfferKind.Weapon)
                weapons.Add(new OwnedWeapon(offer.Id, offer.Tier, offer.Price));
            else
            {
                passiveCounts[offer.Id] = PassiveCount(offer.Id) + 1;
                modifiers.Add(offer.Modifier);
            }
        }

        public bool TrySpend(int price)
        {
            if (price < 0 || CurrencyMinor < (long)price * 100) return false;
            CurrencyMinor -= (long)price * 100;
            return true;
        }
    }
}
