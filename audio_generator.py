#!/usr/bin/env python3
"""
Procedural Audio Generator for Tiền Tuyến Video
Generates music and sound effects from scratch, synced to video timeline
"""
import numpy as np
import wave
import struct

# Audio configuration
SAMPLE_RATE = 44100
DURATION = 20  # seconds
TOTAL_SAMPLES = SAMPLE_RATE * DURATION

def generate_sine_wave(frequency, duration, amplitude=0.3, sample_rate=SAMPLE_RATE):
    """Generate a sine wave"""
    t = np.linspace(0, duration, int(sample_rate * duration), False)
    wave = amplitude * np.sin(2 * np.pi * frequency * t)
    return wave

def generate_noise(duration, amplitude=0.1, sample_rate=SAMPLE_RATE):
    """Generate white noise"""
    samples = int(sample_rate * duration)
    return amplitude * np.random.uniform(-1, 1, samples)

def apply_envelope(wave, attack=0.01, decay=0.1, sustain=0.7, release=0.2):
    """Apply ADSR envelope to audio wave"""
    n = len(wave)
    envelope = np.ones(n)

    # Attack
    attack_samples = int(attack * n)
    if attack_samples > 0:
        envelope[:attack_samples] = np.linspace(0, 1, attack_samples)

    # Decay
    decay_samples = int(decay * n)
    decay_end = min(attack_samples + decay_samples, n)
    if decay_end > attack_samples:
        actual_decay_samples = decay_end - attack_samples
        envelope[attack_samples:decay_end] = np.linspace(1, sustain, actual_decay_samples)

    # Sustain
    release_samples = int(release * n)
    sustain_end = max(decay_end, n - release_samples)
    if sustain_end > decay_end:
        envelope[decay_end:sustain_end] = sustain

    # Release
    if sustain_end < n:
        actual_release_samples = n - sustain_end
        envelope[sustain_end:] = np.linspace(sustain, 0, actual_release_samples)

    return wave * envelope

def generate_kick_drum(sample_rate=SAMPLE_RATE):
    """Generate kick drum sound"""
    duration = 0.5
    t = np.linspace(0, duration, int(sample_rate * duration), False)

    # Pitch envelope: high to low
    freq = 150 * np.exp(-t * 8)
    phase = 2 * np.pi * np.cumsum(freq) / sample_rate
    kick = np.sin(phase)

    # Amplitude envelope
    envelope = np.exp(-t * 10)
    kick = kick * envelope * 0.8

    return kick

def generate_snare(sample_rate=SAMPLE_RATE):
    """Generate snare drum sound"""
    duration = 0.2
    t = np.linspace(0, duration, int(sample_rate * duration), False)

    # Tone component
    tone = 0.3 * np.sin(2 * np.pi * 180 * t)

    # Noise component
    noise = 0.5 * np.random.uniform(-1, 1, len(t))

    # Combine and envelope
    snare = tone + noise
    envelope = np.exp(-t * 20)
    snare = snare * envelope

    return snare

def generate_hihat(sample_rate=SAMPLE_RATE):
    """Generate hi-hat sound"""
    duration = 0.1
    noise = np.random.uniform(-1, 1, int(sample_rate * duration))

    # High-pass filter (simple)
    filtered = np.diff(noise, prepend=0)

    # Envelope
    t = np.linspace(0, duration, len(filtered), False)
    envelope = np.exp(-t * 40)
    hihat = filtered * envelope * 0.3

    return hihat

def generate_bass_line(start_time, duration, root_freq=110, sample_rate=SAMPLE_RATE):
    """Generate bass line sequence"""
    bass = np.array([])

    # Note sequence (pentatonic-ish)
    note_pattern = [1, 1.5, 2, 1.5]  # Relative to root
    note_duration = duration / len(note_pattern)

    for multiplier in note_pattern:
        freq = root_freq * multiplier
        note = generate_sine_wave(freq, note_duration, amplitude=0.4, sample_rate=sample_rate)
        # Add harmonics
        note += 0.2 * generate_sine_wave(freq * 2, note_duration, amplitude=0.2, sample_rate=sample_rate)
        note = apply_envelope(note, attack=0.01, decay=0.05, sustain=0.6, release=0.1)
        bass = np.concatenate([bass, note])

    return bass

