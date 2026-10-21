using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// The lobby's look at the man you will fight as: the duel's own body (<see cref="DuelBody"/>, so the lobby shows
    /// exactly what the match draws) standing in a lit corner far below the arena, filmed by its own camera into
    /// <see cref="Texture"/> with nothing round him, so the lobby lays it over the orbiting arena. Each man is built the
    /// first time he is shown and kept; only the men shown are ticked, and the camera renders only while
    /// <see cref="Active"/>.
    /// </summary>
    internal sealed class DuelCharacterPreview : IDisposable
    {
        /// <summary>An unnamed layer only the preview camera draws; the stage's camera leaves it out.</summary>
        public const int Layer = 29;
        private static readonly Vector3 Studio = new Vector3(0f, -600f, 0f);
        // One man fills the frame at Distance; a pair stands Spacing either side of the middle, seen from PairDistance.
        private const float Distance = 4.3f, PairDistance = 6.2f, Spacing = .48f, EyeHeight = 1.02f, FocusHeight = .93f;

        private readonly Transform root;
        private readonly Camera camera;
        private readonly Dictionary<string, DuelBody> bodies = new Dictionary<string, DuelBody>(StringComparer.Ordinal);
        private readonly List<(DuelBody body, DuelSimState pose)> shown = new List<(DuelBody, DuelSimState)>();

        public RenderTexture Texture { get; }

        public DuelCharacterPreview(Transform parent)
        {
            root = new GameObject("CharacterPreview").transform;
            root.SetParent(parent, false);
            root.position = Studio;
            Texture = new RenderTexture(512, 768, 24, RenderTextureFormat.ARGB32) { name = "CharacterPreview", antiAliasing = 4 };
            camera = new GameObject("PreviewCamera").AddComponent<Camera>();
            camera.transform.SetParent(root, false);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << Layer;
            camera.fieldOfView = 27f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 20f;
            camera.allowHDR = false;
            camera.targetTexture = Texture;
            camera.enabled = false;
            // No post-processing: it would fill the clear alpha the lobby needs round the man.
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            // The arena's sun reaches here too, but a night arena would leave him black: a warm key and a cool rim of
            // his own, out of reach of the arena 600 m above.
            Lamp("Key", new Vector3(1.6f, 2.6f, 2.8f), new Color(1f, .9f, .78f), 2.4f);
            Lamp("Rim", new Vector3(-1.4f, 2.3f, -1.8f), new Color(.62f, .74f, 1f), 2f);
            Lamp("Fill", new Vector3(-2.2f, 1.2f, 2.4f), new Color(.8f, .85f, .9f), .7f);
        }

        /// <summary>Whether the camera draws; off whenever the lobby is not on screen.</summary>
        public bool Active
        {
            get => camera.enabled;
            set
            {
                camera.enabled = value;
                if (!value)
                    foreach (var body in bodies.Values) body.Actor.gameObject.SetActive(false);
                else
                    foreach (var (body, _) in shown) body.Actor.gameObject.SetActive(true);
            }
        }

        /// <summary>Stands these men in front of the camera: one alone, or two side by side turned a little to each other.</summary>
        public void Show(IReadOnlyList<DuelCharacter> men)
        {
            foreach (var body in bodies.Values) body.Actor.gameObject.SetActive(false);
            shown.Clear();
            int count = Mathf.Min(men.Count, 2);
            for (int i = 0; i < count; i++)
            {
                var body = BodyFor(men[i].Profile);
                float x = count == 1 ? 0f : (i == 0 ? -Spacing : Spacing);
                // Shouldered and aimed past the camera, a three-quarter turn: the uniform and the gun both read, and a
                // pair aims the same way, side by side, so neither muzzle leaves the frame.
                const float yaw = 30f;
                var pose = new DuelSimState
                {
                    Position = Studio + new Vector3(x, DuelArenaLayout.GroundY, 0f), Yaw = 180f + yaw, Health = 100f,
                    Ammo0 = DuelPlayerSim.WeaponAt(0).MagazineSize,
                };
                body.Actor.gameObject.SetActive(camera.enabled);
                shown.Add((body, pose));
            }
            float distance = count > 1 ? PairDistance : Distance;
            // The camera stands on -z of the studio looking along +z, so a body turned to yaw 180 faces it.
            camera.transform.position = Studio + new Vector3(0f, EyeHeight, -distance);
            camera.transform.LookAt(Studio + new Vector3(0f, FocusHeight, 0f));
        }

        public void Tick(float deltaTime)
        {
            if (!camera.enabled) return;
            foreach (var (body, pose) in shown) body.Tick(pose, true, false, deltaTime);
        }

        private DuelBody BodyFor(string profile)
        {
            if (bodies.TryGetValue(profile, out var body)) return body;
            body = new DuelBody(root, "Preview " + profile, profile);
            foreach (var part in body.Actor.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = Layer;
            bodies[profile] = body;
            return body;
        }

        private void Lamp(string name, Vector3 at, Color color, float intensity)
        {
            var lamp = new GameObject(name).AddComponent<Light>();
            lamp.transform.SetParent(root, false);
            lamp.transform.localPosition = at;
            lamp.type = LightType.Point;
            lamp.range = 8f;
            lamp.color = color;
            lamp.intensity = intensity;
            lamp.shadows = LightShadows.None;
            lamp.cullingMask = 1 << Layer;
        }

        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            Texture.Release();
            UnityEngine.Object.Destroy(Texture);
        }
    }
}
