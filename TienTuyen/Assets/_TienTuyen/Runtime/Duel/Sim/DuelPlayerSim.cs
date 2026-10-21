using System;
using System.Runtime.InteropServices;
using TienTuyen.Combat;
using TienTuyen.Content;
using UnityEngine;

namespace TienTuyen.Duel
{
    /// <summary>Move is world XZ. Weapon slots: rifle 0, SMG 1, shotgun 2, knife 3; -1 keeps the current slot.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DuelInputFrame
    {
        public int Tick;
        public Vector2 Move;
        public float Yaw, Pitch;
        public Vector3 AimOrigin, AimDirection;
        public DuelButtons Buttons;
        public int WeaponSlot;
    }

    /// <summary>Complete replay snapshot. All fields are values, including holstered weapon timers and rounds.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DuelSimState
    {
        public int Slot, ActiveWeaponSlot;
        public Vector3 Position;
        public float VerticalVelocity, Yaw, Pitch, Crouch, Walk, Health;
        public float DrawRemaining, KnifeRemaining;
        public bool KnifeContactPending;
        public DuelButtons PreviousButtons;
        public int Ammo0, Ammo1, Ammo2;
        public float Reload0, Reload1, Reload2, Cooldown0, Cooldown1, Cooldown2;

        public bool Dead => Health <= 0;
        public bool Aiming => (PreviousButtons & DuelButtons.Aim) != 0;
        public float Height => Mathf.Lerp(CombatRules.PlayerStandHeight, CombatRules.PlayerCrouchHeight, Mathf.Clamp01(Crouch));
        public int AmmoAt(int slot) => slot == 0 ? Ammo0 : slot == 1 ? Ammo1 : slot == 2 ? Ammo2 : 0;
        public float ReloadAt(int slot) => slot == 0 ? Reload0 : slot == 1 ? Reload1 : slot == 2 ? Reload2 : 0;
        public float CooldownAt(int slot) => slot == 0 ? Cooldown0 : slot == 1 ? Cooldown1 : slot == 2 ? Cooldown2 : 0;

        internal void SetAmmo(int slot, int value)
        {
            if (slot == 0) Ammo0 = value; else if (slot == 1) Ammo1 = value; else if (slot == 2) Ammo2 = value;
        }
        internal void SetReload(int slot, float value)
        {
            if (slot == 0) Reload0 = value; else if (slot == 1) Reload1 = value; else if (slot == 2) Reload2 = value;
        }
        internal void SetCooldown(int slot, float value)
        {
            if (slot == 0) Cooldown0 = value; else if (slot == 1) Cooldown1 = value; else if (slot == 2) Cooldown2 = value;
        }
    }

    public enum DuelAttackKind { None, Shot, Knife }

    /// <summary>One attack authorized by a simulation step; a knife event occurs only at contact.</summary>
    public readonly struct DuelAttack
    {
        public readonly DuelAttackKind Kind;
        public readonly int WeaponSlot, Tick;
        public readonly Vector3 Origin, Direction;

        public DuelAttack(DuelAttackKind kind, int weaponSlot, int tick, Vector3 origin, Vector3 direction)
        {
            Kind = kind; WeaponSlot = weaponSlot; Tick = tick; Origin = origin; Direction = direction;
        }
    }

    public readonly struct DuelStepResult
    {
        public readonly DuelAttack Attack;
        public bool ShotFired => Attack.Kind == DuelAttackKind.Shot;
        public bool KnifeContact => Attack.Kind == DuelAttackKind.Knife;
        public DuelStepResult(DuelAttack attack) { Attack = attack; }
    }

    /// <summary>No clocks, input devices, random generator or scene objects. Call once per fixed timestep.</summary>
    public static class DuelPlayerSim
    {
        public const int KnifeSlot = 3;
        public const float MaxHealth = 100f;

        // Duel reach is independent of survival; SMG/shotgun approximate CS:GO half-damage distances.
        // RangeModifier per 500 inches: 0.85 -> 54.17 m, 0.70 -> 24.68 m. Rifle clears the arena.
        private static readonly WeaponDefinition[] weapons =
        {
            WithRange(ContentCatalog.P2.GetWeapon(ContentIds.Rifle), 70f),
            WithRange(ContentCatalog.P2.GetWeapon(ContentIds.Smg), 54f),
            WithRange(ContentCatalog.P2.GetWeapon(ContentIds.Shotgun), 25f)
        };

