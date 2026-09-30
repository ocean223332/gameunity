using System;
using TMPro;
using TienTuyen.Combat;
using TienTuyen.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace TienTuyen.Presentation.Interface
{
    /// <summary>Between-wave supply station: four offers with locks, details, buy/reroll, loadout and selling.</summary>
    public sealed class ShopView : ModalScreen
    {
        private static readonly Vector2 Size = new Vector2(1100, 668);
        private const float Margin = 34f, Gap = 10f;
        private static readonly float Column = (Size.x - Margin * 2 - Gap * 3) / 4f;

        private readonly CombatGame game;
        private readonly Action<string> sound;
        private readonly Offer[] offers = new Offer[4];
        private readonly UiButton[] locks = new UiButton[4];
        private readonly Sell[] sells = new Sell[4];
        private readonly TMP_Text wallet, health, preview, message;
        private readonly UiButton buy, reroll, bandage, combine;
        private int selected;

        private sealed class Offer
        {
            public UiButton button;
            public Image weaponIcon, itemIcon, priceIcon;
            public Image[] tier;
            public TMP_Text header, name, summary, price;
        }

        private sealed class Sell { public UiButton button; public Image icon; public TMP_Text name, refund; }

        public UiButton Next { get; }
        public UiButton FirstOffer => offers[0].button;

        public ShopView(Transform parent, CombatGame game, Action<string> sound) : base("Shop", parent, Size)
        {
            this.game = game;
            this.sound = sound;
            Title.text = "TRẠM TIẾP TẾ";

            UiKit.Icon("WalletIcon", Panel, UiKit.TopRight, UiKit.TopRight, new Vector2(-196, -30), new Vector2(30, 30), UiGlyphs.Supply, UiTheme.Gold);
            wallet = UiKit.Text("Wallet", Panel, "", UiTheme.Display, 38, UiTheme.GoldBright, TextAlignmentOptions.TopRight);
            UiKit.Place(wallet, UiKit.TopRight, UiKit.TopRight, new Vector2(-Margin, -18), new Vector2(150, 48));
            health = UiKit.Text("Health", Panel, "", UiTheme.Body, 15, UiTheme.Muted, TextAlignmentOptions.TopRight);
            UiKit.Place(health, UiKit.TopRight, UiKit.TopRight, new Vector2(-Margin, -68), new Vector2(300, 20));

            for (int i = 0; i < offers.Length; i++)
            {
                int slot = i;
                float x = Margin + i * (Column + Gap);
                var holder = UiKit.Node("Offer" + i, Panel, UiKit.TopLeft, UiKit.TopLeft, new Vector2(x, -112), new Vector2(Column, 196));
                offers[i] = BuildOffer(holder, () => { selected = slot; sound("UI_Click"); }, i);
                var lockHolder = UiKit.Node("Lock" + i, Panel, UiKit.TopLeft, UiKit.TopLeft, new Vector2(x, -314), new Vector2(Column, 30));
                locks[i] = UiButton.Create("Button", lockHolder, UiButton.Style.Secondary, "KHÓA Ô",
                    () => { game.SetOfferLocked(slot, !game.Shop.IsLocked(slot)); sound("UI_Click"); }, 5f);
                locks[i].Label.fontSize = 13;
            }

            float detailWidth = Size.x - Margin * 2 - 262f;
            var details = UiKit.Card("Details", Panel, UiTheme.Alpha(UiTheme.Ink, .5f), UiTheme.Alpha(UiTheme.Paper, .07f), 6f);
            UiKit.Place(details.rectTransform, UiKit.TopLeft, UiKit.TopLeft, new Vector2(Margin, -358), new Vector2(detailWidth, 92));
            var detailCaption = UiKit.Caption("Caption", details.transform, "CHI TIẾT", 10, UiTheme.Muted, TextAlignmentOptions.TopLeft);
            UiKit.Place(detailCaption, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16, -10), new Vector2(200, 14));
            preview = UiKit.Text("Preview", details.transform, "", UiTheme.Body, 15, UiTheme.Paper, TextAlignmentOptions.TopLeft);
            UiKit.Place(preview, UiKit.TopLeft, UiKit.TopLeft, new Vector2(16, -27), new Vector2(detailWidth - 32, 62));
            preview.lineSpacing = -6f;

            float rightX = Margin + detailWidth + 12f, rightWidth = Size.x - Margin - rightX;
            buy = Button(UiButton.Style.Primary, "MUA HÀNG ĐÃ CHỌN", rightX, -358, rightWidth, 50, Buy);
            reroll = Button(UiButton.Style.Secondary, "", rightX, -414, rightWidth, 36, () =>
            {
                bool ok = game.RerollShop();
                message.text = ok ? "Đã đổi hàng mới." : "Không thể đổi hàng.";
                sound("Reroll");
            });
            bandage = Button(UiButton.Style.Secondary, "BĂNG BÓ  ·  +25 HP  ·  15", Margin, -464, 250, 38, () =>
            {
                bool ok = game.BuyBandage();
                message.text = ok ? "Đã hồi 25 HP." : "Không thể băng bó.";
                sound(ok ? "Buy_Success" : "Buy_Fail");
            });
            combine = Button(UiButton.Style.Secondary, "GHÉP VŨ KHÍ TRÙNG", Margin + 260, -464, 250, 38, () =>
            {
                bool ok = game.CombineFirstMatchingWeapons();
                message.text = ok ? "Đã ghép thành vũ khí bậc cao hơn." : "Không có cặp vũ khí phù hợp.";
                sound(ok ? "Buy_Success" : "Buy_Fail");
            });
            bandage.Label.fontSize = combine.Label.fontSize = reroll.Label.fontSize = 14;
            message = UiKit.Text("Message", Panel, "", UiTheme.Body, 15, UiTheme.GoldBright, TextAlignmentOptions.MidlineLeft);
            UiKit.Place(message, UiKit.TopLeft, UiKit.TopLeft, new Vector2(Margin + 530, -464), new Vector2(Size.x - Margin * 2 - 530, 38));

            var loadout = UiKit.Caption("LoadoutCaption", Panel, "VŨ KHÍ ĐANG MANG  ·  BÁN NHẬN LẠI NỬA GIÁ ĐÃ TRẢ", 10, UiTheme.Muted, TextAlignmentOptions.TopLeft);
            UiKit.Place(loadout, UiKit.TopLeft, UiKit.TopLeft, new Vector2(Margin, -516), new Vector2(600, 14));
            for (int i = 0; i < sells.Length; i++)
            {
                int slot = i;
                var holder = UiKit.Node("Sell" + i, Panel, UiKit.TopLeft, UiKit.TopLeft, new Vector2(Margin + i * (Column + Gap), -536), new Vector2(Column, 44));
                var sell = new Sell
                {
                    button = UiButton.Create("Button", holder, UiButton.Style.Secondary, null, () =>
                    {
                        if (game.SellWeapon(slot, out int refund)) { message.text = $"Đã bán, nhận {refund} tiếp tế."; sound("Buy_Success"); }
                    }, 5f)
                };
                var content = sell.button.Content;
                sell.icon = UiKit.Icon("Icon", content, UiKit.MiddleLeft, UiKit.MiddleLeft, new Vector2(10, 0), new Vector2(58, 20), null, UiTheme.Paper);
                sell.name = UiKit.Text("Name", content, "", UiTheme.Display, 13, UiTheme.Paper, TextAlignmentOptions.MidlineLeft, 3f);
                UiKit.Place(sell.name, UiKit.MiddleLeft, UiKit.MiddleLeft, new Vector2(76, 0), new Vector2(Column - 136, 20));
                sell.name.textWrappingMode = TextWrappingModes.NoWrap;
                sell.name.overflowMode = TextOverflowModes.Ellipsis;
                sell.refund = UiKit.Text("Refund", content, "", UiTheme.Display, 13, UiTheme.GoldBright, TextAlignmentOptions.MidlineRight, 3f);
                UiKit.Place(sell.refund, UiKit.MiddleRight, UiKit.MiddleRight, new Vector2(-12, 0), new Vector2(60, 20));
                sells[i] = sell;
            }

            Next = Button(UiButton.Style.Primary, "SANG ĐỢT TIẾP  ›", Margin, -(Size.y - 26 - 50), Size.x - Margin * 2, 50, () =>
            {
                game.ContinueWave();
                sound("UI_Click");
            });
        }

        private Offer BuildOffer(RectTransform holder, Action select, int index)
        {
            var offer = new Offer { button = UiButton.Create("Card", holder, UiButton.Style.Card, null, select, 8f), tier = new Image[3] };
            var content = offer.button.Content;
            offer.header = UiKit.Caption("Header", content, "", 10, UiTheme.Muted, TextAlignmentOptions.TopLeft);
            UiKit.Place(offer.header, UiKit.TopLeft, UiKit.TopLeft, new Vector2(14, -13), new Vector2(160, 14));
            for (int t = 0; t < 3; t++)
                offer.tier[t] = UiKit.Icon("Tier" + t, content, UiKit.TopRight, UiKit.TopRight, new Vector2(-14 - (2 - t) * 12, -14), new Vector2(9, 9), UiGlyphs.Diamond, UiTheme.Gold);
            offer.weaponIcon = UiKit.Icon("Weapon", content, UiKit.TopCenter, new Vector2(.5f, .5f), new Vector2(0, -68), new Vector2(170, 52), null, UiTheme.Paper);
            offer.itemIcon = UiKit.Icon("Item", content, UiKit.TopCenter, new Vector2(.5f, .5f), new Vector2(0, -68), new Vector2(52, 52), null, UiTheme.Paper);
            offer.name = UiKit.Text("Name", content, "", UiTheme.Display, 20, UiTheme.Paper, TextAlignmentOptions.Top, 2f);
            UiKit.Place(offer.name, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -102), new Vector2(Column - 20, 26));
            offer.name.textWrappingMode = TextWrappingModes.NoWrap;
            offer.summary = UiKit.Text("Summary", content, "", UiTheme.Body, 13, UiTheme.Muted, TextAlignmentOptions.Top);
            UiKit.Place(offer.summary, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -130), new Vector2(Column - 20, 18));
            offer.priceIcon = UiKit.Icon("PriceIcon", content, UiKit.BottomCenter, new Vector2(1, .5f), new Vector2(-6, 26), new Vector2(18, 18), UiGlyphs.Supply, UiTheme.Gold);
            offer.price = UiKit.Text("Price", content, "", UiTheme.Display, 20, UiTheme.GoldBright, TextAlignmentOptions.MidlineLeft);
            UiKit.Place(offer.price, UiKit.BottomCenter, new Vector2(0, .5f), new Vector2(0, 26), new Vector2(80, 26));
            return offer;
        }

        private UiButton Button(UiButton.Style style, string caption, float x, float y, float width, float height, Action onClick)
        {
            var button = UiButton.Create(string.IsNullOrEmpty(caption) ? "Button" : caption, Panel, style, caption, onClick, 6f);
            UiKit.Place(button.GetComponent<RectTransform>(), UiKit.TopLeft, UiKit.TopLeft, new Vector2(x, y), new Vector2(width, height));
            return button;
        }

        private void Buy()
        {
            var result = game.PurchaseOffer(selected);
            message.text = CombatText.PurchaseMessage(result);
            sound(result == PurchaseResult.Bought ? "Buy_Success" : "Buy_Fail");
        }

        protected override void OnShow()
        {
            selected = 0;
            message.text = "Chọn một ô hàng để xem chỉ số và giá.";
        }

        public void Refresh()
        {
            if (game.Shop == null) return;
            Eyebrow.text = $"GIỮA ĐỢT {game.Wave} VÀ {Mathf.Min(game.Wave + 1, game.MaxWave)}";
            wallet.text = game.Currency.ToString();
            health.text = $"TIẾP TẾ  ·  {Mathf.CeilToInt(game.Health)} / {Mathf.CeilToInt(game.MaxHealth)} HP";
            bool hasUnlocked = false;
            for (int i = 0; i < offers.Length; i++)
            {
                ShopOffer offer = game.Shop.OfferAt(i);
                bool locked = game.Shop.IsLocked(i);
                hasUnlocked |= !locked;
                RefreshOffer(offers[i], offer, i, locked);
                locks[i].Button.interactable = offer != null;
                locks[i].Chosen = locked;
                locks[i].Label.text = locked ? "ĐÃ KHÓA  ·  MỞ" : "KHÓA Ô NÀY";
                RefreshSell(sells[i], i);
            }

            ShopOffer chosen = game.Shop.OfferAt(selected);
            buy.Button.interactable = chosen != null && game.Currency >= chosen.Price &&
                (chosen.Kind != OfferKind.Weapon || game.WeaponCount < ProgressionRun.MaxWeaponSlots);
            buy.Label.text = chosen == null ? "MUA HÀNG ĐÃ CHỌN" : $"MUA  ·  {chosen.Price} TIẾP TẾ";
            preview.text = chosen == null ? "Ô này đã trống. Chọn một ô còn hàng để xem chỉ số." : CombatText.OfferPreview(game, chosen);
            reroll.Label.text = $"ĐỔI HÀNG  ·  {game.Shop.NextRerollPrice}";
            reroll.Button.interactable = hasUnlocked && game.Currency >= game.Shop.NextRerollPrice;
            bandage.Button.interactable = !game.BandageUsed && game.Health < game.MaxHealth && game.Currency >= 15;
            combine.Button.interactable = HasMatchingWeapons();
        }

        private void RefreshOffer(Offer view, ShopOffer offer, int slot, bool locked)
        {
            view.button.Button.interactable = offer != null;
            view.button.Chosen = slot == selected && offer != null;
            bool weapon = offer != null && offer.Kind == OfferKind.Weapon;
            view.weaponIcon.enabled = weapon;
            view.itemIcon.enabled = offer != null && !weapon;
            view.priceIcon.enabled = offer != null;
            if (offer == null)
            {
                view.header.text = $"Ô {slot + 1}";
                view.name.text = "ĐÃ BÁN";
                view.summary.text = "Ô trống";
                view.price.text = "";
                for (int t = 0; t < 3; t++) view.tier[t].enabled = false;
                return;
            }
            view.header.text = $"Ô {slot + 1}  ·  {(weapon ? "VŨ KHÍ" : "VẬT PHẨM")}{(locked ? "  ·  KHÓA" : "")}";
            if (weapon) view.weaponIcon.sprite = UiGlyphs.Weapon(offer.Id);
            else view.itemIcon.sprite = UiGlyphs.Passive(offer.Id);
            view.name.text = CombatText.OfferName(offer).ToUpperInvariant();
            view.summary.text = CombatText.OfferSummary(game, offer);
            bool affordable = game.Currency >= offer.Price;
            view.price.text = offer.Price.ToString();
            view.price.color = affordable ? UiTheme.GoldBright : UiTheme.Danger;
            for (int t = 0; t < 3; t++)
            {
                view.tier[t].enabled = true;
                view.tier[t].color = t < offer.Tier ? UiTheme.Gold : UiTheme.Alpha(UiTheme.Paper, .15f);
            }
        }

        private void RefreshSell(Sell view, int slot)
        {
            bool owned = slot < game.WeaponCount;
            view.button.Button.interactable = owned && game.WeaponCount > 1;
            view.icon.enabled = owned;
            if (!owned)
            {
                view.name.text = $"Ô {slot + 1}  ·  TRỐNG";
                view.refund.text = "";
                return;
            }
            var entry = game.Run.Weapons[slot];
            view.icon.sprite = UiGlyphs.Weapon(entry.Id);
            view.name.text = $"{game.WeaponAt(slot).DisplayName.ToUpperInvariant()}  {Roman(entry.Tier)}";
            view.refund.text = game.WeaponCount > 1 ? $"BÁN +{entry.PaidPrice / 2}" : "";
        }

        private static string Roman(int tier) => tier == 1 ? "I" : tier == 2 ? "II" : "III";

        private bool HasMatchingWeapons()
        {
            var weapons = game.Run.Weapons;
            for (int i = 0; i < weapons.Count; i++)
                for (int j = i + 1; j < weapons.Count; j++)
                    if (weapons[i].Id == weapons[j].Id && weapons[i].Tier == weapons[j].Tier && weapons[i].Tier < 3) return true;
            return false;
        }
    }
}
