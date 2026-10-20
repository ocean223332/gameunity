using TMPro;
using TienTuyen.Combat;
using TienTuyen.Presentation.Interface;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Owns the combat interface canvas and switches between its screens as the
    /// run changes state. Each screen builds and refreshes itself; this class only
    /// wires callbacks, keyboard shortcuts, focus and sounds.
    /// </summary>
    [RequireComponent(typeof(CombatGame))]
    public sealed class CombatPresentation : MonoBehaviour
    {
        public TMP_FontAsset InterfaceFont;

        private CombatGame game;
        private CombatAudio combatAudio;
        private GameObject canvasObject;
        private HudView hud;
        private MenuView menu;
        private RunDialogView dialog;
        private UpgradeView upgrade;
        private ShopView shop;
        private TMP_Text debugLabel;
        private CombatState previousState = (CombatState)(-1);

        private void Start()
        {
            game = GetComponent<CombatGame>();
            combatAudio = GetComponent<CombatAudio>();
            UiTheme.Load(InterfaceFont);
            var root = BuildCanvas();

            hud = new HudView(root, game);
            hud.Overlay.gameObject.AddComponent<CombatHudOverlay>().Initialize(game, hud.Overlay, UiTheme.Display);
            menu = new MenuView(root, game, kind => { game.SelectWeapon(kind); Ui("UI_Click"); }, () => { game.BeginRun(); Ui("UI_Click"); });
            upgrade = new UpgradeView(root, game, ChooseUpgrade, kind => CombatText.UpgradePreview(game, kind));
            shop = new ShopView(root, game, Ui);
            dialog = new RunDialogView(root, game, OnPrimary, () => { game.ReturnToMenu(); Ui("UI_Click"); });
            debugLabel = UiKit.Text("DebugCheats", root, "", UiTheme.Body, 15, UiTheme.Gold, TextAlignmentOptions.BottomLeft);
            UiKit.Place(debugLabel, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(24, 165), new Vector2(650, 26));
            Refresh(0f);
            foreach (var screen in Screens) screen.Snap();
        }

        /// <summary>The whole interface's opacity; the intro title holds it hidden and fades the menu in.</summary>
        public float Opacity
        {
            set
            {
                if (canvasObject == null) return;
                var group = canvasObject.GetComponent<CanvasGroup>();
                if (group == null) group = canvasObject.AddComponent<CanvasGroup>();
                group.alpha = value;
            }
        }

        // A disabled interface (behind the intro) takes no keyboard or pad input. Enabling it enters its
        // state again, which keeps a focus that survived silently and restores one a click cleared.
        private void OnEnable()
        {
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = true;
        }

        private void OnDisable()
        {
            previousState = (CombatState)(-1);
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = false;
        }

        private void OnDestroy()
        {
            hud?.Dispose();
            if (canvasObject != null) Destroy(canvasObject);
        }

        private UiScreen[] Screens => new UiScreen[] { hud, menu, upgrade, shop, dialog };

        /// <summary>
        /// The canvas is a scene root rather than a child of the combat object, so
        /// interface transitions never count as simulation movement.
        /// </summary>
        private RectTransform BuildCanvas()
        {
            canvasObject = new GameObject("CombatInterface", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            if (EventSystem.current == null)
            {
                var events = new GameObject("CombatEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(canvasObject.transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            return canvasObject.GetComponent<RectTransform>();
        }

        // CombatGame advances simulation in Update. Refresh after all Update methods so
        // the HUD observes the completed reload state on the same rendered frame.
        private void LateUpdate()
        {
            if (game == null || hud == null) return;
            HandleShortcuts();
            Refresh(Time.unscaledDeltaTime);
        }

        private void HandleShortcuts()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (game.State == CombatState.Menu)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) { game.SelectWeapon(WeaponKind.Rifle); Ui("UI_Click"); }
                if (keyboard.digit2Key.wasPressedThisFrame) { game.SelectWeapon(WeaponKind.Smg); Ui("UI_Click"); }
                if (keyboard.digit3Key.wasPressedThisFrame) { game.SelectWeapon(WeaponKind.Shotgun); Ui("UI_Click"); }
            }
            else if (game.State == CombatState.Upgrade)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) ChooseUpgrade(0);
                if (keyboard.digit2Key.wasPressedThisFrame) ChooseUpgrade(1);
                if (keyboard.digit3Key.wasPressedThisFrame) ChooseUpgrade(2);
            }
        }

        private void Refresh(float deltaTime)
        {
            CombatState state = game.State;
            debugLabel.text = game.DebugCheatsUsed ? (game.DebugInvulnerable ? "TEST · BẤT TỬ BẬT · F6 tắt · F9 boss · F10 +1.000 tiền" : "TEST · BẤT TỬ TẮT · F6 bật · F9 boss · F10 +1.000 tiền") : "";
            hud.Show(state != CombatState.Menu && !game.CommanderCinematic);
            menu.Show(state == CombatState.Menu);
            upgrade.Show(state == CombatState.Upgrade);
            shop.Show(state == CombatState.Shop);
            dialog.Show(state == CombatState.Paused || state == CombatState.Defeat || state == CombatState.Victory);
            // After Show, so the focused button is already active and receives OnSelect.
            if (state != previousState) EnterState(state);

            if (hud.Animate(deltaTime)) hud.Refresh(deltaTime);
            if (menu.Animate(deltaTime)) menu.Refresh(deltaTime);
            if (upgrade.Animate(deltaTime) && upgrade.Shown) upgrade.Refresh();
            if (shop.Animate(deltaTime) && shop.Shown) shop.Refresh();
            dialog.Animate(deltaTime);
        }

        private void EnterState(CombatState state)
        {
            previousState = state;
            switch (state)
            {
                case CombatState.Menu: Focus(menu.StartButton); break;
                case CombatState.Playing: EventSystem.current?.SetSelectedGameObject(null); break;
                case CombatState.Upgrade: upgrade.Refresh(); Focus(upgrade.First); break;
                case CombatState.Shop: shop.Refresh(); Focus(shop.FirstOffer); break;
                default:
                    dialog.Present(CombatText.WeaponName(game.SelectedWeapon));
                    Focus(dialog.Primary);
                    break;
            }
        }

        private void ChooseUpgrade(int index)
        {
            if (game.ChooseUpgrade(index)) Ui("Upgrade_Select");
        }

        private void OnPrimary()
        {
            if (game.State == CombatState.Paused) game.TogglePause();
            else game.Restart();
            Ui("UI_Click");
        }

        private void Ui(string clipName) => combatAudio?.PlayUi(clipName);

        private static void Focus(UiButton button)
        {
            if (button != null) EventSystem.current?.SetSelectedGameObject(button.gameObject);
        }
    }
}