        /// <summary>The shouldered weapon sits 0.2 m below the same stance-dependent head height as the capsule.</summary>
        public static Vector3 MuzzlePosition(in DuelSimState state) => new Vector3(state.Position.x,
            state.Position.y - DuelArenaLayout.GroundY + state.Height - .2f, state.Position.z);

        /// <summary>The camera and crosshair origin stays 0.1 m above the muzzle throughout a crouch or jump.</summary>
        public static Vector3 EyePosition(in DuelSimState state) => MuzzlePosition(state) + Vector3.up * .1f;

        public static WeaponDefinition WeaponAt(int slot)
        {
            if (slot < 0 || slot >= weapons.Length) throw new ArgumentOutOfRangeException(nameof(slot));
            return weapons[slot];
        }

        private static WeaponDefinition WithRange(WeaponDefinition source, float range) => source.RoundReload.HasValue
            ? new WeaponDefinition(source.Id, source.DisplayName, source.DamagePerHit, source.PelletsPerShot,
                source.ShotCooldownSeconds, source.MagazineSize, source.RoundReload.Value, range, source.BasePrice)
            : new WeaponDefinition(source.Id, source.DisplayName, source.DamagePerHit, source.PelletsPerShot,
                source.ShotCooldownSeconds, source.MagazineSize, source.ReloadSeconds, range, source.BasePrice);

        public static DuelSimState CreateState(int slot)
        {
            if (slot != 0 && slot != 1) throw new ArgumentOutOfRangeException(nameof(slot));
            return new DuelSimState
            {
                Slot = slot, Position = DuelArenaLayout.Spawn(slot), Yaw = DuelArenaLayout.SpawnYaw(slot),
                Health = MaxHealth,
                Ammo0 = WeaponAt(0).MagazineSize, Ammo1 = WeaponAt(1).MagazineSize, Ammo2 = WeaponAt(2).MagazineSize
            };
        }

