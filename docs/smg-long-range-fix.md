# SMG distant-hit diagnosis and fix — 2026-10-23

Report: submachine gun in the normal combat mode deals no damage at a distance. The user selected extending reach with gradual distance damage falloff.

## Reproduction and cause

The real manual FireWeapon path was exercised against an active infantry body on an unobstructed, chest-height ray at 5, 9, 10, 15 and 20 metres, including a shoulder-camera ray at 15 metres. Both 5 and 9 metres dealt 10 damage. All four cases at 10 metres or farther dealt zero and ended the miss ray at 9.75 metres, even though the crosshair-body query found the target.

Command: `unity test D:/PRU213/gameunity/TienTuyen --mode PlayMode --filter SmgClearLaneDistanceDiagnosis ...`. Evidence: `Builds/smg-distance-diagnosis.xml`, 2 passed / 4 reproduced the reported failure. The temporary diagnostic output was subsequently removed and these cases became damage regression tests.

The limiting code multiplied the SMG catalog range 6.5 by the manual-aim factor 1.5. It rejected enemies beyond 9.75 metres before damage application. Shots are hitscan; projectile flight time was not the cause. The terrain/cover query uses finite sandbag boxes, and the test ray was above them. A second probe changed only FindAimTarget's query reach from 9.75 to 40, keeping the target, ray and covers identical; the extended query found the body (1/1 passed, `Builds/smg-range-probe.xml`). This isolated the hard range cutoff rather than a collider or camera fault.

## Selected behavior

For manually aimed SMG fire in normal mode, bullet reach is now 40 metres times RangeFactor. Full damage extends through the old effective reach, 9.75 metres times RangeFactor. Damage then decreases linearly to 25% at three times that effective reach and retains that floor until maximum bullet reach. Distance is measured horizontally from the soldier to the enemy, so camera setback does not change damage. Weapon tier, damage upgrades, critical hits and headshots continue to multiply the resulting damage. Existing cover tests still run over the full shot ray.

Base tier-I damage before other multipliers:

| Distance | Damage |
|---|---:|
| 5–9.75 m | 10 |
| 15 m | about 7.98 |
| 20 m | about 6.06 |
| 29.25–40 m | 2.5 |

Shop preview distinguishes close DPS, bullet reach and falloff start. The existing automatic-targeting range and other weapon types were outside the chosen change. Separate effective damage reach from maximum bullet reach in future weapon tuning; the new regression cases prevent a visible, unobstructed SMG target from silently losing all damage at the old cutoff.

## Verification

After the fix, the full manual-aim PlayMode class passed 40/40, including nine SMG distance/camera cases, the cover case and the range-upgrade case (Builds/smg-distance-final-play.xml). The entire EditMode suite passed 127/127, including five falloff boundary cases (Builds/smg-distance-final-edit.xml).
Windows Standalone build completed successfully on 2026-10-23 at 08:34:53 UTC: `Builds/Windows/TienTuyen.exe`, 0 errors and 6 warnings (`TienTuyen/Logs/CombatBuildReport.txt`).
