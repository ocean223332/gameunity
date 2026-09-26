using System;
using System.Collections.Generic;

namespace TienTuyen.Progression
{
    public sealed class PassiveDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int BasePrice { get; }
        public StatModifier Modifier { get; }
        public int StackCap { get; }

        public PassiveDefinition(string id, string displayName, int basePrice, StatModifier modifier, int stackCap = 2)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(displayName) ||
                basePrice < 1 || stackCap < 1) throw new ArgumentException("Invalid passive definition");
            Id = id;
            DisplayName = displayName;
            BasePrice = basePrice;
            Modifier = modifier;
            StackCap = stackCap;
        }

        public ShopOffer OfferForWave(int wave) => new ShopOffer(Id, OfferKind.Passive, 1,
            ProgressionRules.OfferPrice(BasePrice, 1, wave), StackCap, Modifier);
    }

    /// <summary>The six passive items in the P2 vertical slice, with stable SPEC IDs.</summary>
    public sealed class PassiveCatalog
    {
        public const string MedKit = "I01";
        public const string Bandage = "I02";
        public const string Armor = "I03";
        public const string Boots = "I04";
        public const string Sling = "I05";
        public const string CleaningKit = "I06";

        private readonly Dictionary<string, PassiveDefinition> byId =
            new Dictionary<string, PassiveDefinition>(StringComparer.Ordinal);
        public IReadOnlyList<PassiveDefinition> Items { get; }

        public static PassiveCatalog P2 { get; } = new PassiveCatalog(new[]
        {
            new PassiveDefinition(MedKit, "Túi cứu thương", 20, new StatModifier { MaxHealth = 15f }),
            new PassiveDefinition(Bandage, "Băng cá nhân", 28, new StatModifier { Regen = 0.3f }),
            new PassiveDefinition(Armor, "Áo bảo hộ", 20, new StatModifier { Armor = 10f, Speed = -0.03f }),
            new PassiveDefinition(Boots, "Giày hành quân", 20, new StatModifier { Speed = 0.08f }),
            new PassiveDefinition(Sling, "Dây mang súng", 20, new StatModifier { ReloadDuration = -0.1f }),
            new PassiveDefinition(CleaningKit, "Bộ vệ sinh súng", 20, new StatModifier { Damage = 0.08f })
        });

        public PassiveCatalog(IEnumerable<PassiveDefinition> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var list = new List<PassiveDefinition>();
            foreach (PassiveDefinition item in items)
            {
                if (item == null || byId.ContainsKey(item.Id))
                    throw new ArgumentException("Null or duplicate passive definition");
                byId.Add(item.Id, item);
                list.Add(item);
            }
            Items = list.AsReadOnly();
        }

        public bool TryGet(string id, out PassiveDefinition item) => byId.TryGetValue(id ?? string.Empty, out item);
    }
}
