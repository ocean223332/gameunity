# Intro video and trailer

## Intro

`TienTuyen/Assets/StreamingAssets/TienTuyenIntro.mp4` plays before the menu: 120 s, 1920×1080, 25 fps, H.264 + AAC, −14 LUFS, under 50 MB (there is no LFS). When the film ends, the menu picks the Adagio up at `IntroVideoGate.HandoffMenuOffsetSeconds` (51.57 s), where the film's last second of Adagio sits in `Music_Menu_Adagio.ogg` (measured by cross-correlation). If the intro's Adagio changes, measure that offset again.

| Time | Content |
|---|---|
| 0–27.5 s | "Maybe" (The Ink Spots) on the radio in an A-frame shelter: the dial, the letter and a girl's photo on the crate, the soldier in the doorway. Intercut with the Trường Sơn trail at peace (black and white) and Kissinger's "Peace is at hand." |
| 27.5–43 s | From "will prove untrue": a B-52 bomb bay and the US Navy's strike film. |
| 43–50 s | A blast shakes the shelter and the radio dies; smoke over the forest. |
| 50–90.6 s | "War. War never changes.", then the English narration (Vietnamese subtitles) over the Adagio and the trail's own archive, ending on a young truck driver's face. |
| 90.6–112 s | The game, in colour: her face dissolves into the photo in the hero's hand; at dusk, the print resting on his open left palm; a dip to black at 99.0 (`DIP` in `animatic.py`), then one crane shot from behind his helmet, the AK back in both hands, onto the enemy line standing ready across the clearing (it never fires), up over the clearing and the road and above the treetops to the karst ridges, with silent flashes on the horizon. Distant gunfire grows under the rise (CS:GO AK shots, `firefight` in `animatic.py`), and the Adagio swells into the cut. His back flag is hidden throughout. |
| 112–120 s | Hard cut to black and 1.5 s of silence; the title on black with a thud at 113.5; the Adagio resumes into the menu. |

Vietnamese subtitles are burned in for the English radio announcer and the Kissinger card. Shot list, script, sound and picture notes, and the source of every clip and sound are in `output/intro/` (`STORYBOARD.md`, `CREDITS.md`).

**The sources and build scripts live in `output/`, which git ignores**: the archive footage, the song and the Adagio (copyrighted streaming downloads), the ElevenLabs narration and effects, and `output/intro-tools/`. Only the finished file is in the repository.

### Rebuild

All commands run from `output/`.

1. **Film the game shots** (Unity closed, at least about 5 GB of RAM free): the throwaway PlayMode test route (`intro-tools/capture_run.sh`, test copy `IntroCaptureRun.cs.txt`; delete the test afterwards, never commit it) runs `IntroCinematic` with the `ending` film and the photo (`-photo intro/photo_driver_ts1163.png`) into `intro/capture/<run>/`. The capture player below also takes `-story <dir>` or `-ending <dir>`, but building it needs more free RAM than the team machine usually has.
2. **Film the shelter**: `ShelterFilm.cs` (`-shelter <dir>`, or its own capture test) writes one folder per take (`pullback`, `soldier`, `jolt_close`, `jolt_wide`). `SHELTER_RUN` in `intro-tools/shots.py` names the run the cut uses.
3. **Cut the game shots**: `python -I intro-tools/cut_ingame.py intro/capture/<run> intro/ingame-shelter shelter` writes `intro/ingame-shelter/<shot>.mp4`.
4. **Build the intro**: `python -I intro-tools/animatic.py intro/cut-shelter en shelter` (ffmpeg, numpy, Pillow) writes `intro/cut-shelter/TienTuyen_Intro_shelter_en.mp4`. Copy it over `TienTuyenIntro.mp4`.

The shot list is `SHOTS_SHELTER` in `shots.py`; the narration lines and their timing are in `narration_lines.py`. Without `shelter`, the same tools build the earlier English-narrated cut (`SHOTS`, `intro/ingame/`).

## Filming

`Assets/_TienTuyen/IntroCinematic/IntroCinematic.cs` does nothing unless the game is launched with a film argument: `-story <dir>` (the earlier intro's game shots), `-ending <dir>` (the shelter cut's closing crane) or `-trailer <dir>`. It hides the interface, mutes the in-game music, takes the camera from `CombatThirdPersonCamera`, and drives `CombatGame.StepSimulation` itself at a fixed 60 fps (`Time.captureFramerate`). While it captures, the presentation animates on that clock too (`CombatArtDirector.FrameSeconds`), however slowly frames render. Each run writes `raw.mp4` (60 fps), `game_audio.f32` and `timeline.txt` (shot boundaries; `kill` and `charge` events). `-introSize WxH` sets the frame size, and `-introOnly a,b` records only those shots.

- **Capture player**: `Unity.exe -batchmode -quit -projectPath TienTuyen -executeMethod IntroCinematicBuild.BuildCapturePlayer` (also *Tien Tuyen → Intro → Build Capture Player*) builds `Builds/IntroCapture/TienTuyen.exe`, separate from the game build. Launch it with the film argument and `-screen-width 640 -screen-height 360 -screen-fullscreen 0`.
- **The story film** runs one quiet wave-5 run (seed 1972) with the AK. It opens on the photo held upright at the hero's support hand, filling the frame. The hero stands in the west lane in the stand idle hold (`CombatCharacterRig.IdleHold`) and never walks or fires; enemies appear on the east side of the clearing and are held at least 7 m ahead of him.
- **Staging shared by the films**: a golden-hour sun, depth of field, film grain and a light vignette for the film only; new enemies are moved to the east side of the clearing; the hero cannot die; enemies between the lens and the subject are hidden for that frame.

## Trailer

`video_output/TienTuyen_Trailer.mp4` — 96 s, 1920×1080, 30 fps, −14 LUFS, cut at 100 BPM (one bar = 2.4 s).

| Time | Content |
|---|---|
| 0–24 s | The road: canopy, camp, the soldier against the low sun, the banner. Four captions, đàn bầu, drums that stop dead. |
| 24–52.8 s | The threat, counted down on number cards: **6** waves (enemies rising on their markers, the line converging), **4** enemy types (one beat each, labelled), **3** weapons (one beat each, labelled), **1** soldier who stays. |
| 52.8–75.6 s | The battle on the beat: running the lane, over the shoulder, from the grass, an orbit, a lateral crane, then slow motion under *“Tuyến đường này không được phép đứt.”* |
| 75.6–96 s | Title over the finale crane, a silent beat, a charger stinger cut on the shotgun blast, end slate. |

Rebuild: the capture player (see *Filming* above), launched with `-trailer <repo>/video_output/trailer` (about 10 minutes; `-introSize` and `-introOnly` apply), then

```
python tools/intro_video/trailer_score.py video_output/trailer_score.wav
python tools/intro_video/trailer_cards.py video_output/trailer_cards
python tools/intro_video/trailer_edit.py video_output/trailer video_output/trailer_cards video_output/trailer_score.wav video_output/TienTuyen_Trailer.mp4
```

The trailer shots live in `IntroCinematic.Trailer.cs`. On top of the shared staging it places enemies by hand (each type gets a clean field and a camera locked on it), re-arms the hero for each weapon beat, and from the battle on gives the hero all three weapons at once. The capture timeline also logs `kill` and `charge` events: the edit uses them to catch the charger's dash and to put the stinger's blast exactly on the score's hit (`T_BOOM`).
