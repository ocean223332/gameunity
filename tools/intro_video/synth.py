"""Synthesis kit shared by the intro and trailer scores: filters, instruments, mixing.

No samples or third-party audio; every sound is generated here with numpy.
Call begin(seconds), place() sounds, then finish(path, end).
"""
import wave

import numpy as np

SR = 48000
rng = np.random.default_rng(1972)
mix = None


def hz(midi):
    return 440.0 * 2 ** ((midi - 69) / 12)


def t_axis(seconds):
    return np.arange(int(seconds * SR)) / SR


def lowpass(x, cutoff, order=2):
    """Zero-phase FFT low-pass with a soft Butterworth-like slope."""
    n = len(x)
    size = 1 << (n - 1).bit_length()
    spec = np.fft.rfft(x, size)
    f = np.fft.rfftfreq(size, 1 / SR)
    spec *= 1 / np.sqrt(1 + (f / cutoff) ** (2 * order))
    return np.fft.irfft(spec, size)[:n]


def highpass(x, cutoff, order=2):
    n = len(x)
    size = 1 << (n - 1).bit_length()
    spec = np.fft.rfft(x, size)
    f = np.fft.rfftfreq(size, 1 / SR)
    spec *= 1 / np.sqrt(1 + (cutoff / np.maximum(f, 1e-3)) ** (2 * order))
    return np.fft.irfft(spec, size)[:n]


def bandpass(x, lo, hi):
    return highpass(lowpass(x, hi), lo)


def sweep_lowpass(x, start, end, steps=12):
    """Time-varying low-pass: crossfade between statically filtered copies."""
    cutoffs = np.geomspace(start, end, steps)
    copies = [lowpass(x, c) for c in cutoffs]
    pos = np.linspace(0, steps - 1, len(x))
    out = np.zeros_like(x)
    for i in range(steps):
        w = np.clip(1 - np.abs(pos - i), 0, 1)
        out += copies[i] * w
    return out


def saw(freq, t, phase=0.0):
    p = (freq * t + phase) % 1.0
    return 2 * p - 1


def adsr(n, a, d, s, r, sustain_time=None):
    a, d, r = int(a * SR), int(d * SR), int(r * SR)
    env = np.full(n, s, dtype=float)
    env[:a] = np.linspace(0, 1, a, endpoint=False) if a else env[:a]
    env[a:a + d] = np.linspace(1, s, len(env[a:a + d]))
    if r:
        env[-r:] *= np.linspace(1, 0, len(env[-r:]))
    return env


def begin(seconds):
    """Allocates the mix buffer for a cue of the given length."""
    global mix
    mix = np.zeros((2, int(seconds * SR) + SR * 4))


def place(signal, at, gain=1.0, pan=0.0):
    """Adds a mono or stereo signal at time `at` (seconds)."""
    start = int(at * SR)
    if signal.ndim == 1:
        left = signal * np.sqrt((1 - pan) / 2) * np.sqrt(2)
        right = signal * np.sqrt((1 + pan) / 2) * np.sqrt(2)
        signal = np.vstack([left, right])
    end = min(start + signal.shape[1], mix.shape[1])
    mix[:, start:end] += signal[:, :end - start] * gain


# ------------------------------------------------------------ instruments

def taiko(size=1.0, length=1.8):
    t = t_axis(length)
    f0 = 52 / size
    pitch = f0 * (1 + 1.6 * np.exp(-t * 28))
    body = np.sin(2 * np.pi * np.cumsum(pitch) / SR) * np.exp(-t * 3.2 / size)
    over = np.sin(2 * np.pi * np.cumsum(pitch * 2.3) / SR) * np.exp(-t * 9) * .35
    skin = lowpass(rng.standard_normal(len(t)), 900) * np.exp(-t * 30) * .9
    return np.tanh((body + over + skin) * 1.6)


def tom(midi, length=.7):
    t = t_axis(length)
    f = hz(midi) * (1 + .5 * np.exp(-t * 40))
    s = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 7)
    s += bandpass(rng.standard_normal(len(t)), 300, 2500) * np.exp(-t * 45) * .5
    return s


def crash(length=4.0):
    t = t_axis(length)
    n = highpass(rng.standard_normal(len(t)), 3500)
    return n * np.exp(-t * 1.3) * .5


def braam(root=26, length=4.5):
    """Detuned saw stack through a slowly opening, then closing filter."""
    t = t_axis(length)
    stack = np.zeros(len(t))
    for midi, g in ((root, 1), (root + 12, .8), (root + 19, .45), (root + 24, .3)):
        for det in (-.12, 0, .11):
            stack += saw(hz(midi + det), t, rng.random()) * g
    env = adsr(len(t), .04, .6, .55, 2.6)
    opened = sweep_lowpass(stack, 2600, 220, 14)
    return np.tanh(opened * env * .6) * 1.1


