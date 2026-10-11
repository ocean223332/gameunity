"""Synthesises the trailer score for Tiền Tuyến (no samples, no third-party audio).

Usage: python trailer_score.py <out.wav>

The cue follows the cut in trailer_edit.py at 100 BPM (one bar = 2.4 s):
I   a quiet road: drones, distant drums, a đàn bầu line under the captions;
II  the threat: a hit on every number card, a pulse that thickens with each act;
III the battle: full drums, ostinato and a brass theme, then a slow-motion lament;
IV  the title hit, a silent beat, the stinger's blast and the end slate.
"""
import sys

import numpy as np

from synth import begin, braam, brass, crash, dan_bau, finish, pad, place, pluck, riser, taiko, tom

BPM = 100
BEAT = 60 / BPM
BAR = 4 * BEAT
T_II = 24.0                    # first number card
CARDS = [24.0, 31.2, 38.4, 48.0]
PULSES = [(26.4, 31.2, 0), (33.6, 38.4, 1), (40.8, 48.0, 2)]  # (start, end, intensity)
T_DROP = 52.8                  # the battle
T_SLOW = 68.4                  # slow motion
T_FINAL = 73.2                 # finale crane
T_TITLE = 75.6                 # title card
T_BOOM = 88.2                  # stinger blast
T_SLATE = 89.0
END = 96.0

begin(END)

D, F, G, A, Bb, C = 50, 53, 55, 57, 58, 60
CHORDS = [[D, F, A], [Bb - 12, D, F], [G - 12, Bb - 12, D], [A - 12, C + 1 - 12, 52]]
ROOTS = [38, 34, 31, 33]
OSTINATO = [0, 0, 12, 0, 3, 0, 7, 0, 0, 0, 12, 0, 5, 3, 2, 0]
GROOVE = {0: 1.0, 3: .55, 6: .7, 8: .9, 11: .5, 14: .65}


def hit(at, size=1.0):
    """A number-card impact: drum, braam and cymbal."""
    place(taiko(.8, 3.0), at, 1.0 * size)
    place(taiko(1.4, 3.5), at + .01, .6 * size)
    place(braam(26, 4.0), at, .75 * size)
    place(crash(3.0), at, .35 * size)


def groove_bar(at, b, level, stop=None):
    """One bar of the battle groove; level 0..2 adds layers."""
    chord, root = CHORDS[b % 4], ROOTS[b % 4]
    for i in range(16):
        t = at + i * BEAT / 4
        if stop is not None and t >= stop - .02:
            break
        cutoff = (700, 1300, 2000)[level] + 300 * (i % 4 == 0)
        place(pluck(root + 12 + OSTINATO[i], cutoff=cutoff), t, (.35, .45, .55)[level], pan=(.35 if i % 2 else -.35))
        if level == 0 and i in (0, 8):
            place(taiko(1.0, 1.4), t, .55)
        if level >= 1 and i in GROOVE:
            place(taiko(.95, 1.4), t, (.55, .75)[level - 1] * GROOVE[i], pan=-.1)
        if level >= 1 and i % 2 == 1:
            place(tom(43 + (i % 4), .5), t, .14 + .06 * level, pan=.4)
        if i % 4 == 0:
            place(pluck(root - 12, length=.5, cutoff=400), t, .5 + .2 * level)
    if level == 2:
        place(brass(chord[0] + 12, BEAT * 1.5), at, .4, pan=-.2)
        place(brass(chord[2] + 12, BEAT * 1.5), at, .3, pan=.2)
    if level >= 1:
        place(pad([c + 12 for c in chord], BAR, cutoff=1400 + 500 * level, attack=.05, release=.4, width=.8), at, .25 + .1 * level)


