using System;
using System.Collections.Generic;
using TienTuyen.Content;

namespace TienTuyen.Progression
{
    /// <summary>Generates the four P2 shop offers; the session owns purchases and locks.</summary>
    public sealed class ShopOfferGenerator
    {
        private readonly ContentCatalog weapons;
        private readonly PassiveCatalog passives;

        public ShopOfferGenerator(ContentCatalog weapons = null, PassiveCatalog passives = null)
        {
            this.weapons = weapons ?? ContentCatalog.P2;
            this.passives = passives ?? PassiveCatalog.P2;
        }

        public ShopOffer[] RollOffers(ProgressionRun run, int wave, Random random, ShopSession current = null)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (wave < 1) throw new ArgumentOutOfRangeException(nameof(wave));
            if (current != null && current.Run != run) throw new ArgumentException("Shop belongs to another run");

            var row = new ShopOffer[ShopSession.OfferSlots];
            var free = new List<int>();
            bool hasWeapon = false, hasPassive = false;
            for (int i = 0; i < row.Length; i++)
            {
                if (current != null && current.IsLocked(i))
                {
                    row[i] = current.OfferAt(i);
                    hasWeapon |= row[i].Kind == OfferKind.Weapon;
                    hasPassive |= row[i].Kind == OfferKind.Passive;
                }
                else free.Add(i);
            }

            var validPassives = new List<PassiveDefinition>();
            foreach (PassiveDefinition passive in passives.Items)
                if (run.PassiveCount(passive.Id) < passive.StackCap) validPassives.Add(passive);

            // Fill absent categories before random slots, prioritizing a weapon if only one slot remains.
            if (!hasWeapon && free.Count > 0)
            {
                row[free[0]] = RollWeapon(wave, random);
                free.RemoveAt(0);
            }
            if (!hasPassive && free.Count > 0 && validPassives.Count > 0)
            {
                row[free[0]] = RollPassive(validPassives, wave, random);
                free.RemoveAt(0);
            }
            foreach (int slot in free)
                row[slot] = validPassives.Count > 0 && random.Next(2) == 0
                    ? RollPassive(validPassives, wave, random) : RollWeapon(wave, random);
            return row;
        }

        private ShopOffer RollWeapon(int wave, Random random)
        {
            if (weapons.Weapons.Count == 0) throw new InvalidOperationException("No weapons available for shop");
            WeaponDefinition weapon = weapons.Weapons[random.Next(weapons.Weapons.Count)];
            int roll = random.Next(100);
            int tier = wave < 5 ? 1 : wave < 9 ? (roll < 75 ? 1 : 2) :
                (roll < 55 ? 1 : roll < 90 ? 2 : 3);
            int tierFactor = tier == 1 ? 1 : tier == 2 ? 2 : 4;
            return new ShopOffer(weapon.Id, OfferKind.Weapon, tier,
                ProgressionRules.OfferPrice(weapon.BasePrice, tierFactor, wave));
        }

        private static ShopOffer RollPassive(List<PassiveDefinition> valid, int wave, Random random) =>
            valid[random.Next(valid.Count)].OfferForWave(wave);
    }
}
