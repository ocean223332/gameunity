# Intro video and trailer

`video_output/TienTuyen_Intro.mp4` — 54.5 s, 1920×1080, 30 fps, H.264 + AAC, −14 LUFS. Everything in it is made from this repository: footage is filmed in the game engine, the score is synthesised, and the type uses the game's own OFL fonts.

| Time | Content |
|---|---|
| 0–22 s | Crane over the jungle, the camp, the hero's banner against the sky, a low push-in on the soldier. Dissolves, Vietnamese captions, đàn bầu-style melody over drones and gathering drums. |
| 22.7–40 s | The drop: hard cuts on the beat (enemy line, over the shoulder, running the lane, reverse on the muzzle), then a half-speed shot under a held chord. |
| 40–54.5 s | Finale crane-up behind the hero, title card **TIỀN TUYẾN — Giữ vững tuyến tiếp tế** on the final hit. |

## Rebuild

1. **Capture player** (Unity closed): `Unity.exe -batchmode -quit -projectPath TienTuyen -executeMethod IntroCinematicBuild.BuildCapturePlayer` (also *Tien Tuyen → Intro → Build Capture Player*). Output: `Builds/IntroCapture/TienTuyen.exe`, separate from the game build.
2. **Film** (~5 min): `Builds/IntroCapture/TienTuyen.exe -intro <repo>/video_output/final -introSize 2560x1440 -screen-width 640 -screen-height 360 -screen-fullscreen 0`. Writes `raw.mp4` (60 fps), `game_audio.f32` and `timeline.txt`. Add `-introOnly flag,hero` to record only some shots while iterating.
3. **Score and cards**: `python tools/intro_video/score.py video_output/score.wav` and `python tools/intro_video/cards.py video_output/cards` (numpy, Pillow). Both scores use the instruments in `synth.py`.
4. **Edit** (~4 min): `python tools/intro_video/edit.py video_output/final video_output/cards video_output/score.wav video_output/TienTuyen_Intro.mp4`. Requires ffmpeg on `PATH`.

## How the filming works

`Assets/_TienTuyen/IntroCinematic/IntroCinematic.cs` does nothing unless the player is launched with `-intro`. It hides the interface, mutes the in-game march, takes the camera from `CombatThirdPersonCamera` and drives `CombatGame.StepSimulation` itself at a fixed 60 fps.

- The first four shots run in the menu state; then a run starts at wave 5 (seed 1972) so all enemy types can appear.
- The battle is staged for the camera: the hero holds a west lane, new enemies are moved to the east side of the clearing, aim sweeps across the enemy line, hits land harder, and nobody can crowd the hero. The hero cannot die.
- A golden-hour sun, depth of field, film grain and a light vignette are applied for the film only; gameplay lighting is unchanged.
- Enemies standing between the lens and the subject are hidden for that frame.

The edit's cut points are tied to the score's tempo (90 BPM): if you change `T_DROP` or the shot lengths in `edit.py`, change the matching constants in `score.py`.

## Trailer

`video_output/TienTuyen_Trailer.mp4` — 96 s, 1920×1080, 30 fps, −14 LUFS, cut at 100 BPM (one bar = 2.4 s).

| Time | Content |
|---|---|
| 0–24 s | The road: canopy, camp, the soldier against the low sun, the banner. Four captions, đàn bầu, drums that stop dead. |
| 24–52.8 s | The threat, counted down on number cards: **6** waves (enemies rising on their markers, the line converging), **4** enemy types (one beat each, labelled), **3** weapons (one beat each, labelled), **1** soldier who stays. |
| 52.8–75.6 s | The battle on the beat: running the lane, over the shoulder, from the grass, an orbit, a lateral crane, then slow motion under *“Tuyến đường này không được phép đứt.”* |
| 75.6–96 s | Title over the finale crane, a silent beat, a charger stinger cut on the shotgun blast, end slate. |

Rebuild: the same capture player, launched with `-trailer <repo>/video_output/trailer` instead of `-intro` (about 10 minutes, the same `-introSize`/`-introOnly` options apply), then

```
python tools/intro_video/trailer_score.py video_output/trailer_score.wav
python tools/intro_video/trailer_cards.py video_output/trailer_cards
python tools/intro_video/trailer_edit.py video_output/trailer video_output/trailer_cards video_output/trailer_score.wav video_output/TienTuyen_Trailer.mp4
```

The trailer shots live in `IntroCinematic.Trailer.cs`. On top of the intro's staging it places enemies by hand (each type gets a clean field and a camera locked on it), re-arms the hero for each weapon beat, and from the battle on gives the hero all three weapons at once. The capture timeline also logs `kill` and `charge` events: the edit uses them to catch the charger's dash and to put the stinger's blast exactly on the score's hit (`T_BOOM`).
