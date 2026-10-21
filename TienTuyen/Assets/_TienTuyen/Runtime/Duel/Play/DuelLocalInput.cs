using TienTuyen.Duel.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TienTuyen.Duel.Play
{
    /// <summary>
    /// The station's controls, read into a <see cref="DuelInputFrame"/>: WASD or arrows and the left stick move
    /// relative to the camera, the left button or right trigger fires, Space jumps, Ctrl or C or R3 crouches,
    /// Shift walks, R reloads, 1-3 pick the guns, 5 or F the knife, Q the last weapon, the wheel or Y cycles.
    /// The first-person camera owns look, aim-down-sights and the cursor; this reads them from it and keys only.
    /// </summary>
    public sealed class DuelLocalInput
    {
        private const int Slots = DuelPlayerSim.KnifeSlot + 1;
        private int lastSlot = -1, heldSlot;

        /// <param name="activeSlot">The weapon in hand now, for Q and cycling.</param>
        public DuelInputFrame Read(IDuelAimSource aim, int activeSlot)
        {
            var frame = new DuelInputFrame
            {
                Yaw = aim.Yaw, Pitch = aim.Pitch, AimOrigin = aim.AimOrigin, AimDirection = aim.AimDirection,
                WeaponSlot = -1
            };
            if (activeSlot != heldSlot)
            {
                lastSlot = heldSlot;
                heldSlot = activeSlot;
            }

            Vector2 move = Vector2.zero;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1;
                if (keyboard.spaceKey.isPressed) frame.Buttons |= DuelButtons.Jump;
                if (keyboard.rKey.isPressed) frame.Buttons |= DuelButtons.Reload;
                if (keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed) frame.Buttons |= DuelButtons.Crouch;
                if (keyboard.shiftKey.isPressed) frame.Buttons |= DuelButtons.Walk;
                if (keyboard.digit1Key.wasPressedThisFrame) frame.WeaponSlot = 0;
                if (keyboard.digit2Key.wasPressedThisFrame) frame.WeaponSlot = 1;
                if (keyboard.digit3Key.wasPressedThisFrame) frame.WeaponSlot = 2;
                if (keyboard.digit5Key.wasPressedThisFrame || keyboard.fKey.wasPressedThisFrame) frame.WeaponSlot = DuelPlayerSim.KnifeSlot;
                if (keyboard.qKey.wasPressedThisFrame && lastSlot >= 0) frame.WeaponSlot = lastSlot;
            }
            if (aim.Aiming) frame.Buttons |= DuelButtons.Aim;
            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                if (mouse.leftButton.isPressed) frame.Buttons |= DuelButtons.Fire;
                float wheel = mouse.scroll.ReadValue().y;
                if (wheel > 0) frame.WeaponSlot = (activeSlot + Slots - 1) % Slots;
                else if (wheel < 0) frame.WeaponSlot = (activeSlot + 1) % Slots;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                move += pad.leftStick.ReadValue();
                if (pad.rightTrigger.isPressed) frame.Buttons |= DuelButtons.Fire;
                if (pad.rightStickButton.isPressed) frame.Buttons |= DuelButtons.Crouch;
                if (pad.buttonSouth.isPressed) frame.Buttons |= DuelButtons.Jump;
                if (pad.buttonWest.isPressed) frame.Buttons |= DuelButtons.Reload;
                if (pad.buttonNorth.wasPressedThisFrame) frame.WeaponSlot = (activeSlot + 1) % Slots;
            }

            // Forward is wherever the camera looks, as in the station.
            move = Vector2.ClampMagnitude(move, 1f);
            float radians = aim.Yaw * Mathf.Deg2Rad, sin = Mathf.Sin(radians), cos = Mathf.Cos(radians);
            frame.Move = new Vector2(move.x * cos + move.y * sin, -move.x * sin + move.y * cos);
            return frame;
        }
    }
}
