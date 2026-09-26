# Combat audio source record

The first pass used procedural fallback tones. The following clips are now imported
from publicly reusable sources and are kept in `Resources/CombatAudio` so the build
is self-contained:

| Runtime clip | Source | License / attribution |
|---|---|---|
| `Rifle_Shot.wav`, `SMG_Shot.wav` | [gunshot1.wav by Mihacappy on Freesound](https://freesound.org/people/Mihacappy/sounds/833404/) | CC0 1.0; downloaded as the 24 kHz preview and normalized to mono 22.05 kHz. SMG is a pitch-shifted presentation variant of the same recording. |
| `Shotgun_Shot.wav`, `Enemy_Shot.wav` | [Gunshot.wav by Cloud-10 on Freesound](https://freesound.org/people/Cloud-10/sounds/632821/) | CC0 1.0; trimmed and normalized to mono 22.05 kHz. |
| `Music_Combat.ogg` | [Dipolog City March](https://commons.wikimedia.org/wiki/File:Dipolog_City_March.ogg) on Wikimedia Commons | Public domain; used as a martial, period-neutral combat march. |

`Charger_Dash.wav` and `Player_Hurt.mp3` are short variations derived from the CC0
gunshot source for transitional impact cues. Other UI, ambience, hit, victory and
defeat cues remain lightweight authored fallback tones until dedicated production
recordings are commissioned.
