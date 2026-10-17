using System;
using System.Collections;
using System.IO;
using TienTuyen.Combat;
using TienTuyen.Presentation.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;
using UnityEngine.Video;

namespace TienTuyen.Presentation
{
    /// <summary>Shows the launch film, then its title over the live karst valley, and joins its score to the menu.</summary>
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(CombatGame))]
    public sealed class IntroVideoGate : MonoBehaviour
    {
        public const string VideoFileName = "TienTuyenIntro.mp4";
        // The film is cut where its last shot reaches the ridges; its black title card and the silence after it are not
        // shown. The menu's Adagio carries on from the bar the film reached there.
        public const float FilmEndSeconds = 112f;
        public const float HandoffMenuOffsetSeconds = 46.07f;
        // The title over the live valley: the film's last frame dissolves away, the title comes up and goes, and the
        // camera swings from the drift to the menu shot as the menu appears.
        private const float DissolveSeconds = 1.2f, TitleInAt = .8f, TitleInSeconds = 1.5f, TitleOutAt = 6.2f,
            TitleOutSeconds = 1.2f, CameraReturnAt = 6f, CameraReturnSeconds = 2.6f, MenuFadeSeconds = 1.2f;
        private const float HandoffFadeSeconds = .25f;
        // The title card's Adagio is brought down to the menu's live music level before the join.
        private const float LevelRampSeconds = 4f;
        // The menu score is armed this early so its stream is buffered when the film's sound ends.
        private const float HandoffLeadSeconds = .5f;

        private CombatAudio combatAudio;
        private CombatPresentation presentation;
        private Canvas overlay;
        private GameObject videoObject;
        private VideoPlayer video;
        private AudioSource videoAudio;
        private RenderTexture target;
        private bool shouldPlay;
        private bool playing;
        private bool skipRequested;
        private double videoEndDsp = double.MaxValue;
        private bool scoreScheduled;
        private IDisposable inputListener;
        private CombatThirdPersonCamera orbit;
        private RawImage frame;
        private CanvasGroup title;
        private bool titling;
        private float titleTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var game = FindFirstObjectByType<CombatGame>();
            if (game == null || game.GetComponent<IntroVideoGate>() != null)
            {
                return;
            }

            game.gameObject.AddComponent<IntroVideoGate>();
        }

        private void Awake()
        {
            shouldPlay = ShouldPlayIntro(Environment.GetCommandLineArgs(), Application.isBatchMode);
            if (!shouldPlay)
            {
                return;
            }

            combatAudio = GetComponent<CombatAudio>();
            presentation = GetComponent<CombatPresentation>();
            if (combatAudio != null)
            {
                combatAudio.AutoStartMusic = false;
                combatAudio.SetIntroPlaying(true);
            }
        }

        private void OnEnable()
        {
            if (shouldPlay)
            {
                inputListener = InputSystem.onAnyButtonPress.Call(OnAnyButtonPress);
            }
        }

        private void OnDisable()
        {
            inputListener?.Dispose();
            inputListener = null;
        }

        private void Start()
        {
            if (!shouldPlay)
            {
                return;
            }

            BuildOverlay();
            BuildVideoPlayer();
            playing = true;
            video.Prepare();
        }

        private void Update()
        {
            if (orbit == null && Camera.main != null) orbit = Camera.main.GetComponent<CombatThirdPersonCamera>();
            if (skipRequested)
            {
                skipRequested = false;
                if (titling) EndTitle();
                else FinishIntro(false);
                return;
            }

            if (titling)
            {
                UpdateTitle(Mathf.Min(Time.unscaledDeltaTime, .05f));
                return;
            }

            // The live view behind the film waits at the start of the title drift, so the dissolve reveals it at rest.
            if (playing && orbit != null)
            {
                orbit.TitleVista = 1f;
                orbit.TitleVistaTime = 0f;
            }

            if (playing && video != null && video.isPlaying && video.time >= FilmEnd)
            {
                BeginTitle();
                return;
            }

            if (playing && video != null && video.isPlaying && combatAudio != null)
            {
                float remaining = (float)(FilmEnd - video.time);
                float menuLevel = combatAudio.MuteMusic ? 0f : combatAudio.MasterVolume * combatAudio.MusicVolume;
                videoAudio.volume = Mathf.Lerp(menuLevel, 1f, remaining / LevelRampSeconds);
                // The film's end on the DSP clock. The frame time trails the playhead, so the earliest
                // estimate once playback has settled past its start-up latency is the closest.
                if (video.time > 1d)
                {
                    videoEndDsp = Math.Min(videoEndDsp, AudioSettings.dspTime + FilmEnd - video.time);
                }

                // The film's last seconds are the menu score up to the handoff offset, so the score
                // starts on the DSP clock where the film's sound stops; ambience leads the picture.
                if (!scoreScheduled && AudioSettings.dspTime >= videoEndDsp - HandoffLeadSeconds)
                {
                    scoreScheduled = true;
                    combatAudio.SetIntroPlaying(false);
                    combatAudio.StartMusic(HandoffMenuOffsetSeconds, 0f, videoEndDsp);
                }
            }
        }

        private double FilmEnd => video != null && video.length > 0d ? Math.Min(video.length, FilmEndSeconds) : FilmEndSeconds;

        /// <summary>The film stops on its last frame, which then dissolves into the live valley under the title.</summary>
        private void BeginTitle()
        {
            titling = true;
            titleTime = 0f;
            video.Pause();
            videoAudio.Stop();
            // The menu was built under the film; it stays hidden until the title has gone.
            if (presentation != null) presentation.Opacity = 0f;
            if (combatAudio != null && !scoreScheduled)
            {
                scoreScheduled = true;
                combatAudio.SetIntroPlaying(false);
                combatAudio.StartMusic(HandoffMenuOffsetSeconds, HandoffFadeSeconds);
            }

            BuildTitle();
        }

        private void UpdateTitle(float dt)
        {
            titleTime += dt;
            if (frame != null) frame.color = new Color(1f, 1f, 1f, 1f - Smooth(titleTime / DissolveSeconds));
            if (title != null)
                title.alpha = Smooth((titleTime - TitleInAt) / TitleInSeconds) * (1f - Smooth((titleTime - TitleOutAt) / TitleOutSeconds));
            float back = Smooth((titleTime - CameraReturnAt) / CameraReturnSeconds);
            if (presentation != null) presentation.Opacity = Smooth((titleTime - TitleOutAt - TitleOutSeconds) / MenuFadeSeconds);
            if (orbit != null)
            {
                orbit.TitleVistaTime = titleTime;
                orbit.TitleVista = 1f - back;
            }

            // The menu comes in as the camera turns to the hero; the swing then finishes under it.
            if (playing && titleTime >= TitleOutAt + TitleOutSeconds) FinishTitleScreen();
            if (back >= 1f) titling = false;
        }

        /// <summary>A skip during the title: straight to the menu shot, the music playing on.</summary>
        private void EndTitle()
        {
            titling = false;
            if (orbit != null) orbit.TitleVista = 0f;
            if (presentation != null) presentation.Opacity = 1f;
            if (playing) FinishTitleScreen();
        }

        private void FinishTitleScreen()
        {
            playing = false;
            inputListener?.Dispose();
            inputListener = null;
            ReleaseVideo();
            ShowMenu();
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private void BuildTitle()
        {
            var group = new GameObject("IntroTitle", typeof(RectTransform), typeof(CanvasGroup));
            group.transform.SetParent(overlay.transform, false);
            var rect = (RectTransform)group.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            title = group.GetComponent<CanvasGroup>();
            title.alpha = 0f;
            title.blocksRaycasts = false;
            // A soft dark pool behind the lettering keeps it readable over a bright sky.
            var shade = new GameObject("Shade", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            shade.transform.SetParent(group.transform, false);
            shade.texture = ShadeTexture();
            shade.color = new Color(0f, 0f, 0f, .62f);
            shade.raycastTarget = false;
            UiKit.Place(shade.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 20), new Vector2(1500, 640));
            var display = UiTheme.Display != null ? UiTheme.Display : TMP_Settings.defaultFontAsset;
            var body = UiTheme.Body != null ? UiTheme.Body : TMP_Settings.defaultFontAsset;
            Line("Title", "TIỀN TUYẾN", display, 190, new Color32(226, 190, 110, 255), 110, 230);
            Line("Tagline", "GIỮ VỮNG TUYẾN TIẾP TẾ", body, 54, new Color32(225, 220, 205, 255), -50, 80);
            Line("Place", "TRƯỜNG SƠN · 1972", body, 38, new Color32(190, 184, 168, 255), -130, 60);

            void Line(string name, string text, TMP_FontAsset font, float size, Color color, float y, float height)
            {
                var label = UiKit.Text(name, group.transform, text, font, size, color, TextAlignmentOptions.Center);
                label.raycastTarget = false;
                UiTheme.Shadow(label);
                UiKit.Place(label.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, y), new Vector2(1800, height));
            }
        }

        private static Texture2D ShadeTexture()
        {
            const int w = 128, h = 64;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "IntroTitleShade" };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x + .5f) / w * 2f - 1f, dy = (y + .5f) / h * 2f - 1f;
                float a = 1f - Smooth((Mathf.Sqrt(dx * dx + dy * dy) - .2f) / .8f);
                pixels[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        // The interface builds in its own Start on the first frame, under the black overlay while the
        // film prepares, so the handoff only reveals it. Runs before its LateUpdate and keeps it inert.
        private void LateUpdate()
        {
            if (playing && presentation != null && presentation.enabled)
            {
                presentation.enabled = false;
            }
        }

        private void OnDestroy()
        {
            ReleaseVideo();
        }

        private void BuildOverlay()
        {
            var root = new GameObject("IntroVideoOverlay", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            overlay = root.GetComponent<Canvas>();
            overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            overlay.sortingOrder = 1000;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;

            var imageObject = new GameObject("IntroVideo", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
            imageObject.transform.SetParent(root.transform, false);
            var rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            imageObject.GetComponent<UnityEngine.UI.RawImage>().color = Color.black;
        }

        private void BuildVideoPlayer()
        {
            videoObject = new GameObject("IntroVideoPlayer", typeof(VideoPlayer), typeof(AudioSource));
            video = videoObject.GetComponent<VideoPlayer>();
            videoAudio = videoObject.GetComponent<AudioSource>();
            video.playOnAwake = false;
            video.waitForFirstFrame = true;
            video.skipOnDrop = true;
            video.source = VideoSource.Url;
            video.url = Path.Combine(Application.streamingAssetsPath, VideoFileName);
            video.renderMode = VideoRenderMode.RenderTexture;
            video.aspectRatio = VideoAspectRatio.FitOutside;
            video.audioOutputMode = VideoAudioOutputMode.AudioSource;
            video.SetTargetAudioSource(0, videoAudio);
            video.prepareCompleted += OnPrepared;
            video.loopPointReached += OnVideoEnded;
            video.errorReceived += OnVideoError;
            videoAudio.playOnAwake = false;
            videoAudio.spatialBlend = 0f;
            if (combatAudio != null && combatAudio.Mixer != null)
            {
                var groups = combatAudio.Mixer.FindMatchingGroups("Music");
                if (groups != null && groups.Length > 0)
                {
                    videoAudio.outputAudioMixerGroup = groups[0];
                }
            }

            target = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
            target.Create();
            // Opaque black until the first frame decodes, so nothing behind the overlay shows through.
            var active = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(false, true, Color.black);
            RenderTexture.active = active;
            video.targetTexture = target;
            var image = overlay.transform.Find("IntroVideo").GetComponent<UnityEngine.UI.RawImage>();
            image.texture = target;
        }

        private void OnPrepared(VideoPlayer source)
        {
            if (!playing)
            {
                return;
            }

            frame = overlay.transform.Find("IntroVideo").GetComponent<RawImage>();
            frame.color = Color.white;
            source.Play();
        }

        private void OnVideoEnded(VideoPlayer source)
        {
            FinishIntro(true);
        }

        private void OnVideoError(VideoPlayer source, string message)
        {
            Debug.LogError("Intro video could not play: " + message);
            FinishIntro(false);
        }

        private void OnAnyButtonPress(InputControl control)
        {
            if (playing)
            {
                skipRequested = true;
            }
        }

        private void FinishIntro(bool completed)
        {
            if (!playing)
            {
                return;
            }

            playing = false;
            inputListener?.Dispose();
            inputListener = null;
            if (orbit != null) orbit.TitleVista = 0f;
            ReleaseVideo();
            if (combatAudio != null && !(completed && scoreScheduled))
            {
                combatAudio.StartMusic(completed ? HandoffMenuOffsetSeconds : 0f,
                    completed ? HandoffFadeSeconds : 0f);
            }

            if (completed)
            {
                ShowMenu();
            }
            else
            {
                StartCoroutine(ShowMenuAfterSkipInput());
            }
        }

        private IEnumerator ShowMenuAfterSkipInput()
        {
            // The skip event must finish processing before menu shortcuts and submit actions exist.
            yield return null;
            ShowMenu();
        }

        private void ShowMenu()
        {
            if (combatAudio != null) combatAudio.SetIntroPlaying(false);
            if (presentation != null)
            {
                presentation.enabled = true;
            }
        }

        private void ReleaseVideo()
        {
            if (video != null)
            {
                video.Stop();
            }

            if (videoAudio != null)
            {
                videoAudio.Stop();
            }

            if (target != null)
            {
                target.Release();
                Destroy(target);
                target = null;
            }

            if (videoObject != null)
            {
                Destroy(videoObject);
                videoObject = null;
            }

            if (overlay != null)
            {
                var shade = overlay.transform.Find("IntroTitle/Shade");
                if (shade != null) Destroy(shade.GetComponent<RawImage>().texture);
                Destroy(overlay.gameObject);
                overlay = null;
            }

        }

        private static bool ShouldPlayIntro(string[] args, bool batchMode)
        {
            if (batchMode)
            {
                return false;
            }

            foreach (string arg in args)
            {
                if (string.Equals(arg, "-trailer", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, "-story", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, "-shelter", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, "-ending", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, "-runTests", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, "-benchmark", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