# I. The road -------------------------------------------------------------------
place(pad([26, 38, 45], 24.0, cutoff=600, attack=5, release=1.0, width=.3), 0, .9)
for at, chord, length in ((0, [D, F, A], 6.6), (6.0, [Bb - 12, D, F], 5.6), (11.0, [G - 12, Bb, D], 5.6),
                          (16.0, [A - 12, C + 1, 64], 4.4), (20.0, [D, F, A, 62], 3.6)):
    place(pad(chord, length, cutoff=1000 + at * 45, attack=1.8, release=1.2), at, .7)
for at in (.8, 4.6, 8.2, 11.0, 13.6, 16.0, 18.0, 19.4):
    place(taiko(1.3, 2.6), at, .3 + at * .015, pan=-.25)
# đàn bầu under "đều phải đi qua con đường này" and "Còn người, còn đường".
place(dan_bau([(0, 69), (.9, 72), (1.5, 69), (2.2, 67), (2.8, 69)], 4.4), 6.6, .9, pan=.15)
place(dan_bau([(0, 74), (.7, 72), (1.2, 69), (1.9, 67), (2.4, 65), (3.0, 69)], 4.6), 16.2, 1.0, pan=.15)
# The drums gather and stop dead before the first card.
build = list(np.arange(19.2, 21.6, BEAT)) + list(np.arange(21.6, 22.8, BEAT / 2)) + list(np.arange(22.8, 23.4, BEAT / 4))
for i, at in enumerate(build):
    place(taiko(1.0, 1.0), at, .3 + .5 * i / len(build), pan=(-.3 if i % 2 else .3))
place(riser(4.2), 19.2, .55)
place(pad([62, 65, 69, 74], 4.2, cutoff=3500, attack=3.6, release=.1, width=.9), 19.2, .3)

# II. The threat ----------------------------------------------------------------
for k, at in enumerate(CARDS):
    hit(at, (.85, .9, .95, 1.1)[k])
    place(pad([26, 38], BAR + .6, cutoff=380, attack=.02, release=.8, width=.2), at, .8)
for start, end, level in PULSES:
    bars = int(round((end - start) / BAR))
    for b in range(bars):
        groove_bar(start + b * BAR, b, level)
    if level >= 1:
        place(braam(ROOTS[0] - 12, 4.0), start, .45)
    # A fill into the next card.
    for k in range(4):
        place(taiko(.85, .9), end - BEAT + k * BEAT / 4, .35 + .1 * level + k * .08)
place(riser(2.0), CARDS[3] - 2.0, .35)
# "1 người ở lại" and the soldier waiting: a heartbeat, high strings, a long riser, then air.
for k in range(4):
    place(taiko(1.5, 1.6), 50.4 + k * BEAT * 1.0, .45)
    place(taiko(1.5, 1.2), 50.4 + k * BEAT + .22, .3)
place(pad([62, 65, 69, 74, 77], 2.2, cutoff=3200, attack=1.6, release=.1, width=1.0), 50.4, .45)
place(riser(2.2), 50.3, .6)

# III. The battle ---------------------------------------------------------------
place(taiko(.75, 3.0), T_DROP, 1.25)
place(braam(26, 5.0), T_DROP, .9)
place(crash(4.5), T_DROP, .55)
battle_bars = int(round((T_SLOW - T_DROP) / BAR + .5))  # 6.5 bars, the last one cut short
for b in range(battle_bars):
    at = T_DROP + b * BAR
    groove_bar(at, b, 2, stop=T_SLOW)
    if b % 2 == 0:
        place(braam(ROOTS[b % 4] - 12, 4.4), at, .5)
    if b % 2 == 1:
        for k in range(4):
            place(taiko(.85, .9), at + 3 * BEAT + k * BEAT / 4, .5 + k * .12)
# The theme on brass: (beat, midi, beats) per four bars.
THEME = [(0, 69, 1.5), (1.5, 67, .5), (2, 69, 1), (3, 74, 1),
         (4, 72, 1.5), (5.5, 70, .5), (6, 69, 2),
         (8, 67, 1.5), (9.5, 69, .5), (10, 70, 1), (11, 74, 1),
         (12, 73, 2), (14, 69, 2)]