        public static DuelStepResult Step(ref DuelSimState state, in DuelInputFrame input, float dt, bool canAct = true)
        {
            if (!Finite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (dt == 0) return default;
            DuelButtons pressed = input.Buttons & ~state.PreviousButtons;
            state.PreviousButtons = input.Buttons;
            if (!canAct || state.Dead) return default;

            if (Finite(input.Yaw)) state.Yaw = Mathf.Repeat(input.Yaw + 180f, 360f) - 180f;
            if (Finite(input.Pitch)) state.Pitch = Mathf.Clamp(input.Pitch, -85f, 85f);
            Move(ref state, input, pressed, dt);
            AdvanceWeapons(ref state, dt);

            DuelAttack attack = default;
            if (state.KnifeRemaining > 0)
            {
                state.KnifeRemaining = Mathf.Max(0, state.KnifeRemaining - dt);
                if (state.KnifeContactPending &&
                    state.KnifeRemaining <= CombatRules.KnifeSwingSeconds - CombatRules.KnifeContactSeconds)
                {
                    state.KnifeContactPending = false;
                    attack = MakeAttack(state, input, DuelAttackKind.Knife);
                }
            }

            int requestedSlot = input.WeaponSlot;
            if (state.KnifeRemaining <= 0 && requestedSlot >= 0 && requestedSlot <= KnifeSlot &&
                requestedSlot != state.ActiveWeaponSlot)
            {
                state.SetReload(state.ActiveWeaponSlot, 0);
                state.ActiveWeaponSlot = requestedSlot;
                state.DrawRemaining = CombatRules.WeaponDrawSeconds;
            }
            // A long step may cross contact and the end of a swing; never emit a second attack in that step.
            if (attack.Kind != DuelAttackKind.None) return new DuelStepResult(attack);
            if (state.KnifeRemaining > 0) return default;

            bool knifeTrigger = state.ActiveWeaponSlot == KnifeSlot && (input.Buttons & DuelButtons.Fire) != 0;
            if (knifeTrigger && state.DrawRemaining <= 0)
            {
                state.SetReload(state.ActiveWeaponSlot, 0);
                state.KnifeRemaining = CombatRules.KnifeSwingSeconds;
                state.KnifeContactPending = true;
                return default;
            }
            if (state.ActiveWeaponSlot == KnifeSlot || state.DrawRemaining > 0) return default;

            int slot = state.ActiveWeaponSlot;
            WeaponDefinition weapon = WeaponAt(slot);
            if ((pressed & DuelButtons.Reload) != 0 || state.AmmoAt(slot) == 0)
                StartReload(ref state, slot, weapon);
            if ((input.Buttons & DuelButtons.Fire) == 0 || state.ReloadAt(slot) > 0 ||
                state.CooldownAt(slot) > 0 || state.AmmoAt(slot) <= 0) return default;
            state.SetAmmo(slot, state.AmmoAt(slot) - 1);
            state.SetCooldown(slot, weapon.ShotCooldownSeconds);
            if (state.AmmoAt(slot) == 0) StartReload(ref state, slot, weapon);
            return new DuelStepResult(MakeAttack(state, input, DuelAttackKind.Shot));
        }

        private static void Move(ref DuelSimState state, in DuelInputFrame input, DuelButtons pressed, float dt)
        {
            Vector2 movement = Finite(input.Move.x) && Finite(input.Move.y) ? input.Move : Vector2.zero;
            movement = Vector2.ClampMagnitude(movement, 1f);
            state.Crouch = Mathf.MoveTowards(state.Crouch, (input.Buttons & DuelButtons.Crouch) != 0 ? 1 : 0,
                dt / CombatRules.CrouchSeconds);
            state.Walk = Mathf.MoveTowards(state.Walk, (input.Buttons & DuelButtons.Walk) != 0 ? 1 : 0,
                dt / CombatRules.WalkSeconds);
            float speed = CombatRules.PlayerSpeed * Mathf.Lerp(1f, CombatRules.CrouchSpeedFactor, state.Crouch) *
                Mathf.Lerp(1f, CombatRules.WalkSpeedFactor, state.Walk * (1f - state.Crouch));
            state.Position = DuelArenaLayout.MoveWithCover(state.Position,
                new Vector3(movement.x, 0, movement.y) * (speed * dt), DuelArenaLayout.PlayerRadius);
            // Keep the entire capsule within the playable rectangle, not just its pivot.
            state.Position.x = Mathf.Clamp(state.Position.x, -DuelArenaLayout.HalfWidth + DuelArenaLayout.PlayerRadius,
                DuelArenaLayout.HalfWidth - DuelArenaLayout.PlayerRadius);
            state.Position.z = Mathf.Clamp(state.Position.z, -DuelArenaLayout.HalfHeight + DuelArenaLayout.PlayerRadius,
                DuelArenaLayout.HalfHeight - DuelArenaLayout.PlayerRadius);
            if ((pressed & DuelButtons.Jump) != 0 && state.Position.y <= DuelArenaLayout.GroundY)
                state.VerticalVelocity = Mathf.Sqrt(-2f * DuelArenaLayout.Gravity * DuelArenaLayout.JumpHeight);
            state.VerticalVelocity += DuelArenaLayout.Gravity * dt;
            state.Position.y += state.VerticalVelocity * dt;
            if (state.Position.y <= DuelArenaLayout.GroundY)
            {
                state.Position.y = DuelArenaLayout.GroundY;
                state.VerticalVelocity = 0;
            }
        }

        private static void AdvanceWeapons(ref DuelSimState state, float dt)
        {
            state.DrawRemaining = Mathf.Max(0, state.DrawRemaining - dt);
            for (int slot = 0; slot < KnifeSlot; slot++)
            {
                state.SetCooldown(slot, Mathf.Max(0, state.CooldownAt(slot) - dt));
                float reload = state.ReloadAt(slot);
                if (reload <= 0) continue;
                state.SetReload(slot, Mathf.Max(0, reload - dt));
                if (state.ReloadAt(slot) == 0) state.SetAmmo(slot, WeaponAt(slot).MagazineSize);
            }
        }

        private static void StartReload(ref DuelSimState state, int slot, WeaponDefinition weapon)
        {
            if (state.ReloadAt(slot) > 0 || state.AmmoAt(slot) >= weapon.MagazineSize) return;
            state.SetReload(slot, weapon.ReloadSecondsFor(weapon.MagazineSize - state.AmmoAt(slot)));
        }

        private static DuelAttack MakeAttack(in DuelSimState state, in DuelInputFrame input, DuelAttackKind kind)
        {
            Vector3 direction = input.AimDirection;
            if (!Finite(direction) || direction.sqrMagnitude < 0.000001f)
            {
                double yaw = state.Yaw * Math.PI / 180, pitch = state.Pitch * Math.PI / 180;
                direction = new Vector3((float)(Math.Sin(yaw) * Math.Cos(pitch)), (float)-Math.Sin(pitch),
                    (float)(Math.Cos(yaw) * Math.Cos(pitch)));
            }
            Vector3 origin = Finite(input.AimOrigin) ? input.AimOrigin : MuzzlePosition(state);
            return new DuelAttack(kind, kind == DuelAttackKind.Knife ? KnifeSlot : state.ActiveWeaponSlot,
                input.Tick, origin, direction.normalized);
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
