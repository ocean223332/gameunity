using System;
using TienTuyen.Presentation.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// The duel HUD, in the wave HUD's corners and type: a scoreboard at the top centre (the round clock between
    /// the two fighters' names and round pips, you on the left, the opponent mirrored on the right; neither health
    /// shows there, so the opponent's stays unknown), your health large at bottom left, the weapon and magazine at bottom right, a crosshair,
    /// and the round banner (countdown, then won / lost / drawn). Reads <see cref="IDuelHudState"/> every frame.
    /// </summary>
    public sealed class DuelHudView : UiScreen
    {
        /// <summary>The knife's weapon id in <see cref="IDuelHudState.WeaponId"/>; any other id is one of the guns.</summary>
        public const string KnifeWeaponId = "weapon.knife";

        private const int MaxPips = 48, MaxRoundPips = 3;

        private TMP_Text roundCaption, timer;
        private Fighter local, opponent;
        private sealed class Fighter
        {
            public TMP_Text Name;
            public readonly Image[] Pips = new Image[MaxRoundPips];
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
        private Image hitArc;
        private Transform hitEye;
        private Vector3 hitFrom;
        private float hitAge = 99f, lastLocalHealth = -1f, healthFlash;

        private readonly RectTransform[] hitTicks = new RectTransform[4];
        private CanvasGroup hitTick;
        private float tickAge = 99f, tickStrength;

        /// <summary>
        /// How long the hit tick shows, and the damage it spans from small and dim to large and bright: an SMG round (10)
        /// to a rifle head hit (48).
        /// </summary>
        private const float TickSeconds = .15f, TickWeakDamage = 10f, TickStrongDamage = 48f;

        /// <summary>How long the hit arc takes to fade, and the share of the circle it spans (about 50 degrees).</summary>
        private const float HitArcSeconds = 1.2f, HitArcSpan = .14f;
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
            BuildHitArc();
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
            local = BuildSide(block, -1f);
            opponent = BuildSide(block, 1f);
        }

        /// <summary>
        /// One fighter's name over their round pips, held close against the clock with a thin rule between, so the
        /// clock and both fighters read as one plate; <paramref name="side"/> -1 grows leftwards from the clock, 1 rightwards.
        /// </summary>
        private static Fighter BuildSide(RectTransform block, float side)
        {
            var result = new Fighter();
            bool right = side > 0;
            var anchor = UiKit.TopCenter;
            var pivot = new Vector2(right ? 0f : 1f, 1f);
            const float rule = 60f, inner = 74f;
            UiKit.Box("Rule", block, anchor, new Vector2(.5f, 1f), new Vector2(side * rule, -26), new Vector2(1, 36),
                null, UiTheme.Alpha(UiTheme.Paper, .22f));
            result.Name = UiKit.Caption("Name", block, "", 14, UiTheme.Paper, right ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
            // Tall enough for the font's line height: TMP drops an ellipsised line that does not fit vertically.
            UiKit.Place(result.Name, anchor, pivot, new Vector2(side * inner, -20), new Vector2(200, 26));
            result.Name.textWrappingMode = TextWrappingModes.NoWrap;
            result.Name.overflowMode = TextOverflowModes.Ellipsis;
            UiTheme.Shadow(result.Name);
            for (int i = 0; i < MaxRoundPips; i++)
            {
                var pip = UiKit.Box("RoundPip" + i, block, anchor, pivot, new Vector2(side * (inner + 1f + i * 24f), -50), new Vector2(18, 6),
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
            // Your hit landed: four diagonal ticks between the crosshair's arms, larger and brighter the harder it hit.
            // They say how hard you hit, never how much the opponent has left.
            var ticks = UiKit.Node("HitTick", centre, UiKit.Center, new Vector2(.5f, .5f), Vector2.zero, new Vector2(40, 40));
            hitTick = ticks.gameObject.AddComponent<CanvasGroup>();
            hitTick.alpha = 0f;
            for (int i = 0; i < hitTicks.Length; i++)
            {
                var arm = UiKit.Node("Arm" + i, ticks, UiKit.Center, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
                arm.localRotation = Quaternion.Euler(0f, 0f, 45f + 90f * i);
                UiKit.Box("Shadow", arm, UiKit.Center, new Vector2(0f, .5f), new Vector2(-1f, 0f), new Vector2(10, 4), UiSprites.Rounded(1f), UiTheme.Alpha(UiTheme.Ink, .5f));
                UiKit.Box("Tick", arm, UiKit.Center, new Vector2(0f, .5f), Vector2.zero, new Vector2(8, 2), UiSprites.Rounded(1f), UiTheme.Paper);
                hitTicks[i] = arm;
            }
        }

        /// <summary>Your round hit the opponent for <paramref name="damage"/>: flashes the hit tick.</summary>
        public void HitLanded(float damage)
        {
            tickAge = 0f;
            tickStrength = Mathf.InverseLerp(TickWeakDamage, TickStrongDamage, damage);
        }

        private void RefreshHitTick(float deltaTime)
        {
            tickAge += deltaTime;
            float left = 1f - tickAge / TickSeconds;
            hitTick.alpha = left <= 0f ? 0f : Mathf.Lerp(.55f, 1f, tickStrength) * Mathf.Clamp01(left * 2f);
            if (left <= 0f) return;
            // Out from the centre as it fades; a hard hit starts further out and longer.
            float inner = Mathf.Lerp(7f, 10f, tickStrength) + 3f * (1f - left);
            float scale = Mathf.Lerp(.8f, 1.5f, tickStrength);
            foreach (var arm in hitTicks)
            {
                var tick = (RectTransform)arm.GetChild(1);
                var shadow = (RectTransform)arm.GetChild(0);
                tick.anchoredPosition = new Vector2(inner, 0f);
                shadow.anchoredPosition = new Vector2(inner - 1f, 0f);
                tick.localScale = shadow.localScale = new Vector3(scale, 1f, 1f);
            }
        }

        /// <summary>A red arc round the crosshair, on the side the shot came from, turning with you as you look round.</summary>
        private void BuildHitArc()
        {
            hitArc = UiKit.Box("HitArc", Root, UiKit.Center, new Vector2(.5f, .5f), Vector2.zero, new Vector2(230, 230), UiSprites.Ring(.06f), UiTheme.Alpha(UiTheme.Danger, 0f));
            hitArc.type = Image.Type.Filled;
            hitArc.fillMethod = Image.FillMethod.Radial360;
            hitArc.fillOrigin = (int)Image.Origin360.Top;
            hitArc.fillClockwise = true;
            hitArc.fillAmount = HitArcSpan;
            hitArc.raycastTarget = false;
        }

        /// <summary>You were hit from <paramref name="from"/> (world space); the arc points there from <paramref name="eye"/>.</summary>
        public void Wounded(Vector3 from, Transform eye)
        {
            hitFrom = from;
            hitEye = eye;
            hitAge = 0f;
        }

        private void RefreshHitArc(float deltaTime)
        {
            hitAge += deltaTime;
            float fade = 1f - Mathf.Clamp01(hitAge / HitArcSeconds);
            if (hitEye == null || fade <= 0f) { hitArc.color = UiTheme.Alpha(UiTheme.Danger, 0f); return; }
            Vector3 to = hitFrom - hitEye.position, forward = hitEye.forward;
            to.y = forward.y = 0f;
            float bearing = to.sqrMagnitude > 1e-4f && forward.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(forward, to, Vector3.up) : 0f;
            // The fill runs clockwise from the top; turn it back by half its span to centre it, then to the bearing.
            hitArc.rectTransform.localEulerAngles = new Vector3(0f, 0f, HitArcSpan * 180f - bearing);
            hitArc.color = UiTheme.Alpha(UiTheme.Danger, .7f * fade * fade);
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
            RefreshHitArc(deltaTime);
            RefreshHitTick(deltaTime);
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
            Show(local, state.LocalName, state.LocalRounds, state.RoundsToWin, UiTheme.GoldBright);
            Show(opponent, state.OpponentName, state.OpponentRounds, state.RoundsToWin, UiTheme.Danger);
        }

        private static void Show(Fighter side, string name, int won, int toWin, Color pip)
        {
            string label = string.IsNullOrEmpty(name) ? "" : name.ToUpperInvariant();
            if (side.Name.text != label) side.Name.text = label;
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
            float damage = lastLocalHealth >= 0f ? lastLocalHealth - state.LocalHealth : 0f;
            lastLocalHealth = state.LocalHealth;
            healthFlash = damage > 0f ? 1f : Mathf.Max(0f, healthFlash - deltaTime * 3f);
            healthText.color = Color.Lerp(low ? Color.Lerp(UiTheme.Paper, UiTheme.Danger, .6f + .4f * pulse) : UiTheme.Paper, UiTheme.Danger, healthFlash);
            // A hit is one flash scaled by its damage, from a faint edge for an SMG round (10) to at most .6 for a rifle
            // head hit (48); low health is a steady pulse. Kept quiet: a first-time player must read it as feedback,
            // not as the screen going wrong.
            hurtFlash = Mathf.Max(hurtFlash - deltaTime * 1.6f, damage > 0f ? Mathf.Clamp(damage / 60f, .3f, .6f) : 0f);
            hurt.color = UiTheme.Alpha(UiTheme.Danger, .32f * hurtFlash + (low ? .12f + .08f * pulse : 0f));
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
            var result = state.MatchResult;
            bool won = result == DuelRoundResult.Won, drawn = result == DuelRoundResult.Draw;
            Accent(won ? UiTheme.Gold : drawn ? UiTheme.Muted : UiTheme.Danger);
            Eyebrow.text = $"KẾT THÚC TRẬN  ·  {state.Round} HIỆP";
            Title.text = won ? "CHIẾN THẮNG" : drawn ? "HÒA" : "THẤT BẠI";
            score.text = $"{state.LocalRounds}<color=#6E7868> – </color>{state.OpponentRounds}";
            localLabel.text = string.IsNullOrEmpty(state.LocalName) ? "BẠN" : state.LocalName.ToUpperInvariant();
            opponentLabel.text = string.IsNullOrEmpty(state.OpponentName) ? "ĐỐI THỦ" : state.OpponentName.ToUpperInvariant();
            // Level rounds with a winner means the damage broke the tie: saying either of you "won more rounds"
            // would contradict the score printed right above this line.
            bool onDamage = state.LocalRounds == state.OpponentRounds;
            line.text = drawn ? $"Hòa {state.LocalRounds}–{state.OpponentRounds}, sát thương ngang nhau. Đá lại để phân thắng bại."
                : won ? (onDamage ? "Hòa hiệp, nhưng bạn gây nhiều sát thương hơn. Thắng sát nút!"
                                  : "Bạn đã thắng " + state.LocalRounds + " hiệp trước. Giữ vững phong độ!")
                : (onDamage ? "Hòa hiệp, nhưng đối thủ gây nhiều sát thương hơn. Hẹn trận sau."
                            : "Đối thủ đã thắng " + state.OpponentRounds + " hiệp trước. Hẹn trận sau.");
        }
    }
}
