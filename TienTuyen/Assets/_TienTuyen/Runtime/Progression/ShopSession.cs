using System;
using System.Collections.Generic;

namespace TienTuyen.Progression
{
    public enum OfferKind { Weapon, Passive }
    public enum PurchaseResult { Bought, InvalidSlot, Empty, InsufficientCurrency, WeaponSlotsFull, StackCapReached }

    /// <summary>An immutable shop quote in whole supply units.</summary>
    public sealed class ShopOffer
    {
        public string Id { get; }
        public OfferKind Kind { get; }
        public int Tier { get; }
        public int Price { get; }
        public int StackCap { get; }
        public StatModifier Modifier { get; }

        public ShopOffer(string id, OfferKind kind, int tier, int price, int stackCap = 2, StatModifier modifier = default(StatModifier))
        {
            if (string.IsNullOrEmpty(id) || price < 0 || tier < 1 || tier > 3 || stackCap < 1 || !Enum.IsDefined(typeof(OfferKind), kind))
                throw new ArgumentException("Invalid shop offer");
            Id = id;
            Kind = kind;
            Tier = tier;
            Price = price;
            StackCap = stackCap;
            Modifier = modifier;
        }
    }

    /// <summary>Four offers and locks for a single shop visit; supply generation stays with the caller.</summary>
    public sealed class ShopSession
    {
        public const int OfferSlots = 4;
        private readonly ShopOffer[] offers = new ShopOffer[OfferSlots];
        private readonly bool[] locked = new bool[OfferSlots];
        private readonly ProgressionRun run;
        private readonly int wave;
        public ProgressionRun Run => run;
        public int Wave => wave;
        public int Rerolls { get; private set; }
        public int NextRerollPrice => ProgressionRules.RerollPrice(wave, Rerolls);

        public ShopSession(ProgressionRun run, int wave, IReadOnlyList<ShopOffer> initialOffers)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            if (wave < 1) throw new ArgumentOutOfRangeException(nameof(wave));
            if (initialOffers == null || initialOffers.Count != OfferSlots) throw new ArgumentException("Expected four offers");
            this.wave = wave;
            for (int i = 0; i < OfferSlots; i++) offers[i] = initialOffers[i];
            RemoveCappedPassives();
        }

        public ShopOffer OfferAt(int slot) => InRange(slot) ? offers[slot] : null;
        public bool IsLocked(int slot) => InRange(slot) && locked[slot];

        public bool SetLocked(int slot, bool value)
        {
            if (!InRange(slot) || offers[slot] == null) return false;
            locked[slot] = value;
            return true;
        }

        /// <summary>Starts the next shop with valid locked offers retained and reroll cost reset.</summary>
        public ShopSession CreateNextShop(int nextWave, IReadOnlyList<ShopOffer> refill)
        {
            if (refill == null || refill.Count != OfferSlots) throw new ArgumentException("Expected four offers");
            if (nextWave <= wave) throw new ArgumentOutOfRangeException(nameof(nextWave));
            var row = new ShopOffer[OfferSlots];
            for (int i = 0; i < OfferSlots; i++) row[i] = locked[i] ? offers[i] : refill[i];
            var next = new ShopSession(run, nextWave, row);
            for (int i = 0; i < OfferSlots; i++)
                if (locked[i] && next.offers[i] != null) next.locked[i] = true;
            return next;
        }

        /// <summary>Checks funds, capacity, and offer validity before changing any run state.</summary>
        public PurchaseResult TryPurchase(int slot)
        {
            if (!InRange(slot)) return PurchaseResult.InvalidSlot;
            ShopOffer offer = offers[slot];
            if (offer == null) return PurchaseResult.Empty;
            if (offer.Kind == OfferKind.Weapon && run.Weapons.Count >= ProgressionRun.MaxWeaponSlots)
                return PurchaseResult.WeaponSlotsFull;
            if (offer.Kind == OfferKind.Passive && run.PassiveCount(offer.Id) >= offer.StackCap)
                return PurchaseResult.StackCapReached;
            if (run.CurrencyMinor < (long)offer.Price * 100) return PurchaseResult.InsufficientCurrency;
            run.Purchase(offer);
            offers[slot] = null;
            locked[slot] = false;
            RemoveCappedPassives();
            return PurchaseResult.Bought;
        }

        /// <summary>Uses a complete prospective row; invalid or unaffordable rolls leave state untouched.</summary>
        public bool TryReroll(IReadOnlyList<ShopOffer> replacements)
        {
            if (replacements == null || replacements.Count != OfferSlots) return false;
            bool hasFreeSlot = false;
            for (int i = 0; i < OfferSlots; i++)
            {
                if (locked[i])
                {
                    if (!ReferenceEquals(replacements[i], offers[i])) return false;
                }
                else
                {
                    hasFreeSlot = true;
                    if (replacements[i] != null && replacements[i].Kind == OfferKind.Passive &&
                        run.PassiveCount(replacements[i].Id) >= replacements[i].StackCap) return false;
                }
            }
            if (!hasFreeSlot || Rerolls >= (int.MaxValue - 4 - (wave - 1) / 3) / 2 ||
                !run.TrySpend(NextRerollPrice)) return false;
            for (int i = 0; i < OfferSlots; i++) if (!locked[i]) offers[i] = replacements[i];
            Rerolls++;
            return true;
        }

        private void RemoveCappedPassives()
        {
            for (int i = 0; i < OfferSlots; i++)
            {
                ShopOffer offer = offers[i];
                if (offer != null && offer.Kind == OfferKind.Passive && run.PassiveCount(offer.Id) >= offer.StackCap)
                {
                    offers[i] = null;
                    locked[i] = false;
                }
            }
        }

        private static bool InRange(int slot) => slot >= 0 && slot < OfferSlots;
    }
}
