using System;
using System.Collections;
using System.IO;
using TienTuyen.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;
using UnityEngine.Video;

namespace TienTuyen.Presentation
{
    /// <summary>Shows the launch film before the title screen and joins its score to the menu.</summary>
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(CombatGame))]
    public sealed class IntroVideoGate : MonoBehaviour
    {
        public const string VideoFileName = "TienTuyenIntro.mp4";
        public const float HandoffMenuOffsetSeconds = 54.1f;
        private const float HandoffFadeSeconds = .25f;

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
        private IDisposable inputListener;

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

            if (presentation != null)
            {
                presentation.enabled = false;
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
            if (skipRequested)
            {
                skipRequested = false;
                FinishIntro(false);
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

            overlay.transform.Find("IntroVideo").GetComponent<UnityEngine.UI.RawImage>().color = Color.white;
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
            ReleaseVideo();
            if (combatAudio != null)
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
                if (string.Equals(arg, "-intro", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, "-trailer", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, "-story", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, "-shelter", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, "-runTests", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
