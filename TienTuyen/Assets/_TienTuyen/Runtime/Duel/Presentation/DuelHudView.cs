using System;
using TienTuyen.Presentation.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// The duel HUD, in the wave HUD's corners and type: a scoreboard at the top centre (the round clock between
    /// the two fighters' health bars, you on the left in green, the opponent mirrored on the right in red, round
    /// pips under each), your health large at bottom left, the weapon and magazine at bottom right, a crosshair,
    /// and the round banner (countdown, then won / lost / drawn). Reads <see cref="IDuelHudState"/> every frame.
    /// </summary>
    public sealed class DuelHudView : UiScreen
    {
        /// <summary>The knife's weapon id in <see cref="IDuelHudState.WeaponId"/>; any other id is one of the guns.</summary>
        public const string KnifeWeaponId = "weapon.knife";

        private const int MaxPips = 48, MaxRoundPips = 3;
        private const float BarWidth = 250f;

        private TMP_Text roundCaption, timer;
        private Fighter local, opponent;
        private sealed class Fighter
        {
            public TMP_Text Name, Value;
            public UiBar Bar;
            public readonly Image[] Pips = new Image[MaxRoundPips];
            public float Flash, Last = -1f;
        }

        private TMP_Text healthText;
        private UiBar healthBar;

        private Image weaponIcon;
        private TMP_Text weaponName, ammoText;
        private RectTransform pipRow;
        private readonly Image[] pips = new Image[MaxPips];
        private UiBar magazineBar;
        private CanvasGroup reloadHint;
        private int pipCount = -1;
        private string lastWeapon;

        private CanvasGroup banner, crosshair;
        private TMP_Text bannerTitle, bannerCaption;
        private Image bannerRule, hurt;
        private float bannerAge = 99f, startAge = 99f, hurtFlash;
        private DuelPhase lastPhase = (DuelPhase)(-1);
        private int lastRound = -1;

        public DuelHudView(Transform parent) : base("DuelHud", parent)
        {
            SlideFrom = Vector2.zero;
            hurt = UiKit.Image("Hurt", Root, UiSprites.Vignette, UiTheme.Alpha(UiTheme.Danger, 0f));
            Shade(true, 150f, .62f);
            Shade(false, 190f, .72f);
            BuildScoreboard();
            BuildHealth();
            BuildWeapon();
            BuildCrosshair();
            BuildBanner();
        }

        // ---------- Construction ----------

        private void Shade(bool top, float height, float alpha)
        {
            var shade = UiKit.Image(top ? "TopShade" : "BottomShade", Root, UiSprites.FadeUp, UiTheme.Alpha(UiTheme.Ink, alpha));
            var rect = shade.rectTransform;
            rect.anchorMin = new Vector2(0, top ? 1 : 0);
            rect.anchorMax = new Vector2(1, top ? 1 : 0);
            rect.sizeDelta = new Vector2(0, height);
            rect.anchoredPosition = new Vector2(0, top ? -height * .5f : height * .5f);
            if (top) rect.localScale = new Vector3(1, -1, 1);
        }

        private void BuildScoreboard()
        {
            var block = UiKit.Node("Scoreboard", Root, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -16), new Vector2(760, 100));
            roundCaption = Centered(UiKit.Caption("Round", block, "", 13, UiTheme.Gold, TextAlignmentOptions.Center), -2, 18);
            timer = Centered(UiKit.Text("Timer", block, "", UiTheme.Display, 42, UiTheme.Paper, TextAlignmentOptions.Center, 2f), -18, 52);
            UiTheme.Shadow(timer);
            local = BuildSide(block, -1f, UiTheme.Health);
            opponent = BuildSide(block, 1f, UiTheme.Danger);
        }

        /// <summary>One fighter's name, health bar and round pips; <paramref name="side"/> -1 grows leftwards from the clock, 1 rightwards.</summary>
        private static Fighter BuildSide(RectTransform block, float side, Color color)
        {
            var result = new Fighter();
            bool right = side > 0;
            var anchor = UiKit.TopCenter;
            var pivot = new Vector2(right ? 0f : 1f, 1f);
            const float inner = 92f;
            result.Name = UiKit.Caption("Name", block, "", 13, UiTheme.Paper, right ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
            // Tall enough for the font's line height: TMP drops an ellipsised line that does not fit vertically.
            UiKit.Place(result.Name, anchor, pivot, new Vector2(side * inner, -10), new Vector2(BarWidth - 50, 26));
            result.Name.textWrappingMode = TextWrappingModes.NoWrap;
            result.Name.overflowMode = TextOverflowModes.Ellipsis;
            UiTheme.Shadow(result.Name);
            result.Value = UiKit.Text("Value", block, "", UiTheme.Display, 15, UiTheme.Paper, right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
            UiKit.Place(result.Value, anchor, new Vector2(right ? 1f : 0f, 1f), new Vector2(side * (inner + BarWidth), -14), new Vector2(60, 18));
            UiTheme.Shadow(result.Value);
            result.Bar = new UiBar(right ? "OpponentHealth" : "LocalHealth", block, color, true, 3f);
            // The opponent's bar is the local one mirrored: both keep their fill at the outer end and empty towards the clock.
            UiKit.Place(result.Bar.Root, anchor, new Vector2(.5f, 1f), new Vector2(side * (inner + BarWidth * .5f), -36), new Vector2(BarWidth, 10));
            if (right) result.Bar.Root.localScale = new Vector3(-1, 1, 1);
            for (int i = 0; i < MaxRoundPips; i++)
            {
                var pip = UiKit.Box("RoundPip" + i, block, anchor, new Vector2(.5f, 1f), new Vector2(side * (inner + 8f + i * 22f), -54), new Vector2(16, 6),
                    UiSprites.Rounded(1.5f), UiTheme.Alpha(UiTheme.Paper, .16f));
                result.Pips[i] = pip;
            }
            return result;
        }

        private void BuildHealth()
        {
            var block = UiKit.Node("Health", Root, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(30, 26), new Vector2(360, 100));
            UiKit.Icon("HeartIcon", block, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(0, 74), new Vector2(14, 14),
                UiGlyphs.Upgrade(TienTuyen.Progression.UpgradeKind.Health), UiTheme.Health);
            var caption = UiKit.Caption("Caption", block, "BẠN  ·  SINH LỰC", 11, UiTheme.Muted);
            UiKit.Place(caption, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(21, 73), new Vector2(260, 16));
            healthText = UiKit.Text("Value", block, "", UiTheme.Display, 44, UiTheme.Paper, TextAlignmentOptions.BottomLeft);
            UiKit.Place(healthText, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(-1, 12), new Vector2(300, 60));
            UiTheme.Shadow(healthText);
            healthBar = new UiBar("HealthBar", block, UiTheme.Health, true, 3f);
            UiKit.Place(healthBar.Root, UiKit.BottomLeft, UiKit.BottomLeft, Vector2.zero, new Vector2(300, 10));
        }

        private void BuildWeapon()
        {
            var block = UiKit.Node("Weapon", Root, UiKit.BottomRight, UiKit.BottomRight, new Vector2(-30, 26), new Vector2(380, 130));
            weaponIcon = UiKit.Icon("Icon", block, UiKit.BottomRight, UiKit.BottomRight, new Vector2(0, 78), new Vector2(160, 50), null, UiTheme.Alpha(UiTheme.Paper, .95f));
            weaponName = UiKit.Caption("Name", block, "", 13, UiTheme.Muted, TextAlignmentOptions.BottomRight);
            UiKit.Place(weaponName, UiKit.BottomRight, UiKit.BottomRight, new Vector2(-172, 84), new Vector2(200, 18));
            ammoText = UiKit.Text("Ammo", block, "", UiTheme.Display, 48, UiTheme.Paper, TextAlignmentOptions.BottomRight);
            UiKit.Place(ammoText, UiKit.BottomRight, UiKit.BottomRight, new Vector2(0, 12), new Vector2(260, 64));
            UiTheme.Shadow(ammoText);
            pipRow = UiKit.Node("Magazine", block, UiKit.BottomRight, UiKit.BottomRight, Vector2.zero, new Vector2(300, 8));
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i] = UiKit.Image("Round", pipRow, UiSprites.Rounded(1f), UiTheme.Paper);
                pips[i].rectTransform.anchorMin = pips[i].rectTransform.anchorMax = Vector2.zero;
                pips[i].rectTransform.pivot = Vector2.zero;
            }
            magazineBar = new UiBar("Continuous", pipRow, UiTheme.Paper, false, 2f);
            var hint = UiKit.HintRow("ReloadHint", block, 13f, "R", "NẠP ĐẠN");
            UiKit.Place(hint, UiKit.BottomRight, UiKit.BottomRight, new Vector2(-266, 26), new Vector2(110, 22));
            reloadHint = hint.gameObject.AddComponent<CanvasGroup>();
        }

        private void BuildCrosshair()
        {
            var centre = UiKit.Node("Crosshair", Root, UiKit.Center, new Vector2(.5f, .5f), Vector2.zero, new Vector2(40, 40));
            crosshair = centre.gameObject.AddComponent<CanvasGroup>();
            UiKit.Box("Dot", centre, UiKit.Center, new Vector2(.5f, .5f), Vector2.zero, new Vector2(4, 4), UiSprites.Disc, UiTheme.Paper);
            for (int i = 0; i < 4; i++)
            {
                var direction = new Vector2(i == 0 ? 1 : i == 1 ? -1 : 0, i == 2 ? 1 : i == 3 ? -1 : 0);
                var size = direction.x != 0 ? new Vector2(8, 2) : new Vector2(2, 8);
                UiKit.Box("Shadow", centre, UiKit.Center, new Vector2(.5f, .5f), direction * 10f, size + Vector2.one * 2f, UiSprites.Rounded(1f), UiTheme.Alpha(UiTheme.Ink, .45f));
                UiKit.Box("Tick", centre, UiKit.Center, new Vector2(.5f, .5f), direction * 10f, size, UiSprites.Rounded(1f), UiTheme.Alpha(UiTheme.Paper, .92f));
            }
        }

        private void BuildBanner()
        {
            var block = UiKit.Node("RoundBanner", Root, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -150), new Vector2(600, 130));
            banner = block.gameObject.AddComponent<CanvasGroup>();
            banner.alpha = 0;
            // A soft dark pool behind the words keeps them readable over sunlit foliage.
            UiKit.Box("Backdrop", block, UiKit.Center, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1100, 420), UiSprites.Glow, UiTheme.Alpha(UiTheme.Ink, .95f));
            bannerTitle = Centered(UiKit.Text("Title", block, "", UiTheme.Display, 68, UiTheme.Paper, TextAlignmentOptions.Center, 6f), 0, 80);
            UiTheme.Shadow(bannerTitle);
            bannerRule = UiKit.Box("Rule", block, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -94), new Vector2(64, 2), UiSprites.White, UiTheme.Gold);
            bannerCaption = Centered(UiKit.Caption("Caption", block, "", 14, UiTheme.Gold, TextAlignmentOptions.Center), -102, 20);
            UiTheme.Shadow(bannerCaption);
        }

        private static TMP_Text Centered(TMP_Text label, float top, float height)
        {
            UiKit.Place(label, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, top), new Vector2(600, height));
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        // ---------- Per-frame refresh ----------

        public void Refresh(IDuelHudState state, float deltaTime)
        {
            if (state == null) return;
            RefreshScoreboard(state, deltaTime);
            RefreshHealth(state, deltaTime);
            RefreshWeapon(state, deltaTime);
            RefreshBanner(state, deltaTime);
            crosshair.alpha = Mathf.MoveTowards(crosshair.alpha, state.Phase == DuelPhase.Live ? 1f : 0f, deltaTime * 6f);
        }

        private void RefreshScoreboard(IDuelHudState state, float deltaTime)
        {
            int rounds = DuelMatch.MaxRounds;
            roundCaption.text = $"HIỆP {state.Round} / {rounds}";
            float clock = state.Phase == DuelPhase.Warmup ? state.RoundRemaining : Mathf.Max(0f, state.RoundRemaining);
            int seconds = Mathf.CeilToInt(clock);
            timer.text = $"{seconds / 60}:{seconds % 60:00}";
            bool urgent = state.Phase == DuelPhase.Live && seconds <= 10;
            timer.color = urgent ? Color.Lerp(UiTheme.Paper, UiTheme.GoldBright, .5f + .5f * Mathf.Sin(Time.unscaledTime * 8f)) : UiTheme.Paper;
            Show(local, state.LocalName, state.LocalHealth, state.LocalMaxHealth, state.LocalRounds, state.RoundsToWin, UiTheme.Health, UiTheme.GoldBright, deltaTime);
            Show(opponent, state.OpponentName, state.OpponentHealth, state.OpponentMaxHealth, state.OpponentRounds, state.RoundsToWin, UiTheme.Danger, UiTheme.Danger, deltaTime);
        }

        private static void Show(Fighter side, string name, float health, float max, int won, int toWin, Color bar, Color pip, float deltaTime)
        {
            string label = string.IsNullOrEmpty(name) ? "" : name.ToUpperInvariant();
            if (side.Name.text != label) side.Name.text = label;
            max = Mathf.Max(1f, max);
            if (side.Last >= 0f && health < side.Last) side.Flash = 1f;
            side.Last = health;
            side.Flash = Mathf.Max(0f, side.Flash - deltaTime * 3f);
            side.Value.text = Mathf.CeilToInt(Mathf.Max(0f, health)).ToString();
            side.Value.color = Color.Lerp(UiTheme.Paper, UiTheme.Danger, side.Flash);
            side.Bar.Set(health / max, deltaTime);
            side.Bar.Fill.color = Color.Lerp(bar, UiTheme.Paper, side.Flash * .6f);
            for (int i = 0; i < side.Pips.Length; i++)
            {
                side.Pips[i].gameObject.SetActive(i < toWin);
                side.Pips[i].color = i < won ? pip : UiTheme.Alpha(UiTheme.Paper, .16f);
            }
        }

        private void RefreshHealth(IDuelHudState state, float deltaTime)
        {
            float max = Mathf.Max(1f, state.LocalMaxHealth);
            int health = Mathf.CeilToInt(Mathf.Max(0f, state.LocalHealth));
            healthText.text = $"{health}<size=20><color=#A2AB98>  / {Mathf.CeilToInt(max)}</color></size>";
            float fraction = state.LocalHealth / max;
            healthBar.Set(fraction, deltaTime);
            bool low = fraction <= .3f && state.LocalHealth > 0f;
            float pulse = low ? .5f + .5f * Mathf.Sin(Time.unscaledTime * 7f) : 0f;
            healthBar.Fill.color = low ? Color.Lerp(UiTheme.Danger, UiTheme.GoldBright, pulse * .25f) : UiTheme.Health;
            healthText.color = Color.Lerp(low ? Color.Lerp(UiTheme.Paper, UiTheme.Danger, .6f + .4f * pulse) : UiTheme.Paper, UiTheme.Danger, local.Flash);
            hurtFlash = Mathf.Max(local.Flash, hurtFlash - deltaTime * 2.5f);
            hurt.color = UiTheme.Alpha(UiTheme.Danger, .45f * hurtFlash + (low ? .12f + .08f * pulse : 0f));
        }

        private void RefreshWeapon(IDuelHudState state, float deltaTime)
        {
            if (state.WeaponId != lastWeapon)
            {
                lastWeapon = state.WeaponId;
                weaponIcon.sprite = string.IsNullOrEmpty(state.WeaponId) ? null : state.WeaponId == KnifeWeaponId ? UiGlyphs.Knife : UiGlyphs.Weapon(state.WeaponId);
                weaponIcon.enabled = weaponIcon.sprite != null;
            }
            weaponName.text = (state.WeaponName ?? "").ToUpperInvariant();
            int magazine = Mathf.Max(1, state.MagazineSize), ammo = state.Ammo;
            bool reloading = state.ReloadRemaining > 0f;
            ammoText.text = reloading
                ? $"<size=24><color=#E4B24A>ĐANG NẠP</color></size> {state.ReloadRemaining:0.0}<size=22>s</size>"
                : $"{ammo}<size=22><color=#A2AB98>  / {magazine}</color></size>";
            bool lowAmmo = !reloading && ammo <= Mathf.Max(1, magazine / 4);
            ammoText.color = lowAmmo && ammo == 0 ? UiTheme.Danger : UiTheme.Paper;
            reloadHint.alpha = lowAmmo ? .65f + .35f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;

            float progress = reloading ? 1f - state.ReloadRemaining / Mathf.Max(.01f, state.ReloadDuration) : 0f;
            Color full = reloading ? UiTheme.Gold : lowAmmo ? UiTheme.Danger : UiTheme.Paper;
            if (magazine > MaxPips)
            {
                if (pipCount != 0) LayoutPips(0);
                magazineBar.Fill.color = full;
                magazineBar.Set(reloading ? progress : ammo / (float)magazine, deltaTime);
                return;
            }
            if (pipCount != magazine) LayoutPips(magazine);
            int filled = reloading ? Mathf.RoundToInt(progress * magazine) : ammo;
            for (int i = 0; i < magazine; i++) pips[i].color = i < filled ? full : UiTheme.Alpha(UiTheme.Paper, .16f);
        }

        private void LayoutPips(int count)
        {
            pipCount = count;
            magazineBar.Root.gameObject.SetActive(count == 0);
            const float width = 300f, gap = 3f;
            float pip = count > 0 ? Mathf.Min(14f, (width - gap * (count - 1)) / count) : 0f;
            for (int i = 0; i < pips.Length; i++)
            {
                bool used = i < count;
                pips[i].gameObject.SetActive(used);
                if (!used) continue;
                pips[i].rectTransform.anchoredPosition = new Vector2(width - (count - i) * (pip + gap) + gap, 0);
                pips[i].rectTransform.sizeDelta = new Vector2(pip, 8);
            }
        }

        private void RefreshBanner(IDuelHudState state, float deltaTime)
        {
            if (state.Phase != lastPhase || state.Round != lastRound)
            {
                if (state.Phase == DuelPhase.Live && lastPhase == DuelPhase.Warmup) startAge = 0f;
                if (state.Phase == DuelPhase.RoundOver || state.Phase == DuelPhase.Warmup) bannerAge = 0f;
                lastPhase = state.Phase;
                lastRound = state.Round;
            }
            bannerAge += deltaTime;
            startAge += deltaTime;
            float alpha = 0f;
            Color accent = UiTheme.Gold;
            switch (state.Phase)
            {
                case DuelPhase.Warmup:
                    bannerTitle.text = $"HIỆP {state.Round}";
                    bannerTitle.color = UiTheme.Paper;
                    bannerCaption.text = state.LocalRounds == state.RoundsToWin - 1 && state.OpponentRounds == state.RoundsToWin - 1
                        ? $"HIỆP QUYẾT ĐỊNH  ·  {Mathf.CeilToInt(state.CountdownRemaining)}"
                        : $"CHUẨN BỊ  ·  {Mathf.CeilToInt(state.CountdownRemaining)}";
                    alpha = Mathf.Clamp01(bannerAge / .25f);
                    break;
                case DuelPhase.Live:
                    bannerTitle.text = "BẮT ĐẦU";
                    bannerTitle.color = UiTheme.GoldBright;
                    bannerCaption.text = "MỘT MẠNG  ·  HẠ ĐỐI THỦ";
                    alpha = Mathf.Clamp01((1.1f - startAge) / .4f);
                    break;
                case DuelPhase.RoundOver:
                    var result = state.LastRound;
                    bannerTitle.text = result == DuelRoundResult.Won ? "THẮNG HIỆP" : result == DuelRoundResult.Lost ? "THUA HIỆP" : "HÒA HIỆP";
                    accent = result == DuelRoundResult.Won ? UiTheme.Gold : result == DuelRoundResult.Lost ? UiTheme.Danger : UiTheme.Muted;
                    bannerTitle.color = result == DuelRoundResult.Won ? UiTheme.GoldBright : result == DuelRoundResult.Lost ? UiTheme.Danger : UiTheme.Paper;
                    bannerCaption.text = $"HIỆP {state.Round}  ·  TỈ SỐ {state.LocalRounds} – {state.OpponentRounds}";
                    alpha = Mathf.Clamp01(bannerAge / .25f);
                    break;
            }
            bannerRule.color = accent;
            bannerCaption.color = accent;
            banner.alpha = Mathf.MoveTowards(banner.alpha, alpha, deltaTime * 8f);
            float scale = 1f + (1f - Mathf.Clamp01(bannerAge / .25f)) * .12f;
            banner.transform.localScale = new Vector3(scale, scale, 1f);
        }
    }

    /// <summary>The end of the match: who won, the final score and where to go next.</summary>
    public sealed class DuelResultView : ModalScreen
    {
        private static readonly Vector2 Size = new Vector2(620, 380);
        private readonly TMP_Text score, localLabel, opponentLabel, line;

        public UiButton Primary { get; }
        public UiButton Secondary { get; }

        public DuelResultView(Transform parent, Action toLobby, Action toMenu) : base("DuelResult", parent, Size)
        {
            var body = UiKit.Node("Body", Panel, UiKit.TopLeft, UiKit.TopLeft, new Vector2(34, -118), new Vector2(Size.x - 68, 150));
            var cell = UiKit.Card("Score", body, UiTheme.Alpha(UiTheme.Ink, .45f), UiTheme.Alpha(UiTheme.Paper, .07f), 6f);
            UiKit.Place(cell.rectTransform, UiKit.TopLeft, UiKit.TopLeft, Vector2.zero, new Vector2(Size.x - 68, 112));
            localLabel = UiKit.Caption("Local", cell.transform, "", 11, UiTheme.Health, TextAlignmentOptions.MidlineLeft);
            UiKit.Place(localLabel, UiKit.MiddleLeft, UiKit.MiddleLeft, new Vector2(24, 0), new Vector2(170, 18));
            opponentLabel = UiKit.Caption("Opponent", cell.transform, "", 11, UiTheme.Danger, TextAlignmentOptions.MidlineRight);
            UiKit.Place(opponentLabel, UiKit.MiddleRight, UiKit.MiddleRight, new Vector2(-24, 0), new Vector2(170, 18));
            score = UiKit.Text("Value", cell.transform, "", UiTheme.Display, 64, UiTheme.Paper, TextAlignmentOptions.Center, 4f);
            UiKit.Place(score, UiKit.Center, new Vector2(.5f, .5f), new Vector2(0, 2), new Vector2(220, 80));
            UiTheme.Shadow(score);
            line = UiKit.Text("Line", body, "", UiTheme.Body, 15, UiTheme.Muted, TextAlignmentOptions.TopLeft);
            UiKit.Place(line, UiKit.TopLeft, UiKit.TopLeft, new Vector2(0, -124), new Vector2(Size.x - 68, 22));

            float half = (Size.x - 68 - 12) * .5f;
            Primary = UiButton.Create("Primary", Panel, UiButton.Style.Primary, "VỀ SẢNH ĐỐI KHÁNG", toLobby);
            UiKit.Place(Primary.GetComponent<RectTransform>(), UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(34, 32), new Vector2(half, 54));
            Secondary = UiButton.Create("Secondary", Panel, UiButton.Style.Secondary, "VỀ MENU CHÍNH", toMenu);
            UiKit.Place(Secondary.GetComponent<RectTransform>(), UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(34 + half + 12, 32), new Vector2(half, 54));
        }

        /// <summary>Fills the dialog from the finished match (called when it opens).</summary>
        public void Present(IDuelHudState state)
        {
            bool won = state.MatchWon;
            Accent(won ? UiTheme.Gold : UiTheme.Danger);
            Eyebrow.text = $"KẾT THÚC TRẬN  ·  {state.Round} HIỆP";
            Title.text = won ? "CHIẾN THẮNG" : "THẤT BẠI";
            score.text = $"{state.LocalRounds}<color=#6E7868> – </color>{state.OpponentRounds}";
            localLabel.text = string.IsNullOrEmpty(state.LocalName) ? "BẠN" : state.LocalName.ToUpperInvariant();
            opponentLabel.text = string.IsNullOrEmpty(state.OpponentName) ? "ĐỐI THỦ" : state.OpponentName.ToUpperInvariant();
            line.text = won ? "Bạn đã thắng " + state.LocalRounds + " hiệp trước. Giữ vững phong độ!"
                : "Đối thủ đã thắng " + state.OpponentRounds + " hiệp trước. Hẹn trận sau.";
        }
    }
}
