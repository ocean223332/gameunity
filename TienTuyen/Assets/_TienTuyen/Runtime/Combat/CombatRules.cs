using UnityEngine;

namespace TienTuyen.Combat
{
    public enum CombatState { Menu, Playing, Paused, WaveComplete, Upgrade, Shop, Defeat, Victory }
    public enum WeaponKind { Rifle, Smg, Shotgun }

    // P1 constants: the two enemy profiles are intentionally provisional graybox tuning.
    public static class CombatRules
    {
        public const int EnemyCap = 25;
        public const float PlayerMaxHealth = 200f;
        public const float PlayerSpeed = 4.75f;
        /// <summary>Crouch-walk speed over run speed: CS:GO's crouch-walk cycle against its run cycle (67.8 / 145.7 in).</summary>
        public const float CrouchSpeedFactor = 0.47f;
        /// <summary>Player height standing and crouched; the crouch keeps CS:GO's head-height ratio (1.16 / 1.53 m).</summary>
        public const float PlayerStandHeight = 1.6f, PlayerCrouchHeight = 1.2f;
        /// <summary>Seconds to go fully down or back up.</summary>
        public const float CrouchSeconds = 0.15f;
        /// <summary>Walk speed over run speed: CS:GO's walk (0.52 of the run; its walk takes cover 93.4 in/s against the run's 180.6).</summary>
        public const float WalkSpeedFactor = 0.52f;
        /// <summary>Seconds to slow fully to a walk or speed back up.</summary>
        public const float WalkSeconds = 0.15f;
        public const int MaxWaves = 6;
        public const int SupplyWave = 3;
        public const float WaveSeconds = 40f;
        /// <summary>Seconds to draw the weapon switched to; it cannot fire or reload meanwhile.</summary>
        public const float WeaponDrawSeconds = 0.5f;
        public static int Settlement(int remainingMinorUnits) => Mathf.Max(0, remainingMinorUnits / 200) * 100;
        public static int ExperienceThreshold(int level) => 8 + 4 * (Mathf.Max(1, level) - 1);
        public static bool IsSpawnSafe(Vector3 spawn, Vector3 player)
        {
            spawn.y = player.y = 0;
            return (spawn - player).sqrMagnitude >= 36f;
        }
    }
}
