using UnityEngine;

namespace TienTuyen.Combat
{
    public enum CombatState { Menu, Playing, Paused, WaveComplete, Upgrade, Shop, Defeat, Victory }
    public enum WeaponKind { Rifle, Smg, Shotgun }

    // P1 constants: the two enemy profiles are intentionally provisional graybox tuning.
    public static class CombatRules
    {
        public const int EnemyCap = 25;
        public const float PlayerMaxHealth = 115f;
        public const float PlayerSpeed = 4.75f;
        public const int MaxWaves = 6;
        public const int SupplyWave = 3;
        public const float WaveSeconds = 40f;
        public static int Settlement(int remainingMinorUnits) => Mathf.Max(0, remainingMinorUnits / 200) * 100;
        public static int ExperienceThreshold(int level) => 8 + 4 * (Mathf.Max(1, level) - 1);
        public static bool IsSpawnSafe(Vector3 spawn, Vector3 player)
        {
            spawn.y = player.y = 0;
            return (spawn - player).sqrMagnitude >= 36f;
        }
    }
}
