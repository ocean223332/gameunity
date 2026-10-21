using UnityEngine;

namespace TienTuyen.Combat
{
    public enum CombatState { Menu, Playing, Paused, WaveComplete, Upgrade, Shop, Defeat, Victory }
    public enum WeaponKind { Rifle, Smg, Shotgun }

    // P1 constants: the two enemy profiles are intentionally provisional graybox tuning.
    public static class CombatRules
    {
        public const float HeadshotMultiplier = 1.6f;

        // Match the upper fifth of the same body column used by the crosshair hit test.
        public static bool IsHeadshot(float height, float bodyHeight) =>
            bodyHeight > 0 && height >= bodyHeight * .8f && height <= bodyHeight;

        /// <summary>Enemies alive at once; a squad member that would pass it waits for a kill.</summary>
        public const int EnemyCap = 14;
        /// <summary>Each wave sends its enemies in squads: the first squad 1.1 s in, members 0.35 s apart, and a lull
        /// until the next squad starts. Squads grow 3, 3, 4, 5, 6, 7 and come every 8.2 s, 7.6 s ... 5.2 s:
        /// 15, 18, 24, 35, 54 and 70 enemies, 216 a run (Elites take a squad place).</summary>
        public const float FirstSquadSeconds = 1.1f, SquadSpacingSeconds = 0.35f;
        public static int SquadSize(int wave) => 3 + (Mathf.Max(1, wave) - 1) * 4 / 5;
        public static float SquadIntervalSeconds(int wave) => 8.2f - .6f * (Mathf.Max(1, wave) - 1);
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
        /// <summary>The knife: one slash lasts KnifeSwingSeconds and strikes at KnifeContactSeconds every enemy within
        /// KnifeReach metres and KnifeArcDegrees either side of the aim. Infantry (35 HP) falls in two; elites take half.</summary>
        public const float KnifeDamage = 25f, KnifeEliteFactor = .5f, KnifeReach = 1.8f, KnifeArcDegrees = 60f;
        public const float KnifeSwingSeconds = .6f, KnifeContactSeconds = .2f;
        public static int Settlement(int remainingMinorUnits) => Mathf.Max(0, remainingMinorUnits / 200) * 100;
        public static int ExperienceThreshold(int level) => 8 + 4 * (Mathf.Max(1, level) - 1);
        public static bool IsSpawnSafe(Vector3 spawn, Vector3 player)
        {
            spawn.y = player.y = 0;
            return (spawn - player).sqrMagnitude >= 36f;
        }
    }
}
