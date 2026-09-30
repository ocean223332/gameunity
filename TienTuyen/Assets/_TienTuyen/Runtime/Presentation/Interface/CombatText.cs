using TienTuyen.Combat;
using TienTuyen.Content;
using TienTuyen.Progression;

namespace TienTuyen.Presentation.Interface
{
    /// <summary>Vietnamese read-outs for shop offers, upgrades and purchase results.</summary>
    public static class CombatText
    {
        public static string WeaponName(WeaponKind kind) =>
            kind == WeaponKind.Rifle ? "súng trường" : kind == WeaponKind.Smg ? "tiểu liên" : "súng tản đạn";

        public static string OfferName(ShopOffer offer)
        {
            if (offer.Kind == OfferKind.Weapon) return ContentCatalog.P2.GetWeapon(offer.Id).DisplayName;
            return PassiveCatalog.P2.TryGet(offer.Id, out var passive) ? passive.DisplayName : offer.Id;
        }

        public static float WeaponDps(CombatGame game, WeaponDefinition weapon, int tier)
        {
            float tierFactor = tier == 1 ? 1f : tier == 2 ? 1.35f : 1.8f;
            return weapon.DamagePerHit * weapon.PelletsPerShot / weapon.ShotCooldownSeconds * tierFactor *
                game.Stats.DamageFactor * (1 + game.Stats.AttackSpeedBonus);
        }

        /// <summary>One short line for an offer card.</summary>
        public static string OfferSummary(CombatGame game, ShopOffer offer)
        {
            if (offer.Kind == OfferKind.Weapon)
            {
                var weapon = ContentCatalog.P2.GetWeapon(offer.Id);
                return $"DPS {WeaponDps(game, weapon, offer.Tier):0}  ·  băng {weapon.MagazineSize}";
            }
            return $"Đã có {game.Run.PassiveCount(offer.Id)} / {offer.StackCap}";
        }

        public static string OfferPreview(CombatGame game, ShopOffer offer)
        {
            if (offer.Kind == OfferKind.Weapon)
            {
                var weapon = ContentCatalog.P2.GetWeapon(offer.Id);
                return $"<b>{weapon.DisplayName}</b> · bậc {offer.Tier}\n" +
                    $"DPS vũ khí mới: 0 → {WeaponDps(game, weapon, offer.Tier):0.0}   ·   Băng đạn: {weapon.MagazineSize}   ·   Tầm bắn: {weapon.Range:0.#} m\n" +
                    $"Ô vũ khí: {game.WeaponCount} → {game.WeaponCount + 1}/4   ·   Tiếp tế: {game.Currency} → {game.Currency - offer.Price}";
            }
            ProgressionStats before = game.Stats, after = game.Run.PreviewStats(offer.Modifier);
            return $"<b>{OfferName(offer)}</b> · lần {game.Run.PassiveCount(offer.Id) + 1}/{offer.StackCap}\n" +
                StatsBeforeAfter(before, after, "   ·   ") + $"\nTiếp tế: {game.Currency} → {game.Currency - offer.Price}";
        }

        public static string UpgradePreview(CombatGame game, UpgradeKind kind) =>
            StatsBeforeAfter(game.Stats, game.Run.PreviewStats(ProgressionRules.Upgrade(kind)), "\n");

        public static string StatsBeforeAfter(ProgressionStats before, ProgressionStats after, string separator)
        {
            var lines = new System.Collections.Generic.List<string>();
            void Line(bool changed, string text) { if (changed) lines.Add(text); }
            float baseHealth = CombatRules.PlayerMaxHealth - 100f;
            Line(before.MaxHealth != after.MaxHealth, $"HP tối đa {before.MaxHealth + baseHealth:0} → <color=#F6CD6E>{after.MaxHealth + baseHealth:0}</color>");
            Line(before.DamageFactor != after.DamageFactor, $"Sát thương {before.DamageFactor * 100:0}% → <color=#F6CD6E>{after.DamageFactor * 100:0}%</color>");
            Line(before.AttackSpeedBonus != after.AttackSpeedBonus, $"Tốc bắn +{before.AttackSpeedBonus * 100:0}% → <color=#F6CD6E>+{after.AttackSpeedBonus * 100:0}%</color>");
            Line(before.Armor != after.Armor, $"Giáp {before.Armor:0} → <color=#F6CD6E>{after.Armor:0}</color>");
            Line(before.SpeedFactor != after.SpeedFactor, $"Tốc độ {before.SpeedFactor * 100:0}% → <color=#F6CD6E>{after.SpeedFactor * 100:0}%</color>");
            Line(before.Regen != after.Regen, $"Hồi HP {before.Regen:0.0}/s → <color=#F6CD6E>{after.Regen:0.0}/s</color>");
            Line(before.PickupRadius != after.PickupRadius, $"Tầm nhặt {before.PickupRadius:0.0} → <color=#F6CD6E>{after.PickupRadius:0.0}</color>");
            Line(before.ReloadFactor != after.ReloadFactor, $"Thời gian nạp {before.ReloadFactor * 100:0}% → <color=#F6CD6E>{after.ReloadFactor * 100:0}%</color>");
            Line(before.RangeFactor != after.RangeFactor, $"Tầm bắn {before.RangeFactor * 100:0}% → <color=#F6CD6E>{after.RangeFactor * 100:0}%</color>");
            Line(before.MagazineFactor != after.MagazineFactor, $"Băng đạn {before.MagazineFactor * 100:0}% → <color=#F6CD6E>{after.MagazineFactor * 100:0}%</color>");
            Line(before.IncomeFactor != after.IncomeFactor, $"Thu nhập {before.IncomeFactor * 100:0}% → <color=#F6CD6E>{after.IncomeFactor * 100:0}%</color>");
            Line(before.ExperienceFactor != after.ExperienceFactor, $"Kinh nghiệm {before.ExperienceFactor * 100:0}% → <color=#F6CD6E>{after.ExperienceFactor * 100:0}%</color>");
            return lines.Count == 0 ? "Chỉ số đã đạt giới hạn." : string.Join(separator, lines);
        }

        public static string PurchaseMessage(PurchaseResult result)
        {
            switch (result)
            {
                case PurchaseResult.Bought: return "Đã mua hàng.";
                case PurchaseResult.InsufficientCurrency: return "Chưa đủ tiếp tế.";
                case PurchaseResult.WeaponSlotsFull: return "Đã đủ 4 vũ khí. Hãy bán hoặc ghép.";
                case PurchaseResult.StackCapReached: return "Đã đạt giới hạn vật phẩm.";
                default: return "Ô hàng không còn hiệu lực.";
            }
        }
    }
}
