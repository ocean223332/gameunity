using TienTuyen.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Over-the-shoulder third-person camera. It reads mouse/gamepad look input,
    /// resolves what the crosshair is pointing at and hands that to CombatGame as
    /// manual aim. Outside combat it drifts into a slow cinematic orbit; on the main
    /// menu it holds a front-facing hero shot with a barely perceptible breathing sway.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CombatThirdPersonCamera : MonoBehaviour
    {
        public float Sensitivity = .11f;
        public float Distance = 4.3f, AimDistance = 2.5f;
        public float FieldOfView = 56f, AimFieldOfView = 40f;
        public Vector3 ShoulderOffset = new Vector3(.95f, .14f, 0);
        /// <summary>Pivot above the gameplay actor, whose origin is at capsule centre (Y=.8).</summary>
        public float PivotHeight = .92f;
        public const float MinPitch = -12f, MaxPitch = 48f;

        // First person: the eye sits above the gameplay actor (capsule centre, Y=.8) at the hero's own eye, 1.65 m off the ground.
        public float EyeHeight = .85f;
        /// <summary>How far the pivot and eye drop at a full crouch: CS:GO's head drop from stand to crouch idle.</summary>
        public float CrouchDrop = .37f;
        public float FirstPersonFieldOfView = 70f, FirstPersonAimFieldOfView = 52f;
        public const float FirstPersonMinPitch = -60f, FirstPersonMaxPitch = 70f;

        // Main-menu hero shot: the camera looks along MenuLookYaw at a hero whose shoulders are turned MenuTurn
        // degrees off square-on, who stands in the right third of the frame, clear of the left-hand panel; at
        // that turn the low-ready rifle stays inside the frame. The low-ready idle holds the shoulders
        // MenuBodyOffset degrees clockwise of the character's forward, so the facing the rig is given is turned
        // back by that much.
        public const float MenuLookYaw = 62f, MenuTurn = 59f, MenuBodyOffset = 27f, MenuFieldOfView = 26f;
        public const float MenuDistance = 3.5f, MenuCameraHeight = 1f, MenuFocusHeight = 1.12f, MenuHeroScreenX = .75f;

        private CombatGame game;
        private Transform player;
        private Camera view;
        private float yaw, pitch = 12f, aimBlend, trauma, recoilKick, orbitYaw, cinematic, noiseTime, menuBlend = 1f;
        private Vector3 pivot, aimPoint;
        private bool hasPivot, firstPerson;
        private float firstPersonBlend;
        private CombatState lastState = (CombatState)(-1);

        public float Yaw => yaw;
        public float AimBlend => aimBlend;
        /// <summary>How fast the aim weight moves, per second; anything following it should use the same rate.</summary>
        public const float AimBlendSpeed = 6.5f;
        public Vector3 AimPoint => aimPoint;
        /// <summary>1 on the main-menu shot, 0 on every other view; eases between them.</summary>
        public float MenuBlend => menuBlend;
        /// <summary>The player's chosen combat view (V); the menu, shop and end shots stay third person.</summary>
        public bool FirstPerson => firstPerson;
        /// <summary>1 when the camera is at the hero's eyes, 0 in any third-person or cinematic shot.</summary>
        public float FirstPersonWeight => Smooth(firstPersonBlend) * (1f - Smooth(cinematic)) * (1f - Smooth(menuBlend));

        /// <summary>Switches the combat view; yaw carries over, pitch is kept inside the third-person range.</summary>
        public void SetFirstPerson(bool on)
        {
            firstPerson = on;
            if (!on) pitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
        }
        /// <summary>Planar facing the rig is given on the menu so that the hero's body turns toward the camera, a little off square-on.</summary>
        public static Vector3 MenuHeroFacing => Quaternion.Euler(0, MenuLookYaw + 180f + MenuTurn - MenuBodyOffset, 0) * Vector3.forward;

        private struct Shot
        {
            public Vector3 Position;
            public float Yaw, Pitch, Fov;
        }
        public bool AimingAtEnemy { get; private set; }

        public void Initialize(CombatGame combat, Transform actor)
        {
            game = combat;
            player = actor;
            view = GetComponent<Camera>();
            view.orthographic = false;
            view.fieldOfView = FieldOfView;
            view.nearClipPlane = .08f;
            view.farClipPlane = 420f;
            view.clearFlags = CameraClearFlags.Skybox;
            view.allowHDR = true;
            var data = GetComponent<UniversalAdditionalCameraData>() ?? gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = true;
            (GetComponent<CombatFirstPersonView>() ?? gameObject.AddComponent<CombatFirstPersonView>()).Initialize(combat, actor, this);
            orbitYaw = 20f;
            yaw = 0f;
            menuBlend = game.State == CombatState.Menu ? 1f : 0f;
            cinematic = game.State == CombatState.Menu ? 0f : 1f;
            LateUpdate();
        }

        /// <summary>Weapon kick lifts the view; trauma adds decaying shake.</summary>
        public void Kick(float recoilDegrees, float shake)
        {
            recoilKick = Mathf.Min(recoilKick + recoilDegrees, 3.5f);
            trauma = Mathf.Clamp01(trauma + shake);
        }

        private void Update()
        {
            if (game == null || player == null) return;
            bool playing = game.State == CombatState.Playing;
            // Tools and tests drive the simulation with the component disabled;
            // they keep auto-aim and the cursor as they are.
            bool live = game.isActiveAndEnabled;
            if (live) UpdateCursor(playing);
            if (!playing) return;
            if (live && Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame) SetFirstPerson(!firstPerson);
            Vector2 look = Vector2.zero;
            bool fire = false, aim = false;
            var mouse = Mouse.current;
            if (mouse != null && live && Cursor.lockState == CursorLockMode.Locked)
            {
                look += mouse.delta.ReadValue() * Sensitivity;
                fire |= mouse.leftButton.isPressed;
                aim |= mouse.rightButton.isPressed;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                look += pad.rightStick.ReadValue() * (170f * Time.unscaledDeltaTime);
                fire |= pad.rightTrigger.isPressed;
                aim |= pad.leftTrigger.isPressed;
            }
            look *= Mathf.Lerp(1f, .55f, aimBlend);
            yaw = Mathf.Repeat(yaw + look.x, 360f);
            pitch = firstPerson ? Mathf.Clamp(pitch - look.y, FirstPersonMinPitch, FirstPersonMaxPitch)
                : Mathf.Clamp(pitch - look.y, MinPitch, MaxPitch);
            aimBlend = Mathf.MoveTowards(aimBlend, aim ? 1f : 0f, Time.unscaledDeltaTime * AimBlendSpeed);
            Vector3 eye = GameplayPose(out _);
            aimPoint = ResolveAimPoint(eye);
            if (live) game.SetViewInput(yaw, aimPoint, fire, eye, AimRayDirection);
        }

        private void UpdateCursor(bool playing)
        {
            if (!Application.isFocused) return;
            var wanted = playing ? CursorLockMode.Locked : CursorLockMode.None;
            if (Cursor.lockState != wanted) Cursor.lockState = wanted;
            Cursor.visible = !playing;
        }

        private void LateUpdate()
        {
            if (game == null || player == null || view == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
            bool paused = game.State == CombatState.Paused, menu = game.State == CombatState.Menu;
            if (game.State != lastState)
            {
                // Leaving the menu: the run starts looking the way the hero already faces, so he
                // does not turn and the camera swings round behind him.
                if (game.State == CombatState.Playing && lastState == CombatState.Menu)
                {
                    yaw = MenuLookYaw + 180f + MenuTurn - MenuBodyOffset;
                    pitch = 12f;
                    cinematic = 0f;
                }
                lastState = game.State;
            }
            bool combatView = game.State == CombatState.Playing || paused;
            // The menu shot is entered from wherever the camera was; the orbit weight stays put under it.
            if (!menu) cinematic = Mathf.MoveTowards(cinematic, combatView ? 0f : 1f, dt * 1.6f);
            menuBlend = Mathf.MoveTowards(menuBlend, menu ? 1f : 0f, dt * 1.1f);
            if (!paused) orbitYaw = Mathf.Repeat(orbitYaw + dt * 5f, 360f);
            if (!paused) firstPersonBlend = Mathf.MoveTowards(firstPersonBlend, firstPerson ? 1f : 0f, dt * 6f);

            Vector3 target = player.position + Vector3.up * (PivotHeight - CrouchDrop * game.Crouch);
            pivot = hasPivot ? Vector3.Lerp(pivot, target, 1f - Mathf.Exp(-22f * dt)) : target;
            hasPivot = true;

            var gameplay = new Shot
            {
                Position = GameplayPose(out _),
                Yaw = yaw,
                Pitch = pitch - recoilKick,
                Fov = Mathf.Lerp(Mathf.Lerp(FieldOfView, AimFieldOfView, aimBlend),
                    Mathf.Lerp(FirstPersonFieldOfView, FirstPersonAimFieldOfView, aimBlend), Smooth(firstPersonBlend))
            };
            // Nearly level so the orbit shows the ranges over the valley, not just the yard.
            var orbit = new Shot
            {
                Position = pivot + Vector3.up * .2f - Quaternion.Euler(8f, orbitYaw, 0) * Vector3.forward * 7.5f,
                Yaw = orbitYaw,
                Pitch = 8f,
                Fov = 50f
            };
            Shot shot = Blend(gameplay, orbit, Smooth(cinematic), pivot);
            if (menuBlend > 0f) shot = Blend(shot, MenuShot(), Smooth(menuBlend), pivot);

            Quaternion rotation = Quaternion.Euler(shot.Pitch, shot.Yaw, 0);
            if (!paused)
            {
                recoilKick = Mathf.Lerp(recoilKick, 0, 1f - Mathf.Exp(-9f * dt));
                trauma = Mathf.Max(0, trauma - dt * 1.8f);
                noiseTime += dt * 22f;
            }
            float shake = trauma * trauma;
            if (shake > 0)
            {
                rotation *= Quaternion.Euler((Mathf.PerlinNoise(noiseTime, 3.1f) - .5f) * 3.2f * shake,
                    (Mathf.PerlinNoise(7.7f, noiseTime) - .5f) * 3.2f * shake,
                    (Mathf.PerlinNoise(noiseTime, 11.3f) - .5f) * 2.4f * shake);
            }
            transform.SetPositionAndRotation(shot.Position, rotation);
            view.fieldOfView = shot.Fov;
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>
        /// The hero in the right third of the frame, knee-up, seen from the front. Position and aim
        /// breathe on slow, unrelated cycles so the shot never looks frozen.
        /// </summary>
        private Shot MenuShot()
        {
            float time = Time.unscaledTime;
            float breathe = Mathf.Sin(time * 1.15f), drift = Mathf.Sin(time * .37f + 1.3f), sway = Mathf.Sin(time * .23f);
            Quaternion look = Quaternion.Euler(0, MenuLookYaw, 0);
            Vector3 feet = player.position - Vector3.up * .8f;
            // Standing the camera left of the hero puts him right of centre without turning the lens
            // away from him; the shift tracks the aspect.
            float halfHeight = MenuDistance * Mathf.Tan(MenuFieldOfView * .5f * Mathf.Deg2Rad);
            float aspect = view.aspect > .1f ? view.aspect : 16f / 9f;
            float shift = halfHeight * aspect * (MenuHeroScreenX * 2f - 1f);
            Vector3 position = feet - look * Vector3.forward * MenuDistance - look * Vector3.right * shift + Vector3.up * MenuCameraHeight;
            position += look * Vector3.right * (sway * .035f) + Vector3.up * (breathe * .009f + drift * .012f) +
                look * Vector3.forward * (drift * .03f);
            float pitchDown = Mathf.Atan2(position.y - (feet.y + MenuFocusHeight), MenuDistance) * Mathf.Rad2Deg;
            position.y = Mathf.Max(position.y, CombatEnvironment.GroundHeight(position.x, position.z) + .35f);
            return new Shot
            {
                Position = position,
                Yaw = MenuLookYaw + sway * .22f,
                Pitch = pitchDown + breathe * .08f + drift * .1f,
                Fov = MenuFieldOfView + breathe * .12f
            };
        }

        /// <summary>
        /// Swings the camera round <paramref name="center"/> instead of through it: azimuth, distance and
        /// height blend separately, and the heading turns the same way the position travels.
        /// </summary>
        private static Shot Blend(Shot from, Shot to, float t, Vector3 center)
        {
            if (t <= 0f) return from;
            if (t >= 1f) return to;
            Vector3 a = from.Position - center, b = to.Position - center;
            float azimuthA = Mathf.Atan2(a.x, a.z) * Mathf.Rad2Deg;
            float turn = Mathf.DeltaAngle(from.Yaw, to.Yaw);
            float azimuth = azimuthA + turn * t + Mathf.DeltaAngle(azimuthA + turn, Mathf.Atan2(b.x, b.z) * Mathf.Rad2Deg) * t;
            float radius = Mathf.Lerp(new Vector2(a.x, a.z).magnitude, new Vector2(b.x, b.z).magnitude, t);
            float height = Mathf.Lerp(a.y, b.y, t);
            float rad = azimuth * Mathf.Deg2Rad;
            return new Shot
            {
                Position = center + new Vector3(Mathf.Sin(rad) * radius, height, Mathf.Cos(rad) * radius),
                Yaw = from.Yaw + turn * t,
                Pitch = Mathf.Lerp(from.Pitch, to.Pitch, t),
                Fov = Mathf.Lerp(from.Fov, to.Fov, t)
            };
        }

        private Vector3 GameplayPose(out Quaternion rotation)
        {
            rotation = Quaternion.Euler(pitch - recoilKick, yaw, 0);
            // First person looks from the actor itself, unsmoothed, so the eye never trails the body.
            Vector3 eye = player.position + Vector3.up * (EyeHeight - CrouchDrop * game.Crouch);
            if (firstPersonBlend >= 1f) return eye;
            float distance = Mathf.Lerp(Distance, AimDistance, aimBlend);
            Vector3 origin = (hasPivot ? pivot : player.position + Vector3.up * (PivotHeight - CrouchDrop * game.Crouch)) +
                rotation * (ShoulderOffset * Mathf.Lerp(1f, .85f, aimBlend));
            Vector3 back = rotation * Vector3.back;
            // Sandbag covers keep their gameplay box colliders; never look through them.
            if (Physics.SphereCast(origin, .2f, back, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(.35f, hit.distance - .05f);
            Vector3 position = origin + back * distance;
            // Stay above the ground (and the rising jungle floor) when looking up.
            position.y = Mathf.Max(position.y, CombatEnvironment.GroundHeight(position.x, position.z) + .35f);
            return Vector3.Lerp(position, eye, Smooth(firstPersonBlend));
        }

        /// <summary>
        /// The world point under the crosshair: the nearest enemy body the view ray
        /// passes through, otherwise where the ray meets the ground.
        /// </summary>
        private Vector3 ResolveAimPoint(Vector3 cameraPosition)
        {
            Vector3 direction = AimRayDirection;
            // Which body is under the crosshair is the game's rule, the same one its shots use.
            AimingAtEnemy = game.TryGetCrosshairEnemy(cameraPosition, direction, out Vector3 point);
            if (AimingAtEnemy) return point;
            point = cameraPosition + direction * 70f;
            if (direction.y < -.0001f)
            {
                float t = -cameraPosition.y / direction.y;
                if (t < 70f) point = cameraPosition + direction * t;
            }
            return point;
        }

        private Vector3 AimRayDirection => Quaternion.Euler(pitch - recoilKick, yaw, 0) * Vector3.forward;

        private void OnDisable()
        {
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