def pad(midis, length, cutoff=1400, attack=1.5, release=2.0, width=.5):
    t = t_axis(length)
    left = np.zeros(len(t))
    right = np.zeros(len(t))
    for midi in midis:
        for k, det in enumerate((-.09, -.03, .04, .1)):
            vib = 1 + .002 * np.sin(2 * np.pi * (4.8 + k * .3) * t + k)
            wave_ = saw(hz(midi + det) * vib, t, rng.random())
            if k % 2:
                left += wave_
            else:
                right += wave_
    env = adsr(len(t), attack, .1, 1, release)
    left = lowpass(left, cutoff) * env
    right = lowpass(right, cutoff) * env
    mid = (left + right) / 2
    return np.vstack([mid + (left - mid) * width * 2, mid + (right - mid) * width * 2]) / (len(midis) * 2.5)


def pluck(midi, length=.22, cutoff=2200):
    t = t_axis(length)
    s = saw(hz(midi), t) + saw(hz(midi + .07), t) * .7
    s = lowpass(s, cutoff) * np.exp(-t * 14)
    return s * .5


def brass(midi, length, swell=.08):
    t = t_axis(length)
    vib = 1 + .004 * np.sin(2 * np.pi * 5.2 * t) * np.clip(t * 2, 0, 1)
    s = sum(saw(hz(midi + d) * vib, t, rng.random()) for d in (-.06, 0, .07))
    s += sum(saw(hz(midi - 12 + d) * vib, t, rng.random()) for d in (-.05, .05)) * .5
    env = adsr(len(t), swell, .25, .75, min(.4, length * .4))
    bright = sweep_lowpass(s, 500, 2800, 8) if length > .6 else lowpass(s, 2200)
    return np.tanh(bright * env * .45)


def dan_bau(notes, length):
    """Monochord-like voice: sine plus soft harmonics, gliding between notes with vibrato.
    notes: list of (start, midi) breakpoints inside this phrase."""
    t = t_axis(length)
    pitch = np.zeros(len(t))
    times = [n[0] for n in notes] + [length]
    for i, (start, midi) in enumerate(notes):
        a, b = int(start * SR), int(times[i + 1] * SR)
        prev = notes[i - 1][1] if i else midi
        glide = np.clip((t[a:b] - start) / .12, 0, 1)
        pitch[a:b] = prev + (midi - prev) * (glide * glide * (3 - 2 * glide))
    vib = .28 * np.sin(2 * np.pi * 5.5 * t) * np.clip((t % 1.2) / .6, 0, 1)
    f = hz(pitch + vib)
    phase = 2 * np.pi * np.cumsum(f) / SR
    s = np.sin(phase) + .35 * np.sin(2 * phase) + .12 * np.sin(3 * phase) + .05 * np.sin(5 * phase)
    env = adsr(len(t), .05, .3, .8, 1.2)
    # each new note gets a soft pluck
    for start, _ in notes:
        a = int(start * SR)
        env[a:] *= 1 + .5 * np.exp(-(t[a:] - start) * 6)
    return s * env * .22


def riser(length):
    t = t_axis(length)
    n = rng.standard_normal(len(t))
    swept = sweep_lowpass(n, 300, 9000, 16)
    tone = np.sin(2 * np.pi * np.cumsum(np.geomspace(110, 880, len(t))) / SR) * .3
    env = (t / length) ** 2.2
    return (swept * .5 + tone) * env


def reverb(stereo, seconds=2.8, wet=.28):
    """Convolution with decaying, decorrelated noise for each channel."""
    n = int(seconds * SR)
    tt = np.arange(n) / SR
    out = np.zeros_like(stereo)
    for ch in range(2):
        ir = rng.standard_normal(n) * np.exp(-tt * 6.9 / seconds)
        ir = lowpass(ir, 5200)
        ir[:int(.012 * SR)] = 0
        ir /= np.sqrt(np.sum(ir ** 2))
        size = 1 << (stereo.shape[1] + n).bit_length()
        conv = np.fft.irfft(np.fft.rfft(stereo[ch], size) * np.fft.rfft(ir, size), size)[:stereo.shape[1]]
        out[ch] = stereo[ch] * (1 - wet) + conv * wet * 2.2
    return out


def finish(path, end, fade=1.5, wet=.3):
    """Reverb, soft clip and fade the mix, then write a 16-bit stereo WAV."""
    out = reverb(mix, 3.2, wet)
    out = np.vstack([highpass(out[0], 28), highpass(out[1], 28)])
    out /= np.max(np.abs(out)) + 1e-9
    out = np.tanh(out * 1.6) / np.tanh(1.6)
    out = out[:, :int(end * SR)]
    n = int(fade * SR)
    out[:, -n:] *= np.linspace(1, 0, n)
    out *= .89
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((out.T * 32767).astype("<i2").tobytes())
