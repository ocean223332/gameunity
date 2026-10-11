"""Synthesises the intro score for Tiền Tuyến (no samples, no third-party audio).

Usage: python score.py <out.wav>

The cue is written against the edit in edit.py: a quiet jungle opening with a
đàn bầu-style melody, a drum-roll riser, the action drop on the first cut of
combat, a slow-motion swell, and a final heroic hit under the title card.
"""
import sys

import numpy as np

from synth import begin, braam, brass, crash, dan_bau, finish, pad, place, pluck, riser, taiko, tom

BPM = 90
BEAT = 60 / BPM
BAR = 4 * BEAT
T_DROP = 22.667          # first combat cut
T_SLOW = T_DROP + 4.5 * BAR   # slow-motion shot
T_FINAL = T_SLOW + 2 * BAR    # crane-up finale
T_TITLE = T_FINAL + 1.5 * BAR  # title card hit
END = T_TITLE + 10.5

begin(END)


# ------------------------------------------------------------ the cue

D, F, G, A, Bb, C = 50, 53, 55, 57, 58, 60  # D3 region

# I. Opening: drone, distant drums, strings.
place(pad([26, 38, 45], 23.5, cutoff=600, attack=5, release=1.5, width=.3), 0, .9)
for at, chord in ((0, [D, F, A]), (7.6, [Bb - 12, D, F]), (12.4, [G - 12, Bb, D]),
                  (15.8, [A - 12, C + 1, 64]), (17.6, [D, F, A, 62])):
    length = {0: 8.6, 7.6: 5.6, 12.4: 4.2, 15.8: 2.6, 17.6: 4.9}[at]
    place(pad(chord, length, cutoff=1100 + at * 40, attack=1.8, release=1.4), at, .75)
for at in (1.0, 5.0, 8.6, 11.2, 13.2, 14.6, 16.0, 17.3):
    place(taiko(1.25, 2.4), at, .35 + at * .015, pan=-.2)
# The drum line gathers: quarters, then eighths, then a roll into the silence.
beat_times = list(np.arange(17.97, 20.0, BEAT)) + list(np.arange(20.0, 21.35, BEAT / 2)) + list(np.arange(21.35, 22.25, BEAT / 4))
for i, at in enumerate(beat_times):
    place(taiko(1.0, 1.2), at, .35 + .45 * i / len(beat_times), pan=(-.3 if i % 2 else .3))
place(riser(4.2), 18.1, .5)
place(pad([62, 65, 69, 74], 4.3, cutoff=3500, attack=3.5, release=.2, width=.9), 18.0, .35)
# A lone đàn bầu line over the flag and the soldier.
place(dan_bau([(0, 69), (1.0, 72), (1.6, 69), (2.3, 67), (2.9, 69)], 4.6), 10.6, 1.0, pan=.15)
place(dan_bau([(0, 74), (.8, 72), (1.3, 69), (2.0, 67), (2.4, 69)], 4.4), 15.4, 1.0, pan=.15)