def generate_melody(start_time, duration, root_freq=440, sample_rate=SAMPLE_RATE):
    """Generate melody line"""
    melody = np.array([])

    # Melody pattern (Vietnamese-inspired pentatonic)
    # Using frequencies from pentatonic scale
    frequencies = [
        root_freq * 1.0,      # Root
        root_freq * 1.125,    # Second
        root_freq * 1.265,    # Minor third
        root_freq * 1.5,      # Fifth
        root_freq * 1.688,    # Minor seventh
    ]

    # Pattern
    note_pattern = [0, 2, 4, 2, 3, 1, 0]
    note_duration = duration / len(note_pattern)

    for note_idx in note_pattern:
        freq = frequencies[note_idx % len(frequencies)]
        note = generate_sine_wave(freq, note_duration, amplitude=0.15, sample_rate=sample_rate)
        note = apply_envelope(note, attack=0.02, decay=0.1, sustain=0.5, release=0.15)
        melody = np.concatenate([melody, note])

    return melody

def generate_gunshot(sample_rate=SAMPLE_RATE):
    """Generate gunshot sound effect"""
    duration = 0.15
    t = np.linspace(0, duration, int(sample_rate * duration), False)

    # Sharp attack with noise and low freq thump
    noise = np.random.uniform(-1, 1, len(t))
    thump = np.sin(2 * np.pi * 80 * t)

    shot = 0.6 * noise + 0.4 * thump

    # Very fast decay
    envelope = np.exp(-t * 30)
    shot = shot * envelope * 0.6

    return shot

def generate_explosion(sample_rate=SAMPLE_RATE):
    """Generate explosion sound effect"""
    duration = 0.8
    t = np.linspace(0, duration, int(sample_rate * duration), False)

    # Low frequency rumble with noise
    rumble = np.sin(2 * np.pi * 60 * t * np.exp(-t * 2))
    noise = np.random.uniform(-1, 1, len(t))

    explosion = 0.5 * rumble + 0.5 * noise

    # Envelope
    envelope = np.exp(-t * 3)
    explosion = explosion * envelope * 0.7

    return explosion

def mix_audio_at_time(mix, audio_clip, start_time, sample_rate=SAMPLE_RATE):
    """Mix audio clip into main mix at specified time"""
    start_sample = int(start_time * sample_rate)
    end_sample = start_sample + len(audio_clip)

    if end_sample > len(mix):
        end_sample = len(mix)
        audio_clip = audio_clip[:end_sample - start_sample]

    mix[start_sample:end_sample] += audio_clip

    return mix

