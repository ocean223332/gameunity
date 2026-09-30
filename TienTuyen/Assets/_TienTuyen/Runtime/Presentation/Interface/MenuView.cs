using System;
using TMPro;
using TienTuyen.Combat;
using TienTuyen.Content;
using UnityEngine;
using UnityEngine.UI;

namespace TienTuyen.Presentation.Interface
{
    /// <summary>
    /// Title screen: the orbiting battlefield shows on the right, a dark fade
    /// carries the title, weapon cards with stat bars and the start button on the left.
    /// </summary>
    public sealed class MenuView : UiScreen
    {
        private static readonly (WeaponKind kind, string id, string blurb)[] Weapons =
        {
            (WeaponKind.Rifle, ContentIds.Rifle, "Tầm xa, nhịp bắn đều"),
            (WeaponKind.Smg, ContentIds.Smg, "Tầm gần, bắn rất nhanh"),
            (WeaponKind.Shotgun, ContentIds.Shotgun, "Sáu viên mỗi phát"),
        };

        private readonly CombatGame game;
        private readonly UiButton[] cards = new UiButton[3];
        private readonly Image[] cardIcons = new Image[3];
        private readonly CanvasGroup[] chosenTags = new CanvasGroup[3];
        private readonly CanvasGroup[] cardSlots = new CanvasGroup[3];
        private TMP_Text summary;
        private float age;

        public UiButton StartButton { get; }

        public MenuView(Transform parent, CombatGame game, Action<WeaponKind> choose, Action start) : base("MainMenu", parent)
        {
            this.game = game;
            var scrim = UiKit.Image("LeftFade", Root, UiSprites.FadeRight, UiTheme.Alpha(UiTheme.Ink, .95f));
            scrim.rectTransform.anchorMax = new Vector2(.78f, 1);
            var bottom = UiKit.Image("BottomFade", Root, UiSprites.FadeUp, UiTheme.Alpha(UiTheme.Ink, .8f));
            bottom.rectTransform.anchorMax = new Vector2(1, 0);
            bottom.rectTransform.sizeDelta = new Vector2(0, 220);
            bottom.rectTransform.pivot = new Vector2(.5f, 0);
            UiKit.Image("Vignette", Root, UiSprites.Vignette, UiTheme.Alpha(UiTheme.Ink, .55f));

            var column = UiKit.Node("Column", Root, UiKit.MiddleLeft, UiKit.MiddleLeft, new Vector2(86, 8), new Vector2(560, 590));
            Motion = column;
            BasePosition = column.anchoredPosition;
            SlideFrom = new Vector2(-36, 0);

            UiKit.Box("EyebrowRule", column, UiKit.TopLeft, UiKit.TopLeft, new Vector2(0, -10), new Vector2(34, 2), UiSprites.White, UiTheme.Gold);
            Top(UiKit.Caption("Eyebrow", column, "TRƯỜNG SƠN  ·  MÙA KHÔ 1972", 13, UiTheme.Gold), 46, -2, 480, 18);
            var title = Top(UiKit.Text("Title", column, "TIỀN TUYẾN", UiTheme.Display, 100, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1f), -5, -14, 580, 130);
            UiTheme.Shadow(title);
            Top(UiKit.Text("Tagline", column, "Giữ khoảng cách. Tận dụng vật cản.\nGiữ trạm tiếp tế qua sáu đợt tấn công.", UiTheme.Body, 19,
                UiTheme.Alpha(UiTheme.Paper, .86f), TextAlignmentOptions.TopLeft), 0, -146, 540, 56);

            Top(UiKit.Caption("ChooseCaption", column, "CHỌN VŨ KHÍ KHỞI ĐẦU", 12, UiTheme.Muted), 0, -222, 360, 16);
            const float cardWidth = 176f, cardHeight = 200f, gap = 12f;
            for (int i = 0; i < Weapons.Length; i++)
            {
                int index = i;
                var slot = UiKit.Node("CardSlot" + i, column, UiKit.TopLeft, UiKit.TopLeft, new Vector2(i * (cardWidth + gap), -246), new Vector2(cardWidth, cardHeight));
                cardSlots[i] = slot.gameObject.AddComponent<CanvasGroup>();
                cards[i] = UiButton.Create("Weapon" + i, slot, UiButton.Style.Card, null, () => choose(Weapons[index].kind));
                BuildCard(i, cards[i].Content);
            }

            StartButton = UiButton.Create("Start", column, UiButton.Style.Primary, "BẮT ĐẦU CHIẾN ĐẤU", start);
            UiKit.Place(StartButton.GetComponent<RectTransform>(), UiKit.TopLeft, UiKit.TopLeft, new Vector2(0, -468), new Vector2(552, 58));
            StartButton.Label.fontSize = 22;
            var enter = UiKit.Keycap(StartButton.Content, "ENTER", 11f);
            UiKit.Place(enter, UiKit.MiddleRight, UiKit.MiddleRight, new Vector2(-16, 0), new Vector2(50, 21));
            enter.GetComponent<Image>().color = UiTheme.Alpha(UiTheme.Ink, .18f);
            enter.GetChild(0).GetComponent<Image>().color = UiTheme.Alpha(UiTheme.Ink, .5f);
            enter.GetComponentInChildren<TMP_Text>().color = UiTheme.Ink;
            summary = Top(UiKit.Text("Summary", column, "", UiTheme.Body, 15, UiTheme.Muted), 0, -538, 552, 22);

            var hints = UiKit.HintRow("Controls", Root, 12f, "W+A+S+D", "Di chuyển", "CHUỘT", "Ngắm", "CHUỘT TRÁI", "Bắn",
                "CHUỘT PHẢI", "Ngắm kỹ", "R", "Nạp đạn", "ESC", "Tạm dừng");
            UiKit.Place(hints, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(86, 30), new Vector2(0, 22));
            hints.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            var fitter = hints.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var build = UiKit.Caption("Build", Root, "VERTICAL SLICE P2  ·  6 ĐỢT  ·  3 VŨ KHÍ", 10, UiTheme.Faint, TextAlignmentOptions.BottomRight);
            UiKit.Place(build, UiKit.BottomRight, UiKit.BottomRight, new Vector2(-30, 30), new Vector2(400, 16));
        }