for rep in range(2):
    for beat, midi, beats in THEME:
        at = T_DROP + rep * 4 * BAR + beat * BEAT
        if at + beats * BEAT > T_SLOW - .1:
            continue
        place(brass(midi, beats * BEAT + .08), at, .5, pan=.1)
        place(brass(midi - 12, beats * BEAT + .08), at, .25, pan=-.15)

# Slow motion: the drums fall away under a held chord and a đàn bầu lament.
place(braam(22, 6.0), T_SLOW, .7)
place(pad([46, 50, 53, 58, 62], 4.9, cutoff=2000, attack=.3, release=1.0, width=.9), T_SLOW, .75)
for k in range(4):
    place(taiko(1.5, 2.2), T_SLOW + k * BAR / 2, .5)
place(dan_bau([(0, 74), (.6, 77), (1.1, 76), (1.6, 74), (2.4, 72), (3.0, 74)], 4.6), T_SLOW + .3, .9, pan=.1)
place(riser(2.4), T_FINAL - 2.4, .5)

# Finale: a brass call over a drum build into the title.
place(taiko(.8, 2.5), T_FINAL, 1.0)
place(crash(3.0), T_FINAL, .4)
call = [(0, 62, 1.5), (1.5, 65, .5), (2.0, 69, 1.0), (3.0, 72, 1.0)]
for beat, midi, beats in call:
    place(brass(midi, beats * BEAT + .1), T_FINAL + beat * BEAT, .6, pan=.1)
place(pad([c + 12 for c in CHORDS[0]] + [D], BAR, cutoff=2600, attack=.1, release=.3, width=.8), T_FINAL, .45)
for i in range(16):
    at = T_FINAL + i * BEAT / 4
    place(pluck(50 + OSTINATO[i]), at, .45, pan=(.35 if i % 2 else -.35))
for k in range(8):
    place(taiko(.9, .9), T_TITLE - BEAT * 2 + k * BEAT / 4, .45 + k * .07)

# IV. Title hit: D major, everything at once, a long tail.
place(taiko(.7, 4.0), T_TITLE, 1.4)
place(taiko(1.4, 5.0), T_TITLE + .02, .9)
place(braam(26, 8.0), T_TITLE, 1.0)
place(crash(6.0), T_TITLE, .55)
place(pad([38, 50, 54, 57, 62, 66, 69], 9.0, cutoff=2800, attack=.08, release=6.0, width=1.0), T_TITLE, .9)
place(brass(62, 3.5, swell=.03), T_TITLE, .5, pan=-.25)
place(brass(66, 3.5, swell=.03), T_TITLE, .4, pan=.25)
place(dan_bau([(0, 74), (.7, 76), (1.2, 74), (2.0, 69)], 4.2), T_TITLE + 3.0, .7, pan=.1)

# Stinger: a low pulse under the charger's run, the blast, then the end slate.
place(pad([26, 33], 2.3, cutoff=300, attack=1.6, release=.05, width=.2), T_BOOM - 2.2, .7)
for k in range(3):
    place(tom(38, .4), T_BOOM - 1.8 + k * BEAT, .25 + k * .08)
place(taiko(.6, 3.5), T_BOOM, 1.5)
place(taiko(1.2, 4.0), T_BOOM + .01, .9)
place(braam(26, 4.0), T_BOOM, .8)
place(crash(3.5), T_BOOM, .5)
place(pad([38, 50, 54, 57, 62, 69], 7.0, cutoff=1800, attack=1.5, release=3.5, width=1.0), T_SLATE, .55)
place(dan_bau([(0, 69), (.8, 74), (1.6, 72), (2.3, 69), (3.2, 74)], 5.0), T_SLATE + .8, .65, pan=.1)

finish(sys.argv[1], END, fade=1.2)
print(f"trailer score {END:.1f}s drop {T_DROP} slow {T_SLOW} title {T_TITLE} boom {T_BOOM}")