def generate_complete_audio():
    """Generate complete audio track synced to video timeline"""
    print("Generating audio track...")

    # Initialize empty mix
    mix = np.zeros(TOTAL_SAMPLES)

    # === SCENE 1: Title (0-3s) ===
    print("Scene 1: Title reveal (0-3s)")

    # Deep bass hit at start
    bass_hit = generate_kick_drum()
    mix = mix_audio_at_time(mix, bass_hit * 1.5, 0.0)

    # Rising tension chord
    for i, freq in enumerate([220, 277, 330]):  # Am chord
        chord_note = generate_sine_wave(freq, 2.5, amplitude=0.1)
        chord_note = apply_envelope(chord_note, attack=0.5, decay=0.5, sustain=0.6, release=0.5)
        mix = mix_audio_at_time(mix, chord_note, 0.5 + i * 0.05)

    # Impact at 2s when title fully appears
    impact = generate_explosion()
    mix = mix_audio_at_time(mix, impact * 0.5, 2.0)

    # === SCENE 2: Gameplay (3-13s) ===
    print("Scene 2: Gameplay showcase (3-13s)")

    # Drum beat (tension building)
    beat_times = np.arange(3.0, 13.0, 0.5)  # Every 0.5s
    for i, t in enumerate(beat_times):
        if i % 4 == 0:
            # Kick on downbeat
            kick = generate_kick_drum()
            mix = mix_audio_at_time(mix, kick, t)
        elif i % 4 == 2:
            # Snare on offbeat
            snare = generate_snare()
            mix = mix_audio_at_time(mix, snare, t)

        # Hi-hat every beat
        hihat = generate_hihat()
        mix = mix_audio_at_time(mix, hihat, t)

    # Bass line loops (2.5s loops)
    for start_t in np.arange(3.0, 13.0, 2.5):
        bass = generate_bass_line(start_t, 2.5, root_freq=110)
        mix = mix_audio_at_time(mix, bass, start_t)

    # Melody over gameplay
    for start_t in np.arange(4.0, 13.0, 3.0):
        melody = generate_melody(start_t, 3.0, root_freq=440)
        mix = mix_audio_at_time(mix, melody, start_t)

    # Gunshots at intervals
    gunshot_times = [3.5, 4.2, 5.1, 6.0, 7.3, 8.5, 9.2, 10.5, 11.8]
    for t in gunshot_times:
        gunshot = generate_gunshot()
        mix = mix_audio_at_time(mix, gunshot * 0.4, t)

    # === SCENE 3: Features (13-17s) ===
    print("Scene 3: Features highlight (13-17s)")

    # Stinger hits for each feature reveal
    feature_times = [13.0, 14.0, 15.0]
    for t in feature_times:
        # Hit sound
        hit = generate_kick_drum()
        mix = mix_audio_at_time(mix, hit * 1.2, t)

        # Rising sweep
        sweep_duration = 0.5
        sweep_t = np.linspace(0, sweep_duration, int(SAMPLE_RATE * sweep_duration), False)
        sweep_freq = 200 * np.exp(sweep_t * 3)
        phase = 2 * np.pi * np.cumsum(sweep_freq) / SAMPLE_RATE
        sweep = 0.2 * np.sin(phase)
        sweep = sweep * np.exp(-sweep_t * 2)
        mix = mix_audio_at_time(mix, sweep, t + 0.1)

    # Continuing beat
    for t in np.arange(13.0, 17.0, 0.5):
        hihat = generate_hihat()
        mix = mix_audio_at_time(mix, hihat * 0.8, t)

    # === SCENE 4: Ending (17-20s) ===
    print("Scene 4: Coming soon (17-20s)")

    # Final impact
    final_impact = generate_explosion()
    mix = mix_audio_at_time(mix, final_impact * 0.8, 17.0)

    # Resolving chord (Am to E)
    resolve_chord_freqs = [165, 207, 247, 330]  # E major-ish
    for i, freq in enumerate(resolve_chord_freqs):
        note = generate_sine_wave(freq, 3.0, amplitude=0.15)
        note = apply_envelope(note, attack=0.3, decay=0.5, sustain=0.7, release=1.5)
        mix = mix_audio_at_time(mix, note, 17.0 + i * 0.05)

    # Fading heartbeat
    for t in [18.0, 18.8, 19.6]:
        heartbeat = generate_kick_drum()
        fade = 1.0 - (t - 18.0) / 2.0
        mix = mix_audio_at_time(mix, heartbeat * fade * 0.6, t)

    # === Master Processing ===
    print("Applying master processing...")

    # Normalize to prevent clipping
    max_val = np.max(np.abs(mix))
    if max_val > 0:
        mix = mix / max_val * 0.95  # Leave headroom

    # Add subtle tape saturation
    mix = np.tanh(mix * 1.2) * 0.9

    # Fade out last 0.5s
    fade_start = TOTAL_SAMPLES - int(0.5 * SAMPLE_RATE)
    fade_curve = np.linspace(1, 0, TOTAL_SAMPLES - fade_start)
    mix[fade_start:] *= fade_curve

    print("✓ Audio generation complete!")
    return mix

def save_wav(filename, audio_data, sample_rate=SAMPLE_RATE):
    """Save audio data as WAV file"""
    # Convert to 16-bit PCM
    audio_int16 = np.int16(audio_data * 32767)

    with wave.open(filename, 'w') as wav_file:
        # Set parameters: nchannels, sampwidth, framerate, nframes, comptype, compname
        wav_file.setnchannels(1)  # Mono
        wav_file.setsampwidth(2)  # 2 bytes = 16 bits
        wav_file.setframerate(sample_rate)
        wav_file.writeframes(audio_int16.tobytes())

    print(f"Saved audio to: {filename}")

def main():
    """Main audio generation pipeline"""
    print("=== Tiền Tuyến Audio Generator ===")
    print(f"Generating {DURATION}s audio at {SAMPLE_RATE}Hz...")

    # Create output directory
    import os
    os.makedirs('video_output', exist_ok=True)

    # Generate complete audio
    audio = generate_complete_audio()

    # Save as WAV
    output_file = 'video_output/audio.wav'
    save_wav(output_file, audio)

    print("\n✓ Audio file ready!")
    print(f"Duration: {DURATION}s")
    print(f"Sample rate: {SAMPLE_RATE}Hz")

if __name__ == '__main__':
    main()
