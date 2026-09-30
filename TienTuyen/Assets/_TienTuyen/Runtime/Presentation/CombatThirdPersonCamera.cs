using System.Collections.Generic;
using TienTuyen.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Over-the-shoulder third-person camera. It reads mouse/gamepad look input,
    /// resolves what the crosshair is pointing at and hands that to CombatGame as
    /// manual aim. Outside combat it drifts into a slow cinematic orbit.
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

        private CombatGame game;
        private Transform player;
        private Camera view;
        private readonly List<Transform> enemies = new List<Transform>();
        private float yaw, pitch = 12f, aimBlend, trauma, recoilKick, orbitYaw, cinematic = 1f, noiseTime, menuShift = 1f;
        private Vector3 pivot, aimPoint;
        private bool hasPivot;
        private CombatState lastState = (CombatState)(-1);

        public float Yaw => yaw;
        public float AimBlend => aimBlend;
        public Vector3 AimPoint => aimPoint;
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
            foreach (Transform child in combat.transform)
                if (child.name.StartsWith("Enemy_", System.StringComparison.Ordinal)) enemies.Add(child);
            orbitYaw = 20f;
            yaw = 0f;
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
            pitch = Mathf.Clamp(pitch - look.y, MinPitch, MaxPitch);
            aimBlend = Mathf.MoveTowards(aimBlend, aim ? 1f : 0f, Time.unscaledDeltaTime * 6.5f);
            aimPoint = ResolveAimPoint(GameplayPose(out _));
            if (live) game.SetViewInput(yaw, aimPoint, fire);
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
            bool paused = game.State == CombatState.Paused;
            if (game.State != lastState)
            {
                // Leaving the menu: start the run looking where the orbit was looking.
                if (game.State == CombatState.Playing && lastState == CombatState.Menu)
                {
                    yaw = orbitYaw;
                    pitch = 12f;
                }
                lastState = game.State;
            }
            bool combatView = game.State == CombatState.Playing || paused;
            cinematic = Mathf.MoveTowards(cinematic, combatView ? 0f : 1f, dt * 1.6f);
            if (!paused) orbitYaw = Mathf.Repeat(orbitYaw + dt * 5f, 360f);

            Vector3 target = player.position + Vector3.up * PivotHeight;
            pivot = hasPivot ? Vector3.Lerp(pivot, target, 1f - Mathf.Exp(-22f * dt)) : target;
            hasPivot = true;

            Quaternion gameplayRotation;
            Vector3 gameplayPosition = GameplayPose(out gameplayRotation);
            Quaternion orbitRotation = Quaternion.Euler(18f, orbitYaw, 0);
            // On the main menu, slide the view left so the soldier stands clear of the left-hand panel.
            menuShift = Mathf.MoveTowards(menuShift, game.State == CombatState.Menu ? 1f : 0f, dt * 1.5f);
            Vector3 orbitPosition = pivot + Vector3.up * .2f - orbitRotation * Vector3.forward * 7.5f -
                orbitRotation * Vector3.right * (3.1f * menuShift);
            float blend = cinematic * cinematic * (3f - 2f * cinematic);
            Vector3 position = Vector3.Lerp(gameplayPosition, orbitPosition, blend);
            Quaternion rotation = Quaternion.Slerp(gameplayRotation, orbitRotation, blend);

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
            transform.SetPositionAndRotation(position, rotation);
            view.fieldOfView = Mathf.Lerp(Mathf.Lerp(FieldOfView, AimFieldOfView, aimBlend), 50f, blend);
        }

        private Vector3 GameplayPose(out Quaternion rotation)
        {
            rotation = Quaternion.Euler(pitch - recoilKick, yaw, 0);
            float distance = Mathf.Lerp(Distance, AimDistance, aimBlend);
            Vector3 origin = (hasPivot ? pivot : player.position + Vector3.up * PivotHeight) +
                rotation * (ShoulderOffset * Mathf.Lerp(1f, .85f, aimBlend));
            Vector3 back = rotation * Vector3.back;
            // Sandbag covers keep their gameplay box colliders; never look through them.
            if (Physics.SphereCast(origin, .2f, back, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(.35f, hit.distance - .05f);
            Vector3 position = origin + back * distance;
            // Stay above the ground (and the rising jungle floor) when looking up.
            position.y = Mathf.Max(position.y, CombatEnvironment.GroundHeight(position.x, position.z) + .35f);
            return position;
        }

        /// <summary>
        /// The world point under the crosshair: the nearest enemy body the view ray
        /// passes through, otherwise where the ray meets the ground.
        /// </summary>
        private Vector3 ResolveAimPoint(Vector3 cameraPosition)
        {
            Vector3 direction = Quaternion.Euler(pitch - recoilKick, yaw, 0) * Vector3.forward;
            Vector2 origin2 = new Vector2(cameraPosition.x, cameraPosition.z);
            Vector2 direction2 = new Vector2(direction.x, direction.z);
            float planar = direction2.magnitude;
            AimingAtEnemy = false;
            float best = 70f;
            Vector3 point = cameraPosition + direction * best;
            if (planar > .0001f)
            {
                direction2 /= planar;
                foreach (var enemy in enemies)
                {
                    if (!enemy.gameObject.activeSelf) continue;
                    Vector2 offset = new Vector2(enemy.position.x, enemy.position.z) - origin2;
                    float along = Vector2.Dot(offset, direction2);
                    if (along <= 0) continue;
                    float miss = (offset - direction2 * along).magnitude;
                    float radius = .5f * Mathf.Max(1f, enemy.localScale.x / .7f);
                    if (miss > radius) continue;
                    float t = along / planar;
                    float height = cameraPosition.y + direction.y * t;
                    if (height < -.1f || height > 2.1f * Mathf.Max(1f, enemy.localScale.y / .75f) || t >= best) continue;
                    best = t;
                    point = new Vector3(enemy.position.x, .88f, enemy.position.z);
                    AimingAtEnemy = true;
                }
            }
            if (!AimingAtEnemy && direction.y < -.0001f)
            {
                float t = -cameraPosition.y / direction.y;
                if (t < best) point = cameraPosition + direction * t;
            }
            return point;
        }

        private void OnDisable()
        {
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
