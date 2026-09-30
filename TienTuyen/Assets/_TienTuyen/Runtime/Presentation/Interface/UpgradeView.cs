using System;
using TMPro;
using TienTuyen.Combat;
using TienTuyen.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace TienTuyen.Presentation.Interface
{
    /// <summary>Level-up choice: three cards with an icon, the stat change and a number shortcut.</summary>
    public sealed class UpgradeView : ModalScreen
    {
        private const float CardWidth = 280f, CardHeight = 270f, Gap = 30f;
        private readonly CombatGame game;
        private readonly Func<UpgradeKind, string> preview;
        private readonly TMP_Text subtitle;
        private readonly Card[] cards = new Card[3];
        private sealed class Card { public UiButton button; public Image icon; public TMP_Text name, description, body; }

        public UiButton First => cards[0].button;

        public UpgradeView(Transform parent, CombatGame game, Action<int> choose, Func<UpgradeKind, string> preview)
            : base("Upgrade", parent, new Vector2(CardWidth * 3 + Gap * 2, 410), false)
        {
            this.game = game;
            this.preview = preview;
            Title.text = "CHỌN NÂNG CẤP";
            UiTheme.Shadow(Title);
            subtitle = UiKit.Text("Subtitle", Panel, "", UiTheme.Body, 17, UiTheme.Alpha(UiTheme.Paper, .8f), TextAlignmentOptions.TopLeft);
            UiKit.Place(subtitle, UiKit.TopLeft, UiKit.TopLeft, new Vector2(34, -100), new Vector2(800, 24));
            UiTheme.Shadow(subtitle);

            for (int i = 0; i < cards.Length; i++)
            {
                int index = i;
                var slot = UiKit.Node("Choice" + i, Panel, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(i * (CardWidth + Gap), 0), new Vector2(CardWidth, CardHeight));
                var card = new Card { button = UiButton.Create("Card", slot, UiButton.Style.Card, null, () => choose(index), 8f) };
                var content = card.button.Content;
                var key = UiKit.Keycap(content, (i + 1).ToString(), 12f);
                UiKit.Place(key, UiKit.TopLeft, UiKit.TopLeft, new Vector2(14, -14), new Vector2(24, 22));
                var halo = UiKit.Box("Halo", content, UiKit.TopCenter, new Vector2(.5f, .5f), new Vector2(0, -62), new Vector2(120, 120), UiSprites.Glow, UiTheme.Alpha(UiTheme.Gold, .16f));
                halo.name = "Halo";
                card.icon = UiKit.Icon("Icon", content, UiKit.TopCenter, new Vector2(.5f, .5f), new Vector2(0, -62), new Vector2(56, 56), null, UiTheme.GoldBright);
                card.name = UiKit.Text("Name", content, "", UiTheme.Display, 26, UiTheme.Paper, TextAlignmentOptions.Top, 4f);
                UiKit.Place(card.name, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -104), new Vector2(CardWidth - 30, 34));
                card.description = UiKit.Text("Description", content, "", UiTheme.Body, 14, UiTheme.Muted, TextAlignmentOptions.Top);
                UiKit.Place(card.description, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -140), new Vector2(CardWidth - 36, 40));
                UiKit.Box("Rule", content, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -186), new Vector2(36, 2), UiSprites.White, UiTheme.Alpha(UiTheme.Gold, .6f));
                card.body = UiKit.Text("Preview", content, "", UiTheme.Body, 16, UiTheme.Alpha(UiTheme.Paper, .9f), TextAlignmentOptions.Top);
                UiKit.Place(card.body, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -198), new Vector2(CardWidth - 36, 60));
                cards[i] = card;
            }
        }

        public void Refresh()
        {
            int pending = game.Run?.PendingUpgrades ?? 0;
            Eyebrow.text = $"LÊN CẤP {game.Level}";
            subtitle.text = pending > 1 ? $"Còn {pending} lượt nâng cấp. Cửa hàng mở sau khi chọn xong." : "Chọn một nâng cấp. Cửa hàng mở ngay sau đó.";
            for (int i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                bool offered = i < game.UpgradeChoiceCount;
                card.button.Button.interactable = offered;
                card.icon.enabled = offered;
                if (!offered) { card.name.text = "—"; card.description.text = card.body.text = ""; continue; }
                UpgradeKind kind = game.Run.CurrentChoices[i];
                card.icon.sprite = UiGlyphs.Upgrade(kind);
                card.name.text = UpgradeName(kind);
                card.description.text = UpgradeDescription(kind);
                card.body.text = preview(kind);
            }
        }

        public static string UpgradeDescription(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.Health: return "Tăng lượng sinh lực tối đa.";
                case UpgradeKind.Damage: return "Mọi vũ khí gây thêm sát thương.";
                case UpgradeKind.AttackSpeed: return "Mọi vũ khí bắn nhanh hơn.";
                case UpgradeKind.Armor: return "Giảm sát thương phải nhận.";
                case UpgradeKind.Speed: return "Di chuyển và né tránh nhanh hơn.";
                default: return "Tự hồi sinh lực theo thời gian.";
            }
        }

        public static string UpgradeName(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.Health: return "THỂ LỰC";
                case UpgradeKind.Damage: return "SÁT THƯƠNG";
                case UpgradeKind.AttackSpeed: return "TỐC BẮN";
                case UpgradeKind.Armor: return "GIÁP";
                case UpgradeKind.Speed: return "TỐC ĐỘ";
                case UpgradeKind.Regen: return "HỒI PHỤC";
                default: return kind.ToString().ToUpperInvariant();
            }
        }
    }
}
