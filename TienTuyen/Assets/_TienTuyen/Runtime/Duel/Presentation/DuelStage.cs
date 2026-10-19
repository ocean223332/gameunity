using System;
using System.Collections;
using TienTuyen.Duel.Net;
using TienTuyen.Presentation;
using TienTuyen.Presentation.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// Owns the duel's look: builds the arena, its light and the interface canvas, and switches between the lobby,
    /// the HUD and the match result. The connection and the match plug in through <see cref="Lobby"/> and
    /// <see cref="Match"/>; until something sets them the stage runs a <see cref="FakeDuelLobbyHost"/>, so the
    /// screens can be exercised alone.
    /// The main menu's 1v1 entry opens it in a scene of its own made at runtime (<see cref="Enter"/>), so the build
    /// keeps its single scene; "back" loads CombatSpike again, which starts at its menu.
    /// Whatever moves the duel's camera must do it in Update with an execution order below 0, as this stage does:
    /// FloraScatter picks the plants to draw for the camera's pose in its own Update, so a camera moved later (in
    /// LateUpdate) shows the previous view's plants for a frame on every cut, a spawn or round reset included.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class DuelStage : MonoBehaviour
    {
        public const string SceneName = "DuelArena", MenuScene = "CombatSpike";

        /// <summary>Fallback when the baked interface fonts are missing (as CombatPresentation.InterfaceFont).</summary>
        public TMP_FontAsset InterfaceFont;
        /// <summary>Starts in a scripted match instead of the lobby, for looking at the HUD alone.</summary>
        public bool DemoMatch;
        /// <summary>While true the stage moves the camera itself: orbiting the arena in the lobby, at the local spawn in a match.</summary>
        public bool DriveCamera = true;
        /// <summary>The slot whose spawn the stage's match camera stands at.</summary>
        public int LocalSlot;

        /// <summary>What the lobby drives; a fake until the connection layer sets its own.</summary>
        public IDuelLobbyHost Lobby { get; set; }
        /// <summary>The match the HUD shows; null shows the lobby.</summary>
        public IDuelHudState Match { get; set; }
        /// <summary>
        /// The running duel. While it has a state the stage plays it in first person (both bodies, the eye and the
        /// weapon in hand) and the camera is the eye's, whatever <see cref="DriveCamera"/> says; null ends that.
        /// </summary>
        public DuelSession Session { get; set; }
        /// <summary>The local player's look while a duel is played (null otherwise): what the input sends as aim.</summary>
        public IDuelAimSource Aim => play?.Aim;

        /// <summary>
        /// Raised from every stage's Awake, before its Start builds anything, so the connection layer can set
        /// <see cref="Lobby"/>, <see cref="Match"/> and <see cref="Session"/> and subscribe to <see cref="Built"/>.
        /// Single consumer by design (the duel's play driver): assign it, never +=, since a static event survives a
        /// Play without a domain reload and would stack handlers. A second assignment silently replaces the first, so
        /// a second consumer needs a different mechanism, not another assignment.
        /// </summary>
        public static Action<DuelStage> Created;

        /// <summary>The result screen's "back to the lobby": the match owner ends the match (Match = null).</summary>
        public event Action LeaveMatchRequested;

        /// <summary>
        /// Raised once the arena and the interface exist (after the previous scene's unload when entered from the
        /// menu). Mark the arena ready to the match here, not at <see cref="Enter"/>: the views are null until then.
        /// </summary>
        public event Action Built;
        /// <summary>True once <see cref="Built"/> has been raised; check it before subscribing late.</summary>
        public bool IsBuilt => built;

        public Camera View { get; private set; }
        public DuelLobbyView LobbyView { get; private set; }
        public DuelHudView HudView { get; private set; }
        public DuelResultView ResultView { get; private set; }

        private FakeDuelLobbyHost fake;
        private FakeDuelHudState fakeMatch;
        private DuelArenaLighting lighting;
        private GameObject canvasObject;
        private bool built;
        private DuelPlayView play;
        private DuelSession playing;
        private float orbit = -35f;
        private Screen shown = (Screen)(-1);
        private enum Screen { Lobby, Hud, Result }

        // ---------- Entry from the main menu ----------

        // Writes into the single-player main menu, which cannot reference the duel: MenuView builds its duel button
        // from this. By assignment, so a Play without a domain reload never stacks a second entry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => MenuView.DuelEntry = Enter;

        /// <summary>
        /// Leaves the current scene for a new duel scene: the new scene is made and made active first (Unity keeps
        /// at least one scene loaded, and new objects land in the active one), then the old one is unloaded. The
        /// stage builds only once that unload and its assets are gone, so the station and the arena never sit in
        /// memory together.
        /// </summary>
        public static void Enter()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.CreateScene(SceneName + "_" + Time.frameCount);
            SceneManager.SetActiveScene(scene);
            var stage = new GameObject("DuelStage").AddComponent<DuelStage>();
            stage.InterfaceFont = UiTheme.Display;
            ClearPersistent();
            if (previous.IsValid() && previous.isLoaded) pendingUnload = SceneManager.UnloadSceneAsync(previous);
        }

        private static AsyncOperation pendingUnload;

        /// <summary>Back to the title screen: CombatSpike loads fresh at its menu (the intro only plays on first start).</summary>
        public static void ReturnToMenu() => SceneManager.LoadScene(MenuScene, LoadSceneMode.Single);

        /// <summary>
        /// Removes event systems and audio listeners that outlived their scene in DontDestroyOnLoad: a second event
        /// system silently takes the interface's input, a second listener spams warnings.
        /// </summary>
        private static void ClearPersistent()
        {
            var probe = new GameObject("PersistentProbe");
            DontDestroyOnLoad(probe);
            foreach (var root in probe.scene.GetRootGameObjects())
            {
                if (root == probe) continue;
                foreach (var events in root.GetComponentsInChildren<EventSystem>(true)) Destroy(events.gameObject);
                foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) Destroy(listener);
            }
            Destroy(probe);
        }

        // ---------- Build ----------

        private void Awake() => Created?.Invoke(this);

        private IEnumerator Start()
        {
            if (pendingUnload != null)
            {
                yield return pendingUnload;
                pendingUnload = null;
                yield return Resources.UnloadUnusedAssets();
            }
            UiTheme.Load(InterfaceFont);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            View = SetupCamera();
            lighting = new DuelArenaLighting(transform, View);
            // Blood decals (CombatBloodEffects) project only onto renderers carrying its receiver layer, bit 2.
            foreach (var receiver in DuelArenaView.Build(transform).GetComponentsInChildren<Renderer>(true))
                receiver.renderingLayerMask |= 2u;
            lighting.Apply(DuelArenaLighting.Choose());

            var root = BuildCanvas();
            fake = new FakeDuelLobbyHost();
            if (Lobby == null) Lobby = fake;
            LobbyView = new DuelLobbyView(root, () => Lobby, ReturnToMenu);
            HudView = new DuelHudView(root);
            ResultView = new DuelResultView(root, LeaveToLobby, ReturnToMenu);
            if (DemoMatch && Match == null) Match = fakeMatch = new FakeDuelHudState();
            built = true;
            Refresh(0f);
            LobbyView.Snap();
            HudView.Snap();
            ResultView.Snap();
            gameObject.AddComponent<PlayDraw>().Stage = this;
            Built?.Invoke();
        }

        private Camera SetupCamera()
        {
            var view = Camera.main;
            if (view == null || view.gameObject.scene != gameObject.scene)
            {
                view = new GameObject("DuelCamera", typeof(Camera)).GetComponent<Camera>();
                view.tag = "MainCamera";
            }
            view.orthographic = false;
            view.fieldOfView = 55f;
            view.nearClipPlane = .1f;
            view.farClipPlane = 420f;
            view.allowHDR = true;
            var data = view.GetComponent<UniversalAdditionalCameraData>() ?? view.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            return view;
        }

        private RectTransform BuildCanvas()
        {
            canvasObject = new GameObject("DuelInterface", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            return canvasObject.GetComponent<RectTransform>();
        }

        /// <summary>
        /// The menu scene unloads a frame or two after this one starts; its event system and listener go with it.
        /// Only then does the stage add its own, so there is never a second of either.
        /// </summary>
        private void EnsureInputAndListener()
        {
            if (EventSystem.current == null)
            {
                var events = new GameObject("DuelEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(canvasObject.transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                Focus(shown == Screen.Lobby ? LobbyView.HostButton : shown == Screen.Result ? ResultView.Primary : null);
            }
            if (View != null && View.GetComponent<AudioListener>() == null &&
                FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length == 0)
                View.gameObject.AddComponent<AudioListener>();
        }

        private void OnDestroy()
        {
            play?.Dispose();
            play = null;
            if (canvasObject != null) Destroy(canvasObject);
        }

        // ---------- Per frame ----------

        private void LateUpdate()
        {
            if (!built) return;
            float deltaTime = Time.unscaledDeltaTime;
            EnsureInputAndListener();
            fake.Tick();
            fakeMatch?.Tick(deltaTime);
            HandleKeys();
            // Capped so a hitch (a spawn, a round reset) cannot skip a banner that lasts about a second.
            Refresh(Mathf.Min(deltaTime, .1f));
        }

        private void Update()
        {
            if (!built) return;
            float deltaTime = Time.unscaledDeltaTime;
            var session = Session;
            if (session != null && session.HasState)
            {
                if (play == null)
                {
                    play = new DuelPlayView(transform, View, session.LocalSlot);
                    play.Wounded += from => HudView.Wounded(from, View.transform);
                }
                playing = session;
                play.Look(Match != null && Match.Phase != DuelPhase.MatchOver, deltaTime);
                return;
            }
            playing = null;
            if (play != null)
            {
                play.Dispose();
                play = null;
                View.fieldOfView = 55f;
            }
            if (DriveCamera) MoveCamera(deltaTime);
        }

        /// <summary>
        /// The played duel's second half of a frame. The stage reads look at -50, before the play driver (order 0)
        /// sends it and steps the session; the bodies and the eye are placed here at 50, from the state that step
        /// produced (a frame sooner than drawing at -50), and still before FloraScatter (100) picks its plants.
        /// </summary>
        [DefaultExecutionOrder(50)]
        private sealed class PlayDraw : MonoBehaviour
        {
            public DuelStage Stage;

            private void Update()
            {
                if (Stage != null && Stage.play != null && Stage.playing != null) Stage.play.Draw(Stage.playing, Time.unscaledDeltaTime);
            }
        }

        private void Refresh(float deltaTime)
        {
            var match = Match;
            var screen = match == null ? Screen.Lobby : match.Phase == DuelPhase.MatchOver ? Screen.Result : Screen.Hud;
            LobbyView.Show(screen == Screen.Lobby);
            HudView.Show(screen != Screen.Lobby);
            ResultView.Show(screen == Screen.Result);
            if (screen != shown)
            {
                shown = screen;
                if (screen == Screen.Result) ResultView.Present(match);
                Focus(screen == Screen.Lobby ? LobbyView.HostButton : screen == Screen.Result ? ResultView.Primary : null);
            }
            if (LobbyView.Animate(deltaTime)) LobbyView.Refresh(deltaTime);
            if (HudView.Animate(deltaTime) && match != null) HudView.Refresh(match, deltaTime);
            ResultView.Animate(deltaTime);
        }

        private void HandleKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || shown != Screen.Lobby) return;
            var lobby = Lobby;
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (lobby != null && lobby.Busy) lobby.RequestCancel();
                else if (!LobbyView.Typing) ReturnToMenu();
            }
            if (LobbyView.Typing) return;
            if (keyboard.digit1Key.wasPressedThisFrame && lobby != null && !lobby.Busy) lobby.RequestHost();
            if (keyboard.digit2Key.wasPressedThisFrame) LobbyView.FocusAddress();
            if (keyboard.cKey.wasPressedThisFrame && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed)) LobbyView.Copy();
        }

        private void LeaveToLobby()
        {
            if (fakeMatch != null && ReferenceEquals(Match, fakeMatch)) { Match = fakeMatch = null; return; }
            LeaveMatchRequested?.Invoke();
        }

        /// <summary>Lobby: a slow orbit high over the clearing. Match: over the local fighter's shoulder at their spawn.</summary>
        private void MoveCamera(float deltaTime)
        {
            if (View == null) return;
            if (Match == null)
            {
                orbit += deltaTime * 3.2f;
                float yaw = orbit * Mathf.Deg2Rad;
                // The arena sits right of centre, beside the lobby column.
                var focus = new Vector3(0f, .6f, 0f);
                var position = focus + new Vector3(Mathf.Sin(yaw) * 31f, 14.5f, Mathf.Cos(yaw) * 31f);
                var look = Quaternion.LookRotation(focus - position);
                View.transform.SetPositionAndRotation(position, look * Quaternion.Euler(0f, -17f, 0f));
                return;
            }
            var spawn = DuelArenaLayout.Spawn(LocalSlot);
            var forward = Quaternion.Euler(0f, DuelArenaLayout.SpawnYaw(LocalSlot), 0f);
            var eye = spawn + forward * new Vector3(.62f, 1.05f, -3.4f);
            View.transform.SetPositionAndRotation(eye, forward * Quaternion.Euler(6f, 0f, 0f));
        }

        private static void Focus(UiButton button)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button != null ? button.gameObject : null);
        }
    }
}
