using TMPro;
using TienTuyen.Combat;
using TienTuyen.Content;
using TienTuyen.Progression;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace TienTuyen.Presentation
{
    /// <summary>Runtime-authored, keyboard-accessible HUD for the six-wave vertical-slice combat loop.</summary>
    [RequireComponent(typeof(CombatGame))]
    public sealed class CombatPresentation : MonoBehaviour
    {
        public TMP_FontAsset InterfaceFont;
        private CombatGame game;
        private CombatAudio combatAudio;
        private RectTransform canvasRoot, hud, overlay, menu, dialog, upgradePanel, shopPanel;
        private TMP_Text health, wave, resources, weapon, reload, instructions, selectedWeapon;
        private UnityEngine.UI.Image weaponIcon;
        private readonly System.Collections.Generic.Dictionary<string, Sprite> weaponIcons = new System.Collections.Generic.Dictionary<string, Sprite>();
        private string lastWeaponIcon;
        private TMP_Text dialogTitle, dialogBody;
        private TMP_Text upgradeTitle, shopWallet, shopPreview, shopMessage, rerollLabel, loadout;
        private readonly UnityEngine.UI.Button[] upgradeButtons = new UnityEngine.UI.Button[3];
        private readonly TMP_Text[] upgradeLabels = new TMP_Text[3];
        private readonly UnityEngine.UI.Button[] offerButtons = new UnityEngine.UI.Button[4];
        private readonly TMP_Text[] offerLabels = new TMP_Text[4];
        private readonly UnityEngine.UI.Button[] lockButtons = new UnityEngine.UI.Button[4];
        private readonly TMP_Text[] lockLabels = new TMP_Text[4];
        private readonly UnityEngine.UI.Button[] sellButtons = new UnityEngine.UI.Button[4];
        private readonly TMP_Text[] sellLabels = new TMP_Text[4];
        private UnityEngine.UI.Button buyButton, rerollButton, bandageButton, combineButton, nextWaveButton;
        private int selectedOffer;
        private UnityEngine.UI.Image healthFill, reloadFill;
        private UnityEngine.UI.Button primaryButton, secondaryButton, startButton;
        private TMP_Text primaryLabel, secondaryLabel;
        private CombatState previousState = (CombatState)(-1);
        private static readonly Color Ink = Hex("#202A25"), Paper = Hex("#EEE7D4"), Gold = Hex("#E7BD62");
        private static readonly Color Muted = Hex("#BBC3A8"), Olive = Hex("#354638");

        private void Start()
        {
            game = GetComponent<CombatGame>();
            combatAudio = GetComponent<CombatAudio>();
            BuildInterface();
            Refresh();
        }

        private void Update()
        {
            if (game == null) return;
            if (game.State == CombatState.Menu && Keyboard.current != null)
            {
                if (Keyboard.current.digit1Key.wasPressedThisFrame) game.SelectWeapon(WeaponKind.Rifle);
                if (Keyboard.current.digit2Key.wasPressedThisFrame) game.SelectWeapon(WeaponKind.Smg);
                if (Keyboard.current.digit3Key.wasPressedThisFrame) game.SelectWeapon(WeaponKind.Shotgun);
            }
            Refresh();
        }

        private void BuildInterface()
        {
            var canvasObject = new GameObject("CombatInterface", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasRoot = canvasObject.GetComponent<RectTransform>();
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            if (EventSystem.current == null)
            {
                var events = new GameObject("CombatEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            hud = Rect("HUD", canvasRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var top = Panel("StatusBar", hud, new Vector2(0, 1), Vector2.one, new Vector2(16, -80), new Vector2(-16, -16), Ink);
            health = Label("Health", top, "", 21, new Vector2(16, 29), new Vector2(270, 58));
            Bar("HealthBar", top, new Vector2(16, 14), new Vector2(245, 20), out healthFill, Gold);
            wave = Label("Wave", top, "", 23, new Vector2(330, 11), new Vector2(700, 58));
            wave.alignment = TextAlignmentOptions.Center;
            resources = Label("Resources", top, "", 17, new Vector2(780, 7), new Vector2(1210, 58));
            resources.alignment = TextAlignmentOptions.MidlineRight;

            var bottom = Panel("LoadoutBar", hud, Vector2.zero, new Vector2(1, 0), new Vector2(16, 16), new Vector2(-16, 85), Ink);
            weapon = Label("Weapon", bottom, "", 19, new Vector2(15, 27), new Vector2(250, 59));
            var iconRect = Rect("WeaponIcon", bottom, Vector2.zero, Vector2.zero, new Vector2(258, 11), new Vector2(315, 67));
            weaponIcon = iconRect.gameObject.AddComponent<UnityEngine.UI.Image>();
            weaponIcon.preserveAspect = true;
            weaponIcon.color = Color.white;
            reload = Label("Ammo", bottom, "", 15, new Vector2(15, 5), new Vector2(315, 29));
            Bar("ReloadBar", bottom, new Vector2(330, 19), new Vector2(438, 27), out reloadFill, Gold);
            loadout = Label("WeaponSlots", bottom, "", 13, new Vector2(463, 8), new Vector2(756, 64));
            loadout.color = Muted;
            instructions = Label("Controls", bottom, "WASD / phím mũi tên: di chuyển\nTự ngắm · tự bắn · tự nạp     ESC: dừng", 15,
                new Vector2(770, 10), new Vector2(1210, 59));
            instructions.alignment = TextAlignmentOptions.MidlineRight;

            overlay = Panel("Overlay", canvasRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.06f, 0.09f, 0.07f, 0.87f));
            overlay.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            menu = Panel("MainMenu", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-390, -281), new Vector2(390, 281), Ink);
            Panel("MenuAccent", menu, new Vector2(0, 1), Vector2.one, new Vector2(0, -5), Vector2.zero, Gold);
            Label("Eyebrow", menu, "TRẠM TIẾP TẾ HƯ CẤU  /  TRƯỜNG SƠN 1972", 16, new Vector2(40, 502), new Vector2(740, 531)).color = Gold;
            Label("Title", menu, "TIỀN TUYẾN", 55, new Vector2(36, 423), new Vector2(743, 499));
            Label("Description", menu, "Giữ khoảng cách. Tận dụng vật cản. Sống sót qua 6 đợt.", 20, new Vector2(40, 379), new Vector2(740, 424));
            Label("ChooseTitle", menu, "BỘ BINH  /  CHỌN VŨ KHÍ KHỞI ĐẦU", 16, new Vector2(40, 331), new Vector2(740, 363)).color = Muted;
            Button("RifleChoice", menu, "1  SÚNG TRƯỜNG\nTầm xa · nhịp bắn ổn định", new Vector2(40, 240), new Vector2(270, 325), () => { game.SelectWeapon(WeaponKind.Rifle); Ui("UI_Click"); });
            Button("SmgChoice", menu, "2  TIỂU LIÊN\nTầm gần · bắn nhanh", new Vector2(276, 240), new Vector2(505, 325), () => { game.SelectWeapon(WeaponKind.Smg); Ui("UI_Click"); });
            Button("ShotgunChoice", menu, "3  TẢN ĐẠN\nGần · sáu viên mỗi phát", new Vector2(511, 240), new Vector2(740, 325), () => { game.SelectWeapon(WeaponKind.Shotgun); Ui("UI_Click"); });
            selectedWeapon = Label("SelectedWeapon", menu, "", 17, new Vector2(40, 200), new Vector2(740, 235));
            selectedWeapon.color = Gold;
            startButton = Button("Start", menu, "BẮT ĐẦU", new Vector2(40, 130), new Vector2(740, 190), () => { game.BeginRun(); Ui("UI_Click"); }, true);
            Label("HowToPlay", menu, "WASD / phím mũi tên để di chuyển. Súng tự ngắm, bắn và nạp.\nNé đạn sáng; vòng ngoài rìa báo địch sắp xuất hiện. ESC tạm dừng.", 17,
                new Vector2(40, 60), new Vector2(740, 119));
            Label("Scope", menu, "VERTICAL SLICE P2  ·  Art/audio vertical slice  ·  6 đợt / 3 vũ khí", 13,
                new Vector2(40, 18), new Vector2(740, 45)).color = Muted;

            dialog = Panel("RunDialog", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-330, -218), new Vector2(330, 218), Ink);
            Panel("DialogAccent", dialog, new Vector2(0, 1), Vector2.one, new Vector2(0, -5), Vector2.zero, Gold);
            dialogTitle = Label("Title", dialog, "", 35, new Vector2(36, 339), new Vector2(624, 400));
            dialogBody = Label("RunStats", dialog, "", 20, new Vector2(36, 140), new Vector2(624, 328));
            dialogBody.alignment = TextAlignmentOptions.TopLeft;
            primaryButton = Button("Primary", dialog, "", new Vector2(36, 77), new Vector2(624, 132), OnPrimary, true);
            primaryLabel = primaryButton.GetComponentInChildren<TMP_Text>();
            secondaryButton = Button("Secondary", dialog, "VỀ MENU", new Vector2(36, 17), new Vector2(624, 66), () => { game.ReturnToMenu(); Ui("UI_Click"); });
            secondaryLabel = secondaryButton.GetComponentInChildren<TMP_Text>();

            upgradePanel = Panel("UpgradeChoices", overlay, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(-470, -230), new Vector2(470, 230), Ink);
            Panel("UpgradeAccent", upgradePanel, new Vector2(0, 1), Vector2.one, new Vector2(0, -5), Vector2.zero, Gold);
            upgradeTitle = Label("UpgradeTitle", upgradePanel, "", 31, new Vector2(28, 394), new Vector2(912, 444));
            Label("UpgradeHint", upgradePanel, "Chọn một nâng cấp. Trận tiếp theo bắt đầu sau cửa hàng.", 18,
                new Vector2(28, 350), new Vector2(912, 388));
            for (int i = 0; i < upgradeButtons.Length; i++)
            {
                int choice = i;
                int x = 28 + i * 300;
                upgradeButtons[i] = Button("Upgrade" + i, upgradePanel, "", new Vector2(x, 110),
                    new Vector2(x + 280, 330), () => { game.ChooseUpgrade(choice); Ui("Upgrade_Select"); });
                upgradeLabels[i] = upgradeButtons[i].GetComponentInChildren<TMP_Text>();
            }

            shopPanel = Panel("Shop", overlay, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(-540, -315), new Vector2(540, 315), Ink);
            Panel("ShopAccent", shopPanel, new Vector2(0, 1), Vector2.one, new Vector2(0, -5), Vector2.zero, Gold);
            Label("ShopTitle", shopPanel, "TRẠM TIẾP TẾ", 32, new Vector2(28, 567), new Vector2(600, 612));
            shopWallet = Label("Wallet", shopPanel, "", 20, new Vector2(630, 567), new Vector2(1052, 612));
            shopWallet.alignment = TextAlignmentOptions.MidlineRight;
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                int x = 28 + i * 258;
                offerButtons[i] = Button("Offer" + i, shopPanel, "", new Vector2(x, 356),
                    new Vector2(x + 238, 550), () => { selectedOffer = slot; Ui("UI_Click"); RefreshShop(); });
                offerLabels[i] = offerButtons[i].GetComponentInChildren<TMP_Text>();
                lockButtons[i] = Button("Lock" + i, shopPanel, "", new Vector2(x, 320),
                    new Vector2(x + 238, 350), () => { game.SetOfferLocked(slot, !game.Shop.IsLocked(slot)); Ui("UI_Click"); RefreshShop(); });
                lockLabels[i] = lockButtons[i].GetComponentInChildren<TMP_Text>();
                sellButtons[i] = Button("Sell" + i, shopPanel, "", new Vector2(x, 70),
                    new Vector2(x + 238, 115), () => { game.SellWeapon(slot, out _); Ui("Buy_Success"); RefreshShop(); });
                sellLabels[i] = sellButtons[i].GetComponentInChildren<TMP_Text>();
                sellLabels[i].fontSize = 15;
                sellLabels[i].enableAutoSizing = true;
                sellLabels[i].fontSizeMin = 12;
                sellLabels[i].fontSizeMax = 15;
                sellLabels[i].textWrappingMode = TextWrappingModes.NoWrap;
            }
            shopPreview = Label("Preview", shopPanel, "", 17, new Vector2(28, 220), new Vector2(790, 309));
            buyButton = Button("Buy", shopPanel, "MUA Ô ĐANG CHỌN", new Vector2(800, 246),
                new Vector2(1052, 307), () => { var result = game.PurchaseOffer(selectedOffer); shopMessage.text = PurchaseMessage(result); Ui(result == PurchaseResult.Bought ? "Buy_Success" : "Buy_Fail"); RefreshShop(); }, true);
            rerollButton = Button("Reroll", shopPanel, "", new Vector2(800, 183),
                new Vector2(1052, 239), () => { bool ok = game.RerollShop(); shopMessage.text = ok ? "Đã đổi hàng." : "Không thể đổi hàng."; Ui("Reroll"); RefreshShop(); });
            rerollLabel = rerollButton.GetComponentInChildren<TMP_Text>();
            bandageButton = Button("Bandage", shopPanel, "BĂNG BÓ · 15", new Vector2(28, 155),
                new Vector2(276, 205), () => { bool ok = game.BuyBandage(); shopMessage.text = ok ? "Đã hồi 25 HP." : "Không thể băng bó."; Ui(ok ? "Buy_Success" : "Buy_Fail"); RefreshShop(); });
            combineButton = Button("Combine", shopPanel, "GHÉP VŨ KHÍ TRÙNG", new Vector2(284, 155),
                new Vector2(532, 205), () => { bool ok = game.CombineFirstMatchingWeapons(); shopMessage.text = ok ? "Đã ghép vũ khí." : "Không có cặp phù hợp."; Ui(ok ? "Buy_Success" : "Buy_Fail"); RefreshShop(); });
            shopMessage = Label("ShopMessage", shopPanel, "", 16, new Vector2(548, 155), new Vector2(790, 205));
            Label("LoadoutTitle", shopPanel, "BỐN Ô VŨ KHÍ · BÁN NHẬN NỬA GIÁ ĐÃ TRẢ", 16,
                new Vector2(28, 121), new Vector2(1052, 148)).color = Muted;
            nextWaveButton = Button("NextWave", shopPanel, "SANG ĐỢT TIẾP", new Vector2(28, 8),
                new Vector2(1052, 62), () => { game.ContinueWave(); Ui("UI_Click"); }, true);
        }

        private void Refresh()
        {
            bool playing = game.State == CombatState.Playing;
            bool isMenu = game.State == CombatState.Menu;
            overlay.gameObject.SetActive(!playing);
            hud.gameObject.SetActive(!isMenu);
            menu.gameObject.SetActive(isMenu);
            dialog.gameObject.SetActive(game.State == CombatState.Paused || game.State == CombatState.Defeat || game.State == CombatState.Victory);
            upgradePanel.gameObject.SetActive(game.State == CombatState.Upgrade);
            shopPanel.gameObject.SetActive(game.State == CombatState.Shop);
            health.text = $"BỘ BINH   {Mathf.CeilToInt(game.Health)} / {Mathf.CeilToInt(game.MaxHealth)} HP";
            healthFill.fillAmount = game.Health / Mathf.Max(1, game.MaxHealth);
            int seconds = Mathf.CeilToInt(game.WaveRemaining);
            wave.text = $"ĐỢT {game.Wave} / {game.MaxWave}    {seconds / 60:00}:{seconds % 60:00}";
            string supply = game.SupplyVisible ? $"\nKIỆN TIẾP TẾ  {game.SupplyProgress * 100:0}%" : "";
            resources.text = $"TIẾP TẾ  {game.Currency}\nCẤP {game.Level}  ·  XP {game.Experience}/{game.NextLevelExperience}{supply}";
            string weaponName = game.SelectedWeapon == WeaponKind.Rifle ? "SÚNG TRƯỜNG" : game.SelectedWeapon == WeaponKind.Smg ? "TIỂU LIÊN" : "SÚNG TẢN ĐẠN";
            weapon.text = game.WeaponCount > 0 ? $"I   {game.WeaponAt(0).DisplayName.ToUpperInvariant()}" : "I   —";
            string iconName = game.SelectedWeapon == WeaponKind.Smg ? "SMG" : game.SelectedWeapon == WeaponKind.Shotgun ? "Shotgun" : "Rifle";
            if (weaponIcon != null && iconName != lastWeaponIcon)
            {
                if (!weaponIcons.TryGetValue(iconName, out var sprite))
                {
                    var texture = Resources.Load<Texture2D>("WeaponIcons/" + iconName);
                    if (texture != null)
                    {
                        sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), texture.width);
                        weaponIcons[iconName] = sprite;
                    }
                }
                weaponIcon.sprite = sprite;
                lastWeaponIcon = iconName;
            }
            loadout.text = "";
            for (int slot = 1; slot < ProgressionRun.MaxWeaponSlots; slot++)
                loadout.text += slot < game.WeaponCount ? $"{slot + 1}. {game.WeaponAt(slot).DisplayName}  {game.AmmoAt(slot)}\n" : $"{slot + 1}. —\n";
            bool reloading = game.ReloadRemaining > 0;
            reload.text = reloading ? $"ĐANG NẠP  {game.ReloadRemaining:0.0}s" : $"ĐẠN  {game.Ammo} / {game.MagazineSize}  ·  TỰ ĐỘNG";
            reloadFill.fillAmount = reloading ? 1 - game.ReloadRemaining / Mathf.Max(0.01f, game.ReloadDuration) : 1;
            selectedWeapon.text = $"ĐÃ CHỌN: {weaponName}   ·   {Mathf.CeilToInt(game.MaxHealth)} HP";
            if (game.State == CombatState.Upgrade) RefreshUpgrades();
            if (game.State == CombatState.Shop) RefreshShop();
            if (previousState == game.State) return;
            previousState = game.State;
            if (isMenu) { Select(startButton); return; }
            if (playing) { EventSystem.current?.SetSelectedGameObject(null); return; }
            if (game.State == CombatState.Upgrade) { Select(upgradeButtons[0]); return; }
            if (game.State == CombatState.Shop)
            {
                selectedOffer = 0;
                shopMessage.text = "Chọn hàng để xem chỉ số và giá.";
                RefreshShop();
                Select(offerButtons[0]);
                return;
            }
            primaryLabel.text = game.State == CombatState.Paused ? "TIẾP TỤC" : "THỬ LẠI";
            secondaryLabel.text = "VỀ MENU";
            if (game.State == CombatState.Paused)
            {
                dialogTitle.text = "TẠM DỪNG";
                dialogBody.text = "Thời gian chiến đấu đã dừng.\n\nWASD / phím mũi tên: di chuyển\nESC: tiếp tục\nVật cản chặn di chuyển và đường đạn.";
            }
            else
            {
                dialogTitle.text = game.State == CombatState.Victory ? "GIỮ VỮNG TRẬN ĐỊA" : game.State == CombatState.Defeat ? "LƯỢT CHƠI KẾT THÚC" : $"HOÀN THÀNH ĐỢT {game.Wave}";
                dialogBody.text = $"Bộ binh · {weaponName.ToLowerInvariant()}\nĐợt {game.Wave}/{game.MaxWave}   ·   Thời gian {game.RunElapsed:0.0}s\nHạ địch {game.Kills}   ·   Sát thương {game.DamageDealt:0}\nTiếp tế {game.Currency}   ·   Cấp {game.Level}\nSeed: {game.Seed}";
            }
            Select(primaryButton);
        }

        private void OnPrimary()
        {
            if (game.State == CombatState.Paused) game.TogglePause();
            else game.Restart();
            Ui("UI_Click");
        }

        private void RefreshUpgrades()
        {
            upgradeTitle.text = $"NÂNG CẤP · CÒN {game.Run.PendingUpgrades} LƯỢT";
            for (int i = 0; i < upgradeButtons.Length; i++)
            {
                bool offered = i < game.UpgradeChoiceCount;
                upgradeButtons[i].interactable = offered;
                if (!offered) { upgradeLabels[i].text = "—"; continue; }
                UpgradeKind kind = game.Run.CurrentChoices[i];
                upgradeLabels[i].text = $"{UpgradeName(kind)}\n\n{UpgradePreview(kind)}\n\nCHỌN {i + 1}";
            }
        }

        private void RefreshShop()
        {
            if (game.Shop == null) return;
            shopWallet.text = $"{game.Currency} TIẾP TẾ · {Mathf.CeilToInt(game.Health)}/{Mathf.CeilToInt(game.MaxHealth)} HP";
            bool hasUnlocked = false;
            for (int i = 0; i < offerButtons.Length; i++)
            {
                ShopOffer offer = game.Shop.OfferAt(i);
                bool locked = game.Shop.IsLocked(i);
                hasUnlocked |= !locked;
                offerButtons[i].interactable = offer != null;
                offerButtons[i].GetComponent<UnityEngine.UI.Image>().color = i == selectedOffer ? Gold : Olive;
                offerLabels[i].color = i == selectedOffer ? Ink : Paper;
                offerLabels[i].text = offer == null ? $"Ô {i + 1}\n\nĐÃ BÁN · TRỐNG" :
                    $"Ô {i + 1} · {(offer.Kind == OfferKind.Weapon ? "VŨ KHÍ" : "VẬT PHẨM")}\n\n{OfferName(offer)}\nBẬC {offer.Tier} · {offer.Price} TIẾP TẾ";
                lockButtons[i].interactable = offer != null;
                lockLabels[i].text = locked ? "MỞ KHÓA" : "KHÓA Ô";
                bool owned = i < game.WeaponCount;
                sellButtons[i].interactable = owned && game.WeaponCount > 1;
                sellLabels[i].text = owned ? $"BÁN {game.WeaponAt(i).DisplayName.ToUpperInvariant()} · +{game.Run.Weapons[i].PaidPrice / 2}" : $"Ô VŨ KHÍ {i + 1} · TRỐNG";
            }
            ShopOffer selected = game.Shop.OfferAt(selectedOffer);
            buyButton.interactable = selected != null && game.Currency >= selected.Price &&
                (selected.Kind != OfferKind.Weapon || game.WeaponCount < ProgressionRun.MaxWeaponSlots);
            shopPreview.text = selected == null ? "Chọn một ô có hàng để xem chỉ số." : OfferPreview(selected);
            rerollLabel.text = $"ĐỔI HÀNG · {game.Shop.NextRerollPrice}";
            rerollButton.interactable = hasUnlocked && game.Currency >= game.Shop.NextRerollPrice;
            bandageButton.interactable = !game.BandageUsed && game.Health < game.MaxHealth && game.Currency >= 15;
            combineButton.interactable = HasMatchingWeapons();
        }

        private bool HasMatchingWeapons()
        {
            for (int i = 0; i < game.WeaponCount; i++)
                for (int j = i + 1; j < game.WeaponCount; j++)
                    if (game.Run.Weapons[i].Id == game.Run.Weapons[j].Id &&
                        game.Run.Weapons[i].Tier == game.Run.Weapons[j].Tier && game.Run.Weapons[i].Tier < 3) return true;
            return false;
        }

        private string OfferPreview(ShopOffer offer)
        {
            if (offer.Kind == OfferKind.Weapon)
            {
                WeaponDefinition weaponDefinition = ContentCatalog.P2.GetWeapon(offer.Id);
                float tierFactor = offer.Tier == 1 ? 1f : offer.Tier == 2 ? 1.35f : 1.8f;
                float dps = weaponDefinition.DamagePerHit * weaponDefinition.PelletsPerShot /
                    weaponDefinition.ShotCooldownSeconds * tierFactor * game.Stats.DamageFactor * (1 + game.Stats.AttackSpeedBonus);
                return $"{weaponDefinition.DisplayName} · bậc {offer.Tier}\n" +
                    $"DPS vũ khí mới: 0 → {dps:0.0}   ·   băng đạn: 0 → {weaponDefinition.MagazineSize}\n" +
                    $"Ô vũ khí: {game.WeaponCount} → {game.WeaponCount + 1}/4   ·   Tiếp tế: {game.Currency} → {game.Currency - offer.Price}";
            }
            ProgressionStats before = game.Stats, after = game.Run.PreviewStats(offer.Modifier);
            return $"{OfferName(offer)} · lần {game.Run.PassiveCount(offer.Id) + 1}/{offer.StackCap}\n" +
                StatsBeforeAfter(before, after) + $"\nTiếp tế: {game.Currency} → {game.Currency - offer.Price}";
        }

        private string UpgradePreview(UpgradeKind kind)
        {
            ProgressionStats before = game.Stats, after = game.Run.PreviewStats(ProgressionRules.Upgrade(kind));
            return StatsBeforeAfter(before, after);
        }

        private static string StatsBeforeAfter(ProgressionStats before, ProgressionStats after)
        {
            string lines = "";
            if (before.MaxHealth != after.MaxHealth) lines += $"HP tối đa {before.MaxHealth + CombatRules.PlayerMaxHealth - 100f:0} → {after.MaxHealth + CombatRules.PlayerMaxHealth - 100f:0}\n";
            if (before.DamageFactor != after.DamageFactor) lines += $"Sát thương {before.DamageFactor * 100:0}% → {after.DamageFactor * 100:0}%\n";
            if (before.AttackSpeedBonus != after.AttackSpeedBonus) lines += $"Tốc bắn {before.AttackSpeedBonus * 100:0}% → {after.AttackSpeedBonus * 100:0}%\n";
            if (before.Armor != after.Armor) lines += $"Giáp {before.Armor:0} → {after.Armor:0}\n";
            if (before.SpeedFactor != after.SpeedFactor) lines += $"Tốc độ {before.SpeedFactor * 100:0}% → {after.SpeedFactor * 100:0}%\n";
            if (before.Regen != after.Regen) lines += $"Hồi HP {before.Regen:0.0}/s → {after.Regen:0.0}/s\n";
            if (before.PickupRadius != after.PickupRadius) lines += $"Tầm nhặt {before.PickupRadius:0.0} → {after.PickupRadius:0.0}\n";
            if (before.ReloadFactor != after.ReloadFactor) lines += $"Thời gian nạp {before.ReloadFactor * 100:0}% → {after.ReloadFactor * 100:0}%\n";
            if (before.RangeFactor != after.RangeFactor) lines += $"Tầm bắn {before.RangeFactor * 100:0}% → {after.RangeFactor * 100:0}%\n";
            if (before.MagazineFactor != after.MagazineFactor) lines += $"Băng đạn {before.MagazineFactor * 100:0}% → {after.MagazineFactor * 100:0}%\n";
            if (before.IncomeFactor != after.IncomeFactor) lines += $"Thu nhập {before.IncomeFactor * 100:0}% → {after.IncomeFactor * 100:0}%\n";
            if (before.ExperienceFactor != after.ExperienceFactor) lines += $"Kinh nghiệm {before.ExperienceFactor * 100:0}% → {after.ExperienceFactor * 100:0}%\n";
            return lines.Length == 0 ? "Chỉ số đã đạt giới hạn." : lines.TrimEnd();
        }

        private static string UpgradeName(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.Health: return "THỂ LỰC";
                case UpgradeKind.Damage: return "SÁT THƯƠNG";
                case UpgradeKind.AttackSpeed: return "TỐC BẮN";
                case UpgradeKind.Armor: return "GIÁP";
                case UpgradeKind.Speed: return "TỐC ĐỘ";
                case UpgradeKind.Regen: return "HỒI PHỤC";
                default: return kind.ToString();
            }
        }

        private static string OfferName(ShopOffer offer)
        {
            if (offer.Kind == OfferKind.Weapon) return ContentCatalog.P2.GetWeapon(offer.Id).DisplayName;
            return PassiveCatalog.P2.TryGet(offer.Id, out var passive) ? passive.DisplayName : offer.Id;
        }

        private static string PurchaseMessage(PurchaseResult result)
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

        private void Ui(string clipName) => combatAudio?.PlayUi(clipName);

        private static void Select(UnityEngine.UI.Button button) => EventSystem.current?.SetSelectedGameObject(button.gameObject);

        private UnityEngine.UI.Button Button(string name, Transform parent, string caption, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action, bool primary = false)
        {
            var rect = Panel(name, parent, Vector2.zero, Vector2.zero, min, max, primary ? Gold : Olive);
            var graphic = rect.GetComponent<UnityEngine.UI.Image>();
            graphic.raycastTarget = true;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = graphic;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.selectedColor = new Color(1.3f, 1.3f, 1.3f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            button.colors = colors;
            button.onClick.AddListener(action);
            var label = Label("Label", rect, caption, 19, new Vector2(10, 3), max - min - new Vector2(10, 3));
            label.alignment = TextAlignmentOptions.Center;
            label.color = primary ? Ink : Paper;
            return button;
        }

        private TMP_Text Label(string name, Transform parent, string text, float size, Vector2 min, Vector2 max)
        {
            var rect = Rect(name, parent, Vector2.zero, Vector2.zero, min, max);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = InterfaceFont;
            label.text = text;
            label.fontSize = size;
            label.color = Paper;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Truncate;
            label.raycastTarget = false;
            return label;
        }

        private static void Bar(string name, Transform parent, Vector2 min, Vector2 max, out UnityEngine.UI.Image fill, Color color)
        {
            var track = Panel(name, parent, Vector2.zero, Vector2.zero, min, max, Olive);
            var inner = Panel("Fill", track, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, color);
            fill = inner.GetComponent<UnityEngine.UI.Image>();
            fill.type = UnityEngine.UI.Image.Type.Filled;
            fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
        }

        private static RectTransform Panel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 min, Vector2 max, Color color)
        {
            var rect = Rect(name, parent, anchorMin, anchorMax, min, max);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = (anchorMin + anchorMax) * 0.5f;
            rect.offsetMin = min;
            rect.offsetMax = max;
            return rect;
        }

        private static Color Hex(string value) { ColorUtility.TryParseHtmlString(value, out var color); return color; }
    }
}





