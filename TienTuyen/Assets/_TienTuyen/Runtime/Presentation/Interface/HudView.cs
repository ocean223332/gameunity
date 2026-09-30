using TMPro;
using TienTuyen.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace TienTuyen.Presentation.Interface
{
    /// <summary>
    /// In-combat HUD laid out in the screen corners so the centre stays clear for
    /// the shoulder camera: supplies/level (top-left), wave clock (top-centre),
    /// health (bottom-left), weapon and magazine (bottom-right), control hints.
    /// The radar, crosshair and world-space read-outs live in <see cref="CombatHudOverlay"/>.
    /// </summary>
    public sealed class HudView : UiScreen
    {
        private const int MaxPips = 48, SecondarySlots = 3;
        private readonly CombatGame game;

        private TMP_Text supplyValue, levelValue, experienceText, supplyDropText;
        private UiBar experienceBar, supplyDropBar;
        private RectTransform supplyDropRow;

        private TMP_Text waveCaption, timer;
        private readonly UiBar[] wavePips = new UiBar[CombatRules.MaxWaves];

        private TMP_Text healthText;
        private UiBar healthBar;
        private RectTransform healthTicks;
        private float tickedMaxHealth = -1f, damageFlash;

        private Image weaponIcon;
        private TMP_Text weaponName, ammoText;
        private RectTransform pipRow;
        private readonly Image[] pips = new Image[MaxPips];
        private UiBar magazineBar;
        private CanvasGroup reloadHint;
        private int pipCount = -1;

        private readonly Chip[] chips = new Chip[SecondarySlots];
        private sealed class Chip { public CanvasGroup group; public Image icon; public TMP_Text number, ammo; }

        private CanvasGroup controls, banner;
        private TMP_Text bannerTitle, bannerCaption;
        private float bannerAge = 99f;
        private int bannerWave = -1;

        private string lastWeaponId;
        private int lastSupply = -1, lastLevel = -1, lastExperience = -1, lastSeconds = -1, lastHealth = -1, lastMaxHealth = -1;
        private int lastAmmo = -1, lastMagazine = -1, lastReloadTenths = -1;

        public RectTransform Overlay { get; }

        public HudView(Transform parent, CombatGame game) : base("HUD", parent)
        {
            this.game = game;
            SlideFrom = Vector2.zero;
            Shade(true, 150f, .62f);
            Shade(false, 190f, .72f);
            // World-space markers, crosshair and radar are drawn beneath the corner panels.
            Overlay = UiKit.Rect("Overlay", Root);
            BuildResources();
            BuildWaveClock();
            BuildHealth();
            BuildWeapon();
            BuildSecondarySlots();
            BuildControls();
            BuildBanner();
            game.PlayerDamaged += OnPlayerDamaged;
        }

        public void Dispose() => game.PlayerDamaged -= OnPlayerDamaged;

        private void OnPlayerDamaged(Vector3 source, float amount) => damageFlash = 1f;

        // ---------- Construction ----------

        private void Shade(bool top, float height, float alpha)
        {
            var shade = UiKit.Image(top ? "TopShade" : "BottomShade", Root, UiSprites.FadeUp, UiTheme.Alpha(UiTheme.Ink, alpha));
            var rect = shade.rectTransform;
            rect.anchorMin = new Vector2(0, top ? 1 : 0);
            rect.anchorMax = new Vector2(1, top ? 1 : 0);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(0, height);
            rect.anchoredPosition = new Vector2(0, top ? -height * .5f : height * .5f);
            if (top) rect.localScale = new Vector3(1, -1, 1);
        }

        private void BuildResources()
        {
            var block = UiKit.Node("Resources", Root, UiKit.TopLeft, UiKit.TopLeft, new Vector2(30, -22), new Vector2(380, 100));
            UiKit.Icon("SupplyIcon", block, UiKit.TopLeft, UiKit.TopLeft, new Vector2(0, -4), new Vector2(30, 30), UiGlyphs.Supply, UiTheme.Gold);
            supplyValue = Label(UiKit.Text("Supply", block, "0", UiTheme.Display, 30, UiTheme.Paper), new Vector2(38, 2), new Vector2(110, 36));
            Label(UiKit.Caption("SupplyCaption", block, "TIẾP TẾ", 11, UiTheme.Muted), new Vector2(39, -34), new Vector2(110, 16));

            var badge = UiKit.Card("LevelBadge", block, UiTheme.Alpha(UiTheme.Ink, .55f), UiTheme.Alpha(UiTheme.Gold, .7f), 5f);
            UiKit.Place(badge.rectTransform, UiKit.TopLeft, UiKit.TopLeft, new Vector2(150, -2), new Vector2(34, 34));
            levelValue = UiKit.Text("Level", badge.transform, "1", UiTheme.Display, 19, UiTheme.GoldBright, TextAlignmentOptions.Center);
            Label(UiKit.Caption("LevelCaption", block, "CẤP ĐỘ", 11, UiTheme.Muted), new Vector2(194, -1), new Vector2(120, 16));
            experienceBar = new UiBar("ExperienceBar", block, UiTheme.Paper, false, 2f);
            UiKit.Place(experienceBar.Root, UiKit.TopLeft, UiKit.TopLeft, new Vector2(194, -19), new Vector2(120, 5));
            experienceText = Label(UiKit.Text("Experience", block, "", UiTheme.Body, 12, UiTheme.Muted), new Vector2(194, -24), new Vector2(160, 16));

            supplyDropRow = UiKit.Node("SupplyDrop", block, UiKit.TopLeft, UiKit.TopLeft, new Vector2(0, -60), new Vector2(320, 30));
            Label(UiKit.Caption("Caption", supplyDropRow, "KIỆN TIẾP TẾ ĐANG RƠI", 11, UiTheme.Gold), new Vector2(0, 0), new Vector2(220, 16));
            supplyDropBar = new UiBar("Progress", supplyDropRow, UiTheme.Gold, false, 2f);
            UiKit.Place(supplyDropBar.Root, UiKit.TopLeft, UiKit.TopLeft, new Vector2(0, -19), new Vector2(184, 5));
            supplyDropText = Label(UiKit.Text("Percent", supplyDropRow, "", UiTheme.Display, 13, UiTheme.GoldBright), new Vector2(192, -12), new Vector2(60, 18));
        }

        private void BuildWaveClock()
        {
            var block = UiKit.Node("WaveClock", Root, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -16), new Vector2(320, 100));
            waveCaption = Centered(UiKit.Caption("Wave", block, "", 13, UiTheme.Gold, TextAlignmentOptions.Center), -2, 18);
            timer = Centered(UiKit.Text("Timer", block, "", UiTheme.Display, 42, UiTheme.Paper, TextAlignmentOptions.Center, 2f), -18, 52);
            UiTheme.Shadow(timer);
            const float pipWidth = 26f, gap = 6f;
            float start = -(pipWidth * wavePips.Length + gap * (wavePips.Length - 1)) * .5f;
            for (int i = 0; i < wavePips.Length; i++)
            {
                wavePips[i] = new UiBar("WavePip" + i, block, UiTheme.Paper, false, 1.5f);
                UiKit.Place(wavePips[i].Root, UiKit.TopCenter, UiKit.TopLeft, new Vector2(start + i * (pipWidth + gap), -76), new Vector2(pipWidth, 4));
            }
        }

        private void BuildHealth()
        {
            var block = UiKit.Node("Health", Root, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(30, 26), new Vector2(360, 100));
            UiKit.Icon("HeartIcon", block, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(0, 74), new Vector2(14, 14), UiGlyphs.Upgrade(Progression.UpgradeKind.Health), UiTheme.Health);
            var caption = UiKit.Caption("Caption", block, "BỘ BINH  ·  SINH LỰC", 11, UiTheme.Muted);
            UiKit.Place(caption, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(21, 73), new Vector2(260, 16));
            healthText = UiKit.Text("Value", block, "", UiTheme.Display, 44, UiTheme.Paper, TextAlignmentOptions.BottomLeft);
            UiKit.Place(healthText, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(-1, 12), new Vector2(300, 60));
            UiTheme.Shadow(healthText);
            healthBar = new UiBar("HealthBar", block, UiTheme.Health, true, 3f);
            UiKit.Place(healthBar.Root, UiKit.BottomLeft, UiKit.BottomLeft, Vector2.zero, new Vector2(300, 10));
            healthTicks = UiKit.Rect("Ticks", healthBar.Root);
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

        private void BuildSecondarySlots()
        {
            const float width = 112f, height = 34f, gap = 8f;
            var row = UiKit.Node("SecondaryWeapons", Root, UiKit.BottomRight, UiKit.BottomRight, new Vector2(-30, 168),
                new Vector2(width * SecondarySlots + gap * (SecondarySlots - 1), height));
            for (int i = 0; i < SecondarySlots; i++)
            {
                var card = UiKit.Card("Slot" + (i + 2), row, UiTheme.Alpha(UiTheme.Ink, .55f), UiTheme.Alpha(UiTheme.Paper, .14f), 5f);
                UiKit.Place(card.rectTransform, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(i * (width + gap), 0), new Vector2(width, height));
                var chip = new Chip { group = card.gameObject.AddComponent<CanvasGroup>() };
                chip.number = UiKit.Text("Number", card.transform, (i + 2).ToString(), UiTheme.Display, 13, UiTheme.Muted, TextAlignmentOptions.Center);
                UiKit.Place(chip.number, UiKit.MiddleLeft, UiKit.MiddleLeft, new Vector2(4, 0), new Vector2(16, 20));
                chip.icon = UiKit.Icon("Icon", card.transform, UiKit.MiddleLeft, UiKit.MiddleLeft, new Vector2(20, 0), new Vector2(56, 20), null, UiTheme.Alpha(UiTheme.Paper, .9f));
                chip.ammo = UiKit.Text("Ammo", card.transform, "", UiTheme.Display, 14, UiTheme.Paper, TextAlignmentOptions.MidlineRight);
                UiKit.Place(chip.ammo, UiKit.MiddleRight, UiKit.MiddleRight, new Vector2(-8, 0), new Vector2(34, 20));
                chips[i] = chip;
            }
        }

        private void BuildControls()
        {
            var row = UiKit.HintRow("Controls", Root, 12f, "W+A+S+D", "Di chuyển", "CHUỘT", "Xoay / ngắm", "CHUỘT TRÁI", "Bắn",
                "CHUỘT PHẢI", "Ngắm kỹ", "R", "Nạp đạn", "ESC", "Tạm dừng");
            // Above the corner blocks so it never overlaps the health bar or the magazine.
            UiKit.Place(row, UiKit.BottomCenter, UiKit.BottomCenter, new Vector2(0, 140), new Vector2(700, 22));
            controls = row.gameObject.AddComponent<CanvasGroup>();
        }

        private void BuildBanner()
        {
            var block = UiKit.Node("WaveBanner", Root, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -150), new Vector2(600, 120));
            banner = block.gameObject.AddComponent<CanvasGroup>();
            banner.alpha = 0;
            bannerTitle = Centered(UiKit.Text("Title", block, "", UiTheme.Display, 68, UiTheme.Paper, TextAlignmentOptions.Center, 6f), 0, 80);
            UiTheme.Shadow(bannerTitle);
            var line = UiKit.Box("Rule", block, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -94), new Vector2(64, 2), UiSprites.White, UiTheme.Gold);
            line.name = "Rule";
            bannerCaption = Centered(UiKit.Caption("Caption", block, "", 14, UiTheme.Gold, TextAlignmentOptions.Center), -102, 20);
            UiTheme.Shadow(bannerCaption);
        }

        private static TMP_Text Label(TMP_Text label, Vector2 position, Vector2 size)
        {
            UiKit.Place(label, UiKit.TopLeft, UiKit.TopLeft, position, size);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        private static TMP_Text Centered(TMP_Text label, float top, float height)
        {
            UiKit.Place(label, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, top), new Vector2(600, height));
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        // ---------- Per-frame refresh ----------

        public void Refresh(float deltaTime)
        {
            // Pause freezes the read-outs exactly where they were.
            if (game.State == CombatState.Paused) return;
            RefreshResources(deltaTime);
            RefreshWaveClock();
            RefreshHealth(deltaTime);
            RefreshWeapon(deltaTime);
            RefreshSecondarySlots();
            RefreshBanner(deltaTime);
            controls.alpha = game.State == CombatState.Playing ? Mathf.Clamp01((22f - game.RunElapsed) / 2f) * .9f : 0f;
        }

        private void RefreshResources(float deltaTime)
        {
            if (Changed(ref lastSupply, game.Currency)) supplyValue.text = game.Currency.ToString();
            if (Changed(ref lastLevel, game.Level)) levelValue.text = game.Level.ToString();
            if (Changed(ref lastExperience, game.Experience * 1000 + game.NextLevelExperience))
                experienceText.text = $"{game.Experience} / {game.NextLevelExperience} XP";
            experienceBar.Set(game.Experience / (float)Mathf.Max(1, game.NextLevelExperience), deltaTime);
            supplyDropRow.gameObject.SetActive(game.SupplyVisible);
            if (game.SupplyVisible)
            {
                supplyDropBar.Set(game.SupplyProgress, deltaTime);
                supplyDropText.text = Mathf.RoundToInt(game.SupplyProgress * 100) + "%";
            }
        }

        private void RefreshWaveClock()
        {
            int seconds = Mathf.CeilToInt(game.WaveRemaining);
            if (Changed(ref lastSeconds, seconds + game.Wave * 1000))
            {
                waveCaption.text = $"ĐỢT {game.Wave} / {game.MaxWave}";
                timer.text = $"{seconds / 60:00}:{seconds % 60:00}";
            }
            bool urgent = game.State == CombatState.Playing && seconds <= 10;
            timer.color = urgent ? Color.Lerp(UiTheme.Paper, UiTheme.GoldBright, .5f + .5f * Mathf.Sin(Time.unscaledTime * 8f)) : UiTheme.Paper;
            float progress = 1f - game.WaveRemaining / Mathf.Max(1f, game.WaveLength);
            for (int i = 0; i < wavePips.Length; i++)
            {
                int wave = i + 1;
                wavePips[i].Fill.color = wave < game.Wave ? UiTheme.Gold : UiTheme.Paper;
                wavePips[i].Set(wave < game.Wave ? 1f : wave == game.Wave ? progress : 0f, 0f);
                wavePips[i].Track.color = UiTheme.Alpha(UiTheme.Paper, wave == game.Wave ? .28f : .14f);
            }
        }

        private void RefreshHealth(float deltaTime)
        {
            float max = Mathf.Max(1f, game.MaxHealth);
            int health = Mathf.CeilToInt(game.Health), maxHealth = Mathf.CeilToInt(max);
            if (Changed(ref lastHealth, health) | Changed(ref lastMaxHealth, maxHealth))
                healthText.text = $"{health}<size=20><color=#A2AB98>  / {maxHealth}</color></size>";
            float fraction = game.Health / max;
            healthBar.Set(fraction, deltaTime);
            bool low = fraction <= .3f;
            float pulse = low ? .5f + .5f * Mathf.Sin(Time.unscaledTime * 7f) : 0f;
            damageFlash = Mathf.Max(0f, damageFlash - deltaTime * 3f);
            healthBar.Fill.color = low ? Color.Lerp(UiTheme.Danger, UiTheme.GoldBright, pulse * .25f) : UiTheme.Health;
            healthText.color = Color.Lerp(low ? Color.Lerp(UiTheme.Paper, UiTheme.Danger, .6f + .4f * pulse) : UiTheme.Paper, UiTheme.Danger, damageFlash);
            if (!Mathf.Approximately(tickedMaxHealth, max)) BuildHealthTicks(max);
        }

        private void BuildHealthTicks(float max)
        {
            tickedMaxHealth = max;
            for (int i = healthTicks.childCount - 1; i >= 0; i--) Object.Destroy(healthTicks.GetChild(i).gameObject);
            for (float hp = 25f; hp < max - 1f; hp += 25f)
            {
                var tick = UiKit.Image("Tick", healthTicks, UiSprites.White, UiTheme.Alpha(UiTheme.Ink, .75f));
                var rect = tick.rectTransform;
                rect.anchorMin = new Vector2(hp / max, 0);
                rect.anchorMax = new Vector2(hp / max, 1);
                rect.sizeDelta = new Vector2(2, 0);
                rect.anchoredPosition = Vector2.zero;
            }
        }

        private void RefreshWeapon(float deltaTime)
        {
            if (game.WeaponCount == 0) return;
            var weapon = game.WeaponAt(0);
            if (weapon.Id != lastWeaponId)
            {
                lastWeaponId = weapon.Id;
                weaponIcon.sprite = UiGlyphs.Weapon(weapon.Id);
                weaponName.text = weapon.DisplayName.ToUpperInvariant();
            }
            int magazine = Mathf.Max(1, game.MagazineSize), ammo = game.Ammo;
            bool reloading = game.ReloadRemaining > 0f;
            int reloadTenths = reloading ? Mathf.CeilToInt(game.ReloadRemaining * 10f) : -1;
            if (Changed(ref lastAmmo, ammo) | Changed(ref lastMagazine, magazine) | Changed(ref lastReloadTenths, reloadTenths))
                ammoText.text = reloading
                    ? $"<size=24><color=#E4B24A>ĐANG NẠP</color></size> {game.ReloadRemaining:0.0}<size=22>s</size>"
                    : $"{ammo}<size=22><color=#A2AB98>  / {magazine}</color></size>";
            bool lowAmmo = !reloading && ammo <= Mathf.Max(1, magazine / 4);
            ammoText.color = lowAmmo && ammo == 0 ? UiTheme.Danger : UiTheme.Paper;
            reloadHint.alpha = lowAmmo ? .65f + .35f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;

            float progress = reloading ? 1f - game.ReloadRemaining / Mathf.Max(.01f, game.ReloadDuration) : 0f;
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
                // Right-aligned so the last round sits under the ammo count.
                pips[i].rectTransform.anchoredPosition = new Vector2(width - (count - i) * (pip + gap) + gap, 0);
                pips[i].rectTransform.sizeDelta = new Vector2(pip, 8);
            }
        }

        private void RefreshSecondarySlots()
        {
            for (int i = 0; i < chips.Length; i++)
            {
                int slot = i + 1;
                var chip = chips[i];
                bool owned = slot < game.WeaponCount;
                chip.group.alpha = owned ? 1f : .3f;
                chip.icon.enabled = owned;
                if (!owned) { chip.ammo.text = "—"; chip.ammo.color = UiTheme.Muted; continue; }
                chip.icon.sprite = UiGlyphs.Weapon(game.WeaponAt(slot).Id);
                bool reloading = game.ReloadAt(slot) > 0f;
                chip.ammo.text = reloading ? "···" : game.AmmoAt(slot).ToString();
                chip.ammo.color = reloading ? UiTheme.Gold : UiTheme.Paper;
            }
        }

        private void RefreshBanner(float deltaTime)
        {
            if (game.State == CombatState.Playing && bannerWave != game.Wave)
            {
                bannerWave = game.Wave;
                bannerAge = 0f;
                bannerTitle.text = $"ĐỢT {game.Wave}";
                bannerCaption.text = game.Wave == game.MaxWave ? "ĐỢT CUỐI  ·  GIỮ VỮNG TRẬN ĐỊA" :
                    game.Wave == 1 ? "BẢO VỆ TRẠM TIẾP TẾ" : $"CÒN {game.MaxWave - game.Wave + 1} ĐỢT";
            }
            if (game.State == CombatState.Menu) bannerWave = -1;
            bannerAge += deltaTime;
            float fadeIn = Mathf.Clamp01(bannerAge / .25f), fadeOut = Mathf.Clamp01((2.6f - bannerAge) / .6f);
            banner.alpha = game.State == CombatState.Playing ? Mathf.Min(fadeIn, fadeOut) : 0f;
            float scale = 1f + (1f - fadeIn) * .12f;
            banner.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private static bool Changed(ref int cache, int value)
        {
            if (cache == value) return false;
            cache = value;
            return true;
        }
    }
}