        private void BuildCard(int index, RectTransform content)
        {
            var definition = ContentCatalog.P2.GetWeapon(Weapons[index].id);
            var key = UiKit.Keycap(content, (index + 1).ToString(), 11f);
            UiKit.Place(key, UiKit.TopLeft, UiKit.TopLeft, new Vector2(12, -12), new Vector2(22, 21));
            var tag = UiKit.Caption("Chosen", content, "ĐÃ CHỌN", 10, UiTheme.Gold, TextAlignmentOptions.TopRight);
            UiKit.Place(tag, UiKit.TopRight, UiKit.TopRight, new Vector2(-12, -15), new Vector2(90, 14));
            chosenTags[index] = tag.gameObject.AddComponent<CanvasGroup>();
            cardIcons[index] = UiKit.Icon("Icon", content, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -40), new Vector2(150, 46),
                UiGlyphs.Weapon(definition.Id), UiTheme.Paper);
            var name = UiKit.Text("Name", content, definition.DisplayName.ToUpperInvariant(), UiTheme.Display, 20, UiTheme.Paper, TextAlignmentOptions.Top, 3f);
            UiKit.Place(name, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -92), new Vector2(170, 26));
            var blurb = UiKit.Text("Blurb", content, Weapons[index].blurb, UiTheme.Body, 13, UiTheme.Muted, TextAlignmentOptions.Top);
            UiKit.Place(blurb, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -118), new Vector2(170, 18));

            Stat(content, 0, "SÁT THƯƠNG", Rank(w => w.DamagePerHit * w.PelletsPerShot, definition));
            Stat(content, 1, "TỐC BẮN", Rank(w => 1f / w.ShotCooldownSeconds, definition));
            Stat(content, 2, "TẦM BẮN", Rank(w => w.Range, definition));
        }

        /// <summary>1–5 segments relative to the strongest starter weapon for this stat.</summary>
        private static int Rank(Func<WeaponDefinition, float> stat, WeaponDefinition weapon)
        {
            float best = 0f;
            foreach (var entry in Weapons) best = Mathf.Max(best, stat(ContentCatalog.P2.GetWeapon(entry.id)));
            return Mathf.Clamp(Mathf.RoundToInt(stat(weapon) / best * 5f), 1, 5);
        }

        private static void Stat(RectTransform content, int row, string caption, int rank)
        {
            float y = -144 - row * 16;
            var label = UiKit.Text("Stat", content, caption, UiTheme.Display, 10, UiTheme.Muted, TextAlignmentOptions.MidlineLeft, 6f);
            UiKit.Place(label, UiKit.TopLeft, UiKit.TopLeft, new Vector2(14, y), new Vector2(80, 14));
            for (int i = 0; i < 5; i++)
            {
                var segment = UiKit.Box("Segment", content, UiKit.TopLeft, UiKit.TopLeft, new Vector2(92 + i * 14, y - 5), new Vector2(11, 4),
                    UiSprites.Rounded(1f), i < rank ? UiTheme.Gold : UiTheme.Alpha(UiTheme.Paper, .14f));
                segment.name = i < rank ? "On" : "Off";
            }
        }

        private static T Top<T>(T graphic, float x, float y, float width, float height) where T : Graphic =>
            UiKit.Place(graphic, UiKit.TopLeft, UiKit.TopLeft, new Vector2(x, y), new Vector2(width, height));

        protected override void OnShow() => age = 0f;

        public void Refresh(float deltaTime)
        {
            age += deltaTime;
            for (int i = 0; i < cards.Length; i++)
            {
                bool chosen = game.SelectedWeapon == Weapons[i].kind;
                cards[i].Chosen = chosen;
                chosenTags[i].alpha = chosen ? 1f : 0f;
                cardIcons[i].color = chosen ? UiTheme.GoldBright : UiTheme.Alpha(UiTheme.Paper, .8f);
                // Cards rise in one after another when the menu opens.
                float t = Mathf.Clamp01((age - .12f - i * .07f) / .35f);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                cardSlots[i].alpha = eased;
                cards[i].GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -16f * (1f - eased));
            }
            var weapon = ContentCatalog.P2.GetWeapon(Weapons[(int)game.SelectedWeapon].id);
            summary.text = $"Bộ binh  ·  {Mathf.CeilToInt(game.MaxHealth)} HP  ·  {weapon.DisplayName}: {weapon.MagazineSize} viên/băng, nạp {weapon.ReloadSeconds:0.0}s";
        }
    }
}
