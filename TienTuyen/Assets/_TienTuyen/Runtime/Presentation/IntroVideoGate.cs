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
        public const float HandoffMenuOffsetSeconds = 51.57f;
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
            if (skipRequested)
            {
                skipRequested = false;
                FinishIntro(false);
                return;
            }

            if (playing && video != null && video.isPlaying && combatAudio != null)
            {
                float remaining = (float)(video.length - video.time);
                float menuLevel = combatAudio.MuteMusic ? 0f : combatAudio.MasterVolume * combatAudio.MusicVolume;
                videoAudio.volume = Mathf.Lerp(menuLevel, 1f, remaining / LevelRampSeconds);
                // The film's end on the DSP clock. The frame time trails the playhead, so the earliest
                // estimate once playback has settled past its start-up latency is the closest.
                if (video.time > 1d)
                {
                    videoEndDsp = Math.Min(videoEndDsp, AudioSettings.dspTime + video.length - video.time);
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
                    || string.Equals(arg, "-runTests", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
