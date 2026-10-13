using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using TienTuyen.Combat;
using TienTuyen.Content;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// First-person presentation, active while <see cref="CombatThirdPersonCamera.FirstPersonWeight"/> is past half:
    /// the hero's body is hidden but keeps its shadow, and the weapon in hand is drawn in front of the eye by an
    /// overlay camera, so it never clips into cover. A model in Resources/Viewmodels/&lt;weapon id&gt;/ replaces the
    /// generated stand-in for that weapon. It binds by empties and takes only: the authored camera sits at the empty
    /// "Viewmodel" placed at the eye (see Mount), "Muzzle" marks the muzzle and an optional "MuzzleFlash" the flash.
    /// Its takes (idle, draw, fire1.., reload, or start_reload/insert/after_reload) are played to the game's timing:
    /// the draw reaches its "done" moment (&lt;name&gt;.takes.json beside the model) when the game's draw ends, then plays
    /// out unless something interrupts; the reload is fitted whole into the game's reload; each shot restarts a fire take.
    /// Presentation only: it reads the game and never changes it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatFirstPersonView : MonoBehaviour
    {
        /// <summary>User layer drawn only by the viewmodel camera (31 is taken by the character review tool).</summary>
        public const int ViewmodelLayer = 30;
        public float ViewmodelFieldOfView = 54f;
        /// <summary>Vertical field of view the imported viewmodels were animated for (90 horizontal at 4:3).</summary>
        public float AuthoredFieldOfView = 73.7f;
        public Vector3 HipOffset = new Vector3(.17f, -.17f, .38f), AimOffset = new Vector3(0f, -.105f, .3f);

        private sealed class Viewmodel
        {
            public Transform root, muzzle;
            public GameObject flash;
            public Flash look;
            public Takes takes;
            public Sights sights;
            public Vector3 hipPosition;
            public Quaternion hipRotation;
        }

        /// <summary>Rear and front sight and the gun's up, in the Muzzle empty's frame so they follow the animated gun.</summary>
        private sealed class Sights { public Vector3 rear, front, up; public float eyeBehindRear; }

        /// <summary>Layout of &lt;name&gt;.sights.json: points in the file's "Viewmodel" frame, idle take frame 0, metres.</summary>
        [System.Serializable]
        private sealed class SightsFile { public float[] rear_sight = null, front_sight = null, muzzle = null, gun_up = null; public float eye_behind_rear_m = 0; }

        /// <summary>
        /// Layout of flash.json beside a model: the muzzle flash's star across the bore and its flame along it, in metres,
        /// its HDR brightness, and how much of the world muzzle light it gives (1 = the full light).
        /// </summary>
        [System.Serializable]
        private sealed class FlashFile { public float burst = .075f, length = .09f, width = .05f, brightness = 3f, light = 1f; }

        private sealed class Flash { public FlashFile file; public Material burst, flame; public GameObject root; }

        private CombatGame game;
        private Transform player, body;
        private CombatThirdPersonCamera view;
        private Camera mainCamera, overlay;
        private Transform rig;
        private readonly Dictionary<string, Viewmodel> viewmodels = new Dictionary<string, Viewmodel>();
        private readonly Dictionary<Renderer, ShadowCastingMode> hiddenBody = new Dictionary<Renderer, ShadowCastingMode>();
        private readonly List<Renderer> scratch = new List<Renderer>();
        private Viewmodel shown;
        private Vector3 lastPlayer;
        private float lastYaw, kick, flashAt = float.MinValue, flashRoll, bobPhase, bobWeight, swayYaw, ready = 1f;
        private int flashFrame = -1;
        private float flashScale = 1f, flashStretch = 1f, flashGlow = 1f;
        private readonly System.Random flashRandom = new System.Random(1714);
        private static Texture2D burstTexture, flameTexture;
        private static Mesh flashQuad;

        /// <summary>Share of the world muzzle light the gun in hand gives (the suppressed gun's is small).</summary>
        public float MuzzleLight => Active && shown?.look != null ? shown.look.file.light : 1f;

        public bool Active { get; private set; }

        /// <summary>How far the weapon in hand is up on its iron sights (0 at the hip); 0 for guns without sights.</summary>
        public float SightsWeight { get; private set; }

        /// <summary>The rear and front sight in the viewmodel camera's viewport, when the gun in hand has sights.</summary>
        public bool TryGetSightLine(out Vector3 rearViewport, out Vector3 frontViewport)
        {
            rearViewport = frontViewport = default;
            if (!Active || shown?.sights == null || shown.muzzle == null) return false;
            rearViewport = overlay.WorldToViewportPoint(shown.muzzle.TransformPoint(shown.sights.rear));
            frontViewport = overlay.WorldToViewportPoint(shown.muzzle.TransformPoint(shown.sights.front));
            return true;
        }

        public void Initialize(CombatGame combat, Transform actor, CombatThirdPersonCamera camera)
        {
            if (game != null) game.WeaponFired -= OnWeaponFired;
            game = combat;
            player = actor;
            view = camera;
            mainCamera = GetComponent<Camera>();
            game.WeaponFired += OnWeaponFired;
            if (overlay == null) BuildOverlay();
            lastPlayer = player.position;
            lastYaw = view.Yaw;
        }

        /// <summary>
        /// Where shots should appear to leave the weapon, as seen by the main camera: the viewmodel muzzle is drawn
        /// with its own field of view, so this is the world point under the same pixel.
        /// </summary>
        public bool TryGetMuzzle(out Vector3 worldPosition)
        {
            worldPosition = default;
            if (!Active || shown == null || shown.muzzle == null) return false;
            Vector3 screen = overlay.WorldToScreenPoint(shown.muzzle.position);
            worldPosition = mainCamera.ScreenToWorldPoint(screen);
            return true;
        }

        private void BuildOverlay()
        {
            var holder = new GameObject("ViewmodelCamera");
            holder.transform.SetParent(transform, false);
            overlay = holder.AddComponent<Camera>();
            overlay.cullingMask = 1 << ViewmodelLayer;
            overlay.nearClipPlane = .01f;
            overlay.farClipPlane = 4f;
            overlay.fieldOfView = ViewmodelFieldOfView;
            var data = holder.AddComponent<UniversalAdditionalCameraData>();
            data.renderType = CameraRenderType.Overlay;
            data.renderShadows = false;
            data.renderPostProcessing = true;
            mainCamera.GetUniversalAdditionalCameraData().cameraStack.Add(overlay);
            mainCamera.cullingMask &= ~(1 << ViewmodelLayer);
            overlay.enabled = false;
            rig = new GameObject("Viewmodel").transform;
            rig.SetParent(holder.transform, false);
        }

        private void LateUpdate()
        {
            if (game == null || view == null) return;
            bool active = view.FirstPersonWeight > .5f && game.State != CombatState.Menu && game.ActiveWeapon != null;
            HideBody(active);
            Active = active;
            overlay.enabled = active;
            if (!active)
            {
                if (shown != null) shown.root.gameObject.SetActive(false);
                shown = null;
                SightsWeight = 0;
                lastPlayer = player.position;
                lastYaw = view.Yaw;
                return;
            }
            if (game.State == CombatState.Paused) return;
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, .0001f, .05f);
            Show(game.ActiveWeapon);

            Vector3 step = player.position - lastPlayer;
            step.y = 0;
            lastPlayer = player.position;
            float speed = Mathf.Min(step.magnitude / dt, 6f);
            bobWeight = Mathf.MoveTowards(bobWeight, Mathf.Clamp01(speed / 4f), dt * 4f);
            bobPhase += dt * (4f + speed * 1.6f);
            swayYaw = Mathf.Lerp(swayYaw, Mathf.Clamp(Mathf.DeltaAngle(lastYaw, view.Yaw) / dt * -.012f, -4f, 4f), 1f - Mathf.Exp(-10f * dt));
            lastYaw = view.Yaw;
            kick = Mathf.Lerp(kick, 0f, 1f - Mathf.Exp(-14f * dt));
            if (shown.flash != null)
            {
                // Seen for the shot's own frame at least, however long that frame took.
                bool flash = Time.frameCount == flashFrame || Time.unscaledTime - flashAt < .055f;
                shown.flash.SetActive(flash);
                if (flash && shown.look != null)
                {
                    // Faces the eye, rolled and sized afresh each shot; the parent's scale is undone so sizes are metres.
                    var flashRoot = shown.flash.transform;
                    flashRoot.rotation = rig.rotation * Quaternion.Euler(0, 0, flashRoll);
                    flashRoot.localScale = new Vector3(flashScale, flashScale, flashScale * flashStretch) / Mathf.Max(flashRoot.parent.lossyScale.x, 1e-4f);
                    var tint = Color.white * (shown.look.file.brightness * flashGlow);
                    shown.look.burst.SetColor("_BaseColor", tint);
                    shown.look.flame.SetColor("_BaseColor", tint);
                }
            }

            float aim = view.AimBlend;
            float draw = 1f - game.SwitchProgress;
            // The source takes start reload and draw from the hip: the gun comes off its sights for them, at the aim rate.
            ready = Mathf.MoveTowards(ready, game.IsSwitching || game.ReloadRemaining > 0 ? 0f : 1f, dt * CombatThirdPersonCamera.AimBlendSpeed);
            SightsWeight = shown.sights != null ? aim * ready : 0f;
            if (shown.takes != null)
            {
                // Animated arms carry draw, reload and recoil themselves; walk bob and look sway are added, and on the
                // sights the gun is held on the sight line with a short kick of its own per shot.
                float sights = SightsWeight, loose = (1f - .75f * aim) * (1f - sights);
                shown.root.localPosition = shown.hipPosition;
                shown.root.localRotation = shown.hipRotation;
                shown.takes.Tick(game, dt);
                rig.localPosition = new Vector3(Mathf.Sin(bobPhase) * .008f, -Mathf.Abs(Mathf.Cos(bobPhase)) * .01f, 0) * bobWeight * loose +
                    new Vector3(0, 0, -.02f * kick * sights);
                rig.localRotation = Quaternion.Euler(-2.5f * kick * sights, swayYaw * (1f - sights), 0);
                if (sights > 0) HoldOnSights(shown, sights);
                return;
            }
            float reload = game.ReloadRemaining > 0 ? Mathf.Sin(Mathf.PI * (1f - game.ReloadRemaining / Mathf.Max(.01f, game.ReloadDuration))) : 0f;
            float bob = bobWeight * (1f - .75f * aim);
            Vector3 position = Vector3.Lerp(HipOffset, AimOffset, aim) +
                new Vector3(Mathf.Sin(bobPhase) * .012f, -Mathf.Abs(Mathf.Cos(bobPhase)) * .014f, 0) * bob +
                new Vector3(0, -.3f * draw * draw - .06f * reload, -.045f * kick);
            Quaternion rotation = Quaternion.Euler(-7f * kick + 28f * draw + 32f * reload, swayYaw, -18f * reload);
            rig.localPosition = position;
            rig.localRotation = rotation;
        }

        /// <summary>
        /// Turns and moves the model rigidly, about its rear sight, so the sight line runs along the camera's forward
        /// axis with the eye the file's distance behind the rear sight and the gun's up kept up. The line is read from
        /// the animated gun each frame, so the post stays in the notch whatever the take does.
        /// </summary>
        private void HoldOnSights(Viewmodel model, float weight)
        {
            Vector3 rear = rig.InverseTransformPoint(model.muzzle.TransformPoint(model.sights.rear));
            Vector3 front = rig.InverseTransformPoint(model.muzzle.TransformPoint(model.sights.front));
            Vector3 up = rig.InverseTransformDirection(model.muzzle.TransformDirection(model.sights.up));
            if ((front - rear).sqrMagnitude < 1e-6f) return;
            Quaternion turn = Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(Quaternion.LookRotation(front - rear, up)), weight);
            Vector3 eye = Vector3.Lerp(rear, new Vector3(0, 0, model.sights.eyeBehindRear), weight);
            model.root.localPosition = turn * (model.root.localPosition - rear) + eye;
            model.root.localRotation = turn * model.root.localRotation;
        }

        private void OnWeaponFired(int slot, Vector3 start, Vector3 end)
        {
            var weapon = game.WeaponAt(slot);
            kick = Mathf.Min(kick + (weapon == null ? .5f : weapon.PelletsPerShot > 1 ? 1f : weapon.ShotCooldownSeconds < .2f ? .3f : .55f), 1.6f);
            flashAt = Time.unscaledTime;
            flashFrame = Time.frameCount;
            flashRoll += 137.508f;
            flashScale = .85f + .3f * (float)flashRandom.NextDouble();
            flashStretch = .8f + .45f * (float)flashRandom.NextDouble();
            flashGlow = .85f + .3f * (float)flashRandom.NextDouble();
            // Posed now, so effects asking for the muzzle during this shot get the fire pose, not the last frame's.
            shown?.takes?.Shot();
        }

        private void Show(WeaponDefinition weapon)
        {
            if (!viewmodels.TryGetValue(weapon.Id, out var model))
            {
                model = Build(weapon);
                viewmodels.Add(weapon.Id, model);
            }
            if (model == shown) return;
            if (shown != null) shown.root.gameObject.SetActive(false);
            shown = model;
            shown.root.gameObject.SetActive(true);
            overlay.fieldOfView = shown.takes != null ? AuthoredFieldOfView : ViewmodelFieldOfView;
        }

        private Viewmodel Build(WeaponDefinition weapon)
        {
            string folder = "Viewmodels/" + weapon.Id;
            GameObject prefab = null;
            foreach (var candidate in Resources.LoadAll<GameObject>(folder))
                if (candidate.transform.parent == null) { prefab = candidate; break; }
            Transform root = prefab != null ? Instantiate(prefab, rig, false).transform : BuildStandIn(weapon);
            root.name = "Viewmodel_" + weapon.Id;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.gameObject.layer = ViewmodelLayer;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                // The takes pose arms and gun far from their import bounds, which then cull them on screen; always in view anyway.
                if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
            var model = new Viewmodel { root = root, muzzle = Find(root, "Muzzle"), flash = Find(root, "MuzzleFlash")?.gameObject };
            if (model.flash == null && model.muzzle != null)
            {
                FlashFile look = null;
                foreach (var text in Resources.LoadAll<TextAsset>(folder))
                    if (text.name == "flash" || text.name.EndsWith(".flash", System.StringComparison.Ordinal))
                    {
                        try { look = JsonUtility.FromJson<FlashFile>(text.text); }
                        catch (System.ArgumentException) { look = null; }
                    }
                // Without a file: a rifle's flash, or a shotgun's wider and shorter one.
                look ??= weapon.PelletsPerShot > 1 ? new FlashFile { burst = .11f, length = .065f, width = .085f, brightness = 3.2f } : new FlashFile();
                model.look = BuildFlash(model.muzzle, look);
                model.flash = model.look.root;
            }
            if (prefab != null)
            {
                string events = null;
                foreach (var text in Resources.LoadAll<TextAsset>(folder))
                    if (text.name == "takes" || text.name.EndsWith(".takes", System.StringComparison.Ordinal)) events = text.text;
                model.takes = Takes.Load(root, Resources.LoadAll<AnimationClip>(folder), events);
                model.takes?.Pose();
                Mount(root, model.muzzle);
                string sights = null;
                foreach (var text in Resources.LoadAll<TextAsset>(folder))
                    if (text.name == "sights" || text.name.EndsWith(".sights", System.StringComparison.Ordinal)) sights = text.text;
                if (model.takes != null && sights != null) model.sights = LoadSights(root, model.muzzle, sights);
            }
            model.hipPosition = root.localPosition;
            model.hipRotation = root.localRotation;
            if (model.flash != null) model.flash.SetActive(false);
            root.gameObject.SetActive(false);
            return model;
        }

        /// <summary>
        /// Puts the model's "Viewmodel" empty at the overlay camera, looking where the file looks. Up is the file's
        /// Blender +Z (the empty's local +Z after import); forward is the empty's axis, square to up, nearest the Muzzle
        /// empty. Snapping to an axis keeps the gun's authored offset instead of aiming the muzzle at the centre.
        /// </summary>
        private static void Mount(Transform root, Transform muzzle)
        {
            var anchor = Find(root, "Viewmodel");
            if (anchor == null || anchor == root) return;
            Vector3 up = Vector3.forward, forward = Vector3.down;
            if (muzzle != null)
            {
                Vector3 toMuzzle = anchor.InverseTransformDirection(muzzle.position - anchor.position);
                forward = Mathf.Abs(toMuzzle.x) > Mathf.Abs(toMuzzle.y)
                    ? new Vector3(Mathf.Sign(toMuzzle.x), 0, 0)
                    : new Vector3(0, Mathf.Sign(toMuzzle.y), 0);
            }
            Quaternion relative = Quaternion.Inverse(root.rotation) * anchor.rotation;
            Vector3 offset = Vector3.Scale(root.localScale, root.InverseTransformPoint(anchor.position));
            root.localRotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * Quaternion.Inverse(relative);
            root.localPosition = -(root.localRotation * offset);
        }

        /// <summary>
        /// Reads a sights file and moves its points into the Muzzle empty's frame. The file's axes are mapped onto the
        /// imported empty's by matching its muzzle row with where the Muzzle empty really is (a signed axis permutation
        /// and a scale), so no import convention is assumed. Null if the file or the match does not hold.
        /// </summary>
        private static Sights LoadSights(Transform root, Transform muzzle, string json)
        {
            var anchor = Find(root, "Viewmodel");
            if (anchor == null || muzzle == null) return null;
            SightsFile file;
            try { file = JsonUtility.FromJson<SightsFile>(json); }
            catch (System.ArgumentException) { return null; }
            if (file == null || !Row(file.rear_sight, out var rear) || !Row(file.front_sight, out var front) ||
                !Row(file.muzzle, out var fileMuzzle) || !Row(file.gun_up, out var up) || file.eye_behind_rear_m <= 0) return null;
            Vector3 seen = anchor.InverseTransformPoint(muzzle.position);
            if (fileMuzzle.magnitude < 1e-4f) return null;
            float scale = seen.magnitude / fileMuzzle.magnitude;
            var axis = new int[3];
            var sign = new float[3];
            var used = new bool[3];
            for (int i = 0; i < 3; i++)
            {
                int best = -1;
                float bestMiss = float.MaxValue;
                for (int j = 0; j < 3; j++)
                {
                    float miss = Mathf.Abs(Mathf.Abs(seen[i]) - Mathf.Abs(fileMuzzle[j]) * scale);
                    if (!used[j] && miss < bestMiss) { best = j; bestMiss = miss; }
                }
                used[best] = true;
                axis[i] = best;
                sign[i] = Mathf.Sign(seen[i]) * Mathf.Sign(fileMuzzle[best]);
            }
            Vector3 Map(Vector3 v, float s) => new Vector3(sign[0] * v[axis[0]], sign[1] * v[axis[1]], sign[2] * v[axis[2]]) * s;
            // The match must reproduce the muzzle; components too alike to tell apart would fail here.
            if ((Map(fileMuzzle, scale) - seen).magnitude > .01f * seen.magnitude + .002f) return null;
            return new Sights
            {
                rear = muzzle.InverseTransformPoint(anchor.TransformPoint(Map(rear, scale))),
                front = muzzle.InverseTransformPoint(anchor.TransformPoint(Map(front, scale))),
                up = muzzle.InverseTransformDirection(anchor.TransformDirection(Map(up, 1f))).normalized,
                eyeBehindRear = file.eye_behind_rear_m,
            };
        }

        private static bool Row(float[] values, out Vector3 row)
        {
            bool ok = values != null && values.Length == 3;
            row = ok ? new Vector3(values[0], values[1], values[2]) : default;
            return ok;
        }

        /// <summary>Boxes sized from the weapon's numbers, with two hands: enough to try the view before the packs land.</summary>
        private Transform BuildStandIn(WeaponDefinition weapon)
        {
            var root = new GameObject().transform;
            root.SetParent(rig, false);
            bool spread = weapon.PelletsPerShot > 1;
            float barrel = .16f + weapon.Range * .022f, bore = spread ? .034f : .022f;
            var metal = new Color(.17f, .18f, .17f);
            var skin = new Color(.62f, .45f, .34f);
            var sleeve = new Color(.3f, .34f, .22f);
            Part(root, "Receiver", new Vector3(0, 0, .02f), new Vector3(.055f, .075f, .26f), metal);
            Part(root, "Barrel", new Vector3(0, .012f, .15f + barrel * .5f), new Vector3(bore, bore, barrel), metal);
            Part(root, "Stock", new Vector3(0, -.015f, -.17f), new Vector3(.045f, .075f, .16f), spread ? new Color(.36f, .22f, .12f) : metal);
            if (!spread)
                Part(root, "Magazine", new Vector3(0, -.075f, .06f), new Vector3(.035f, .06f + Mathf.Min(weapon.MagazineSize, 40) * .0022f, .05f), metal);
            Part(root, "GripHand", new Vector3(.005f, -.06f, -.06f), new Vector3(.06f, .07f, .08f), skin);
            Part(root, "SupportHand", new Vector3(-.01f, -.045f, .17f), new Vector3(.06f, .06f, .09f), skin);
            Part(root, "GripForearm", new Vector3(.06f, -.12f, -.2f), new Vector3(.07f, .07f, .24f), sleeve).localRotation = Quaternion.Euler(-18f, -14f, 0);
            Part(root, "SupportForearm", new Vector3(-.09f, -.11f, .02f), new Vector3(.07f, .07f, .26f), sleeve).localRotation = Quaternion.Euler(-14f, 26f, 0);
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root, false);
            muzzle.localPosition = new Vector3(0, .012f, .15f + barrel);
            return root;
        }

        /// <summary>
        /// The muzzle flash as light: an additive HDR star facing the eye (the burst seen from behind) and two crossed
        /// flame cards along the view through the muzzle (the tongue seen from the side), hot white-yellow at the core and
        /// orange at the tips, bright enough for the bloom to glow. The root is turned to face the view each shot, since
        /// a model's Muzzle empty may point any way.
        /// </summary>
        private static Flash BuildFlash(Transform muzzle, FlashFile file)
        {
            var root = new GameObject("MuzzleFlash").transform;
            root.SetParent(muzzle, false);
            var look = new Flash
            {
                file = file,
                root = root.gameObject,
                burst = FlashMaterial("ViewmodelFlashBurst", burstTexture ??= FlashTexture("FlashBurst", BurstPixel)),
                flame = FlashMaterial("ViewmodelFlashFlame", flameTexture ??= FlashTexture("FlashFlame", FlamePixel)),
            };
            Card(root, look.burst, new Vector3(0, 0, .01f), Quaternion.identity, new Vector3(file.burst, file.burst, 1));
            // The quad's +Y (texture rows, muzzle first) laid along the bore, once in each plane through it.
            var along = Quaternion.Euler(90, 0, 0);
            var size = new Vector3(file.width, file.length, 1);
            Card(root, look.flame, new Vector3(0, 0, file.length / 2), along, size);
            Card(root, look.flame, new Vector3(0, 0, file.length / 2), Quaternion.Euler(0, 0, 90) * along, size);
            return look;
        }

        private static Material FlashMaterial(string name, Texture2D texture)
        {
            var material = ProceduralArt.Particle(name, true, Color.white);
            material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            material.SetFloat("_Cull", 0f);
            return material;
        }

        private static void Card(Transform root, Material material, Vector3 position, Quaternion rotation, Vector3 size)
        {
            if (flashQuad == null)
            {
                // Own quad with white vertex colours, which the particle shader multiplies in.
                flashQuad = new Mesh { name = "FlashCard", hideFlags = HideFlags.DontSave };
                flashQuad.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) };
                flashQuad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                flashQuad.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
                flashQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                flashQuad.RecalculateBounds();
            }
            var part = new GameObject("FlashCard");
            part.layer = ViewmodelLayer;
            part.transform.SetParent(root, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = size;
            part.AddComponent<MeshFilter>().sharedMesh = flashQuad;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>RGB is the colour, A how much light; generated once, the same for every gun.</summary>
        private static Texture2D FlashTexture(string name, System.Func<double, double, Color> pixel)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = pixel((x + .5) / size, (y + .5) / size);
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>A soft star: a hot round core, nine uneven rays fading to their tips, and a faint halo.</summary>
        private static Color BurstPixel(double s, double t)
        {
            double u = s * 2 - 1, v = t * 2 - 1, r = System.Math.Sqrt(u * u + v * v), angle = System.Math.Atan2(v, u);
            double core = System.Math.Exp(-(r / .24) * (r / .24)), ray = 0;
            for (int k = 0; k < 9; k++)
            {
                double at = 2 * System.Math.PI * k / 9 + (Hash(k + 1) - .5) * .5, length = .7 + .3 * Hash(k + 17), width = .10 + .10 * Hash(k + 31);
                double off = System.Math.IEEERemainder(angle - at, 2 * System.Math.PI);
                double w = width * Clamp(1 - .6 * r / length, .05, 1);
                ray = System.Math.Max(ray, System.Math.Exp(-(off / w) * (off / w)) * System.Math.Pow(Clamp(1 - r / length, 0, 1), 1.3));
            }
            double flicker = .75 + .25 * System.Math.Sin(angle * 23 + r * 17), halo = .22 * System.Math.Exp(-(r / .45) * (r / .45));
            double light = Clamp(core + ray * flicker + halo, 0, 1) * Clamp((1 - r) / .12, 0, 1);
            return Heat(Clamp(core + ray * (1 - r) * .9, 0, 1), light);
        }

        /// <summary>A tongue of flame: t runs from the muzzle (0) to the tip, x across; widest past the muzzle, hottest in.</summary>
        private static Color FlamePixel(double s, double t)
        {
            double x = System.Math.Abs(s * 2 - 1);
            double swell = System.Math.Pow(t, .35) * System.Math.Pow(1 - t, .55) / .549;
            double half = (.62 * swell + .12 * (1 - t)) * (.75 + .25 * System.Math.Sin(t * 11 + 1.3));
            double d = x / System.Math.Max(half, 1e-3);
            double light = System.Math.Exp(-d * d * 2.5) * System.Math.Pow(1 - t, 1.1) * System.Math.Sqrt(Clamp(t / .04, 0, 1));
            light *= Clamp((1 - t) / .08, 0, 1) * Clamp((1 - x) / .1, 0, 1);
            return Heat(Clamp(System.Math.Exp(-d * d * 4) * System.Math.Pow(1 - t, 1.2) * 1.1, 0, 1), light);
        }

        /// <summary>Orange at 0 through yellow to near-white at 1.</summary>
        private static Color Heat(double heat, double light)
        {
            Color a = new Color(.85f, .22f, .03f), b = new Color(1f, .52f, .10f), c = new Color(1f, .82f, .42f), d = new Color(1f, .97f, .88f);
            float h = (float)heat;
            Color color = h < .35f ? Color.Lerp(a, b, h / .35f) : h < .7f ? Color.Lerp(b, c, (h - .35f) / .35f) : Color.Lerp(c, d, (h - .7f) / .3f);
            color.a = (float)light;
            return color;
        }

        private static double Hash(int i)
        {
            double x = System.Math.Sin(i * 12.9898) * 43758.5453;
            return x - System.Math.Floor(x);
        }

        private static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

        private static Transform Part(Transform parent, string name, Vector3 position, Vector3 size, Color color)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = size;
            part.GetComponent<Renderer>().material.color = color;
            return part.transform;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Body renderers cast shadows only; ones that never cast (flash, casings) are switched off.</summary>
        private void HideBody(bool hide)
        {
            if (body == null) body = player.Find("HeroVisual");
            if (hide && body != null)
            {
                // Rescanned every frame: the rig may add renderers (casings, a new weapon) while hidden.
                body.GetComponentsInChildren(true, scratch);
                foreach (var renderer in scratch)
                {
                    if (hiddenBody.ContainsKey(renderer)) continue;
                    hiddenBody.Add(renderer, renderer.shadowCastingMode);
                    if (renderer.shadowCastingMode == ShadowCastingMode.Off) renderer.forceRenderingOff = true;
                    else renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            }
            else if (!hide && hiddenBody.Count > 0)
            {
                foreach (var entry in hiddenBody)
                {
                    if (entry.Key == null) continue;
                    entry.Key.shadowCastingMode = entry.Value;
                    entry.Key.forceRenderingOff = false;
                }
                hiddenBody.Clear();
            }
        }

        private void OnDestroy()
        {
            HideBody(false);
            foreach (var model in viewmodels.Values)
            {
                model.takes?.Dispose();
                if (model.look == null) continue;
                Destroy(model.look.burst);
                Destroy(model.look.flame);
            }
            if (game != null) game.WeaponFired -= OnWeaponFired;
            if (mainCamera != null && overlay != null) mainCamera.GetUniversalAdditionalCameraData().cameraStack.Remove(overlay);
        }
        /// <summary>A viewmodel's takes, sampled by hand so the game, not the clip, decides how long each lasts.</summary>
        private sealed class Takes
        {
            private AnimationClip idle, draw, reload, reloadStart, reloadInsert, reloadEnd, current, shot, tail;
            private readonly List<AnimationClip> fires = new List<AnimationClip>();
            private readonly Dictionary<string, float> doneAt = new Dictionary<string, float>();
            private readonly System.Random fireRandom = new System.Random(1714);
            private PlayableGraph graph;
            private AnimationPlayableOutput output;
            private AnimationClipPlayable playable;
            private float idleTime, shotTime = float.MaxValue, tailTime;
            private int insertCount;
            private bool reloading;

            public static Takes Load(Transform root, AnimationClip[] clips, string events)
            {
                if (clips == null || clips.Length == 0) return null;
                var takes = new Takes();
                foreach (var clip in clips)
                {
                    if (clip.name.StartsWith("__preview__", System.StringComparison.Ordinal)) continue;
                    string name = Name(clip.name);
                    if (name == "idle" || name == "idle1") takes.idle = clip;
                    else if (name == "draw") takes.draw = clip;
                    else if (name == "reload") takes.reload = clip;
                    else if (name == "start_reload") takes.reloadStart = clip;
                    else if (name == "insert") takes.reloadInsert = clip;
                    else if (name == "after_reload") takes.reloadEnd = clip;
                    else if (name.StartsWith("fire", System.StringComparison.Ordinal)) takes.fires.Add(clip);
                }
                if (takes.idle == null) return null;
                // takes.json: [{ "take": name, ..., "done": { "frame": n, "time_s": seconds } }]. "done" is the moment the
                // source game counts the action finished (reload: rounds granted; draw: ready to fire); takes without it
                // are done at their end.
                if (!string.IsNullOrEmpty(events))
                    foreach (Match take in Regex.Matches(events, @"""take""\s*:\s*""([^""]+)""(.*?)(?=""take""|$)", RegexOptions.Singleline))
                    {
                        var done = Regex.Match(take.Groups[2].Value, @"""done""\s*:\s*\{[^{}]*?""time_s""\s*:\s*([-+0-9.eE]+)");
                        if (done.Success) takes.doneAt[Name(take.Groups[1].Value)] = float.Parse(done.Groups[1].Value, CultureInfo.InvariantCulture);
                    }
                if (!root.TryGetComponent<Animator>(out var animator)) animator = root.gameObject.AddComponent<Animator>();
                takes.graph = PlayableGraph.Create("Viewmodel_" + root.name);
                takes.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                takes.output = AnimationPlayableOutput.Create(takes.graph, "Viewmodel", animator);
                return takes;
            }

            /// <summary>FBX takes may carry a "Rig|" prefix.</summary>
            private static string Name(string take) => take.Substring(take.LastIndexOf('|') + 1).ToLowerInvariant();

            private float Done(AnimationClip clip) =>
                doneAt.TryGetValue(Name(clip.name), out float done) ? Mathf.Min(done, clip.length) : clip.length;

            /// <summary>Holds the idle pose, so the model is measured as it will be seen.</summary>
            public void Pose()
            {
                Play(idle, 0);
                graph.Evaluate(0);
            }

            public void Shot()
            {
                if (fires.Count == 0) return;
                shot = fires[fireRandom.Next(fires.Count)];
                shotTime = 0;
                tail = null;
                Play(shot, 0);
                graph.Evaluate(0);
            }

            public void Tick(CombatGame game, float dt)
            {
                idleTime += dt;
                shotTime += dt;
                tailTime += dt;
                bool reloadNow = game.ReloadRemaining > 0;
                // The pump shotgun inserts one shell per missing round, counted when the reload starts.
                if (reloadNow && !reloading) insertCount = Mathf.Max(1, game.MagazineSize - game.Ammo);
                reloading = reloadNow;
                if (game.IsSwitching && draw != null) PlayToDone(draw, game.SwitchProgress);
                else if (reloadNow) PlayReload(1f - game.ReloadRemaining / Mathf.Max(.01f, game.ReloadDuration));
                else if (shot != null && shotTime < shot.length) Play(shot, shotTime);
                // After the game's draw ends, the rest of the take (settling) plays at its own speed.
                else if (tail != null && tailTime < tail.length) Play(tail, tailTime);
                else Play(idle, Mathf.Repeat(idleTime, idle.length));
                graph.Evaluate(0);
            }

            private void PlayToDone(AnimationClip clip, float progress)
            {
                float done = Done(clip);
                Play(clip, progress * done);
                tail = clip;
                tailTime = done;
            }

            /// <summary>
            /// The whole reload, settling included, is fitted into the game's reload, so the gun is back at idle when
            /// the game lets it aim and fire again; a tail after it would play while the gun is already on its sights.
            /// </summary>
            private void PlayReload(float progress)
            {
                tail = null;
                if (reload != null) { Play(reload, progress * reload.length); return; }
                if (reloadStart == null || reloadInsert == null || reloadEnd == null) { Play(idle, 0); return; }
                // Start, one insert per missing shell (up to its done moment, where the next shell begins), end.
                float start = Done(reloadStart), insert = Done(reloadInsert), end = reloadEnd.length;
                float t = progress * (start + insertCount * insert + end);
                if (t < start) Play(reloadStart, t);
                else if ((t -= start) < insertCount * insert) Play(reloadInsert, t % insert);
                else Play(reloadEnd, Mathf.Min(t - insertCount * insert, end));
            }

            private void Play(AnimationClip clip, float time)
            {
                if (clip != current)
                {
                    if (playable.IsValid()) playable.Destroy();
                    playable = AnimationClipPlayable.Create(graph, clip);
                    playable.SetApplyFootIK(false);
                    output.SetSourcePlayable(playable);
                    current = clip;
                }
                playable.SetTime(time);
            }

            public void Dispose()
            {
                if (graph.IsValid()) graph.Destroy();
            }
        }
    }
}
