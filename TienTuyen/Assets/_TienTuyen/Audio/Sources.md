# Combat audio source record

The runtime clips live in `Resources/CombatAudio`. The approved WAVs were prepared at
48 kHz, 16-bit PCM; the cicada ambience is stereo and the other clips are mono.

## Valve and CS:GO audio

The AK47, MP5-SD and XM1014 shots, plus dirt, grass and mud footsteps, are Valve
Corporation originals from the [sourcesounds/csgo mirror](https://github.com/sourcesounds/csgo/tree/08f1bd6835d4f510d2ccaedeab6bb9f637b388ab),
pinned to commit `08f1bd6835d4f510d2ccaedeab6bb9f637b388ab`. The repository does not
state a reuse licence; these are proprietary Valve assets and are included for this
coursework project with the user's requested full credit.

Reload, draw, knife and XM shell recordings are Valve / Hidden Path originals from
the [RAGNA SS GameBanana pack 552059](https://gamebanana.com/mods/552059). RAGNA SS's
page lists CC BY-NC-ND 4.0 for the pack; that page does not grant a reuse licence for
the Valve originals. The pack is retained as the source archive and credited here.

## CC0 recordings

| Runtime clips | Source | Licence |
|---|---|---|
| UI, shop, result and progress cues | [Kenney Interface Sounds](https://kenney.nl/assets/interface-sounds) | CC0 |
| Pickup, reload cloth, latch and charger cloth | [Kenney RPG Audio](https://kenney.nl/assets/rpg-audio) | CC0 |
| Body and cover impacts | [Kenney Impact Sounds](https://kenney.nl/assets/impact-sounds) | CC0 |
| Player hurt and death vocal | [TinyWorlds, 5 Hit Sounds + Dying](https://opengameart.org/content/5-hit-sounds-dying) | CC0 |
| Bullet near miss and charger swish | [artisticdude, Swishes Sound Pack](https://opengameart.org/content/swishes-sound-pack) | CC0 |
| Forest cicada ambience | [syncopika, Cicada Sounds](https://opengameart.org/content/cicada-sounds) | CC0 |

## Music

`Music_Menu_Adagio.ogg` is **Adagio for Strings, Op. 11**, composed by Samuel Barber
(1936), performed by the Vancouver Symphony Orchestra, conducted by Georges Delerue,
from the *Platoon* original soundtrack (Atlantic, 1986). The orchestra credit is from
the soundtrack album; the video names only Delerue. Source: the
[YouTube upload](https://www.youtube.com/watch?v=hqVFWlZcnUg) by channel
[서해지](https://www.youtube.com/channel/UC92UvwU5ZlP3M__GQRSuMHg), uploaded 2023-03-19,
downloaded with yt-dlp as Opus. The composition (Barber, d. 1981) and recording
(Atlantic) are copyrighted; the fan upload has no licence.

`Music_Combat_Constance.ogg` is **Constance**, composed and produced by Kevin MacLeod,
released in 2011, ISRC USUAN1100850. Source:
[incompetech.com](https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1100850),
under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).

> "Constance" Kevin MacLeod (incompetech.com)
>
> Licensed under Creative Commons: By Attribution 4.0 License
>
> http://creativecommons.org/licenses/by/4.0/

Both imports are stereo OGG at 48 kHz, with an internal loop and a 1.5-second linear
crossfade baked across its boundary. Adagio uses source seconds 13.19–386.45 (371.76
seconds after the overlap); Constance uses 2.83–118.49 (114.16 seconds after overlap).
Each file begins halfway through its baked overlap, so its end continues directly
into its start. No gain normalization is baked into either track.