# II. The drop and the fight.
place(taiko(.8, 3.0), T_DROP, 1.2)
place(braam(26, 5.0), T_DROP, .9)
place(crash(4.5), T_DROP, .6)
chords = [[D, F, A], [Bb - 12, D, F], [G - 12, Bb - 12, D], [A - 12, C + 1 - 12, 52]]
roots = [38, 34, 31, 33]
ostinato = [0, 0, 12, 0, 3, 0, 7, 0, 0, 0, 12, 0, 5, 3, 2, 0]
groove = {0: 1.0, 3: .55, 6: .7, 8: .9, 11: .5, 14: .65}
bars = 4.5
for b in range(int(np.ceil(bars))):
    bar_start = T_DROP + b * BAR
    chord, root = chords[b % 4], roots[b % 4]
    for i in range(16):
        at = bar_start + i * BEAT / 4
        if at >= T_SLOW - .05:
            break
        place(pluck(root + 12 + ostinato[i], cutoff=1800 + 300 * (i % 4 == 0)), at, .55, pan=(.35 if i % 2 else -.35))
        if i in groove:
            place(taiko(.95, 1.4), at, .75 * groove[i], pan=-.1)
        if i % 2 == 1:
            place(tom(43 + (i % 4), .5), at, .22, pan=.4)
        if i % 4 == 0:
            place(pluck(root - 12, length=.5, cutoff=400), at, .9)
    if bar_start < T_SLOW - BAR / 2:
        place(brass(chord[0] + 12, BEAT * 1.5), bar_start, .45, pan=-.2)
        place(brass(chord[2] + 12, BEAT * 1.5), bar_start, .35, pan=.2)
        place(pad([c + 12 for c in chord], BAR, cutoff=2400, attack=.05, release=.4, width=.8), bar_start, .4)
    if b % 2 == 0:
        place(braam(roots[b % 4] - 12, 4.5), bar_start, .55)
    if b % 2 == 1 and bar_start + BAR < T_SLOW:
        for k in range(4):
            place(taiko(.85, .9), bar_start + 3 * BEAT + k * BEAT / 4, .5 + k * .12)

# III. Slow motion: the drums fall away, a held chord and a heartbeat.
place(braam(22, 6.0), T_SLOW, .7)
place(pad([46, 50, 53, 58, 62], 5.6, cutoff=2000, attack=.3, release=1.0, width=.9), T_SLOW, .8)
place(pad([45, 49, 52, 57, 61, 64], 2.8, cutoff=2600, attack=.8, release=.3, width=.9), T_SLOW + 2.67, .6)
for k in range(4):
    place(taiko(1.5, 2.2), T_SLOW + k * BAR / 2, .55)
place(riser(2.4), T_FINAL - 2.4, .5)

# IV. Finale: a rising brass call to the title.
place(taiko(.8, 2.5), T_FINAL, 1.1)
place(crash(3.5), T_FINAL, .45)
call = [(0, 62, 1.5), (1.5, 65, .5), (2.0, 69, 1.0), (3.0, 67, .5), (3.5, 69, .5)]
for at, midi, beats in call:
    place(brass(midi, beats * BEAT + .1), T_FINAL + at * BEAT, .6, pan=.1)
for b, chord in enumerate([[D, F, A], [Bb - 12, D, F]]):
    place(pad([c + 12 for c in chord] + [chord[0]], BAR, cutoff=2600, attack=.1, release=.5, width=.8), T_FINAL + b * BAR, .45)
for i in range(int(1.5 * 16)):
    at = T_FINAL + i * BEAT / 4
    place(pluck(38 + 12 + ostinato[i % 16]), at, .45, pan=(.35 if i % 2 else -.35))
    if i % 16 in groove:
        place(taiko(.95, 1.4), at, .8 * groove[i % 16])
for k in range(8):
    place(taiko(.9, .9), T_TITLE - BEAT * 2 + k * BEAT / 4, .45 + k * .07)

# The title hit: D major, everything at once, then a long tail.
place(taiko(.7, 4.0), T_TITLE, 1.4)
place(taiko(1.4, 5.0), T_TITLE + .02, .9)
place(braam(26, 8.0), T_TITLE, 1.0)
place(crash(6.0), T_TITLE, .55)
place(pad([38, 50, 54, 57, 62, 66, 69], 10.0, cutoff=2800, attack=.08, release=6.5, width=1.0), T_TITLE, .9)
place(brass(62, 3.5, swell=.03), T_TITLE, .5, pan=-.25)
place(brass(66, 3.5, swell=.03), T_TITLE, .4, pan=.25)
place(dan_bau([(0, 74), (.7, 76), (1.2, 74), (2.0, 69)], 4.5), T_TITLE + 3.2, .8, pan=.1)

# ------------------------------------------------------------ master

finish(sys.argv[1], END)
print(f"score {END:.2f}s drop {T_DROP:.3f} slow {T_SLOW:.3f} final {T_FINAL:.3f} title {T_TITLE:.3f}")
