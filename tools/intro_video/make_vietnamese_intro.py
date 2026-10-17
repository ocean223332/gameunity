import os
import sys
import numpy as np
import miniaudio
import imageio_ffmpeg
import subprocess

SR = 48000
ffmpeg_exe = imageio_ffmpeg.get_ffmpeg_exe()

def hz(midi):
    return 440.0 * (2.0 ** ((midi - 69) / 12.0))

def lowpass(x, cutoff, order=2):
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

def radio_filter(audio, noise_gain=0.015):
    """Simulates a 1970s portable AM radio speaker."""
    # Bandpass 320Hz to 3200Hz
    filtered = bandpass(audio, 320, 3200)
    # Gentle saturation/warmth
    sat = np.tanh(filtered * 2.2) * 0.7
    # Soft background transistor hiss
    np.random.seed(1972)
    hiss = bandpass(np.random.normal(0, 1, len(audio)), 400, 3000) * noise_gain
    return sat + hiss

def synth_vov_fanfare(length=3.5):
    """Synthesises the iconic VOV trumpet/brass fanfare (Diệt phát xít opening motif)."""
    t = np.arange(int(length * SR)) / SR
    # Motif: F4, A4, C5, F5 (high), D5, C5, A4, F4
    notes = [
        (0.0, 0.35, 65),   # F4
        (0.35, 0.35, 69),  # A4
        (0.70, 0.35, 72),  # C5
        (1.05, 0.80, 77),  # F5
        (1.85, 0.35, 74),  # D5
        (2.20, 0.35, 72),  # C5
        (2.55, 0.35, 69),  # A4
        (2.90, 0.60, 65),  # F4
    ]
    sig = np.zeros(len(t))
    for start, dur, midi in notes:
        st_idx = int(start * SR)
        end_idx = min(st_idx + int(dur * SR), len(t))
        note_t = t[st_idx:end_idx] - start
        f = hz(midi)
        # Brass harmonic stack
        s = np.sin(2 * np.pi * f * note_t) + \
            0.6 * np.sin(4 * np.pi * f * note_t) + \
            0.35 * np.sin(6 * np.pi * f * note_t) + \
            0.2 * np.sin(8 * np.pi * f * note_t)
        # Envelope
        env = np.ones(len(note_t))
        att = int(0.04 * SR)
        rel = int(0.08 * SR)
        if len(env) > att:
            env[:att] = np.linspace(0, 1, att)
        if len(env) > rel:
            env[-rel:] *= np.linspace(1, 0, rel)
        sig[st_idx:end_idx] += s * env * 0.45
    return sig

def synth_truong_son_song(length=27.0):
    """Synthesises an evocative acoustic/đàn bầu folk melody for the radio."""
    t = np.arange(int(length * SR)) / SR
    sig = np.zeros(len(t))
    
    # Đàn bầu melodic phrases (Truong Son melody inspired by Bai ca ben canh vong / Duong Truong Son)
    # Pentatonic scale in D: D, F, G, A, C (Midis: 62, 65, 67, 69, 72, 74, 77)
    dan_bau_phrases = [
        # Phrase 1: Dừng chân bên suối võng đưa (0s - 7s)
        [(0.0, 62), (1.2, 65), (2.4, 67), (3.6, 69), (4.8, 67), (5.8, 65)],
        # Phrase 2: Nhìn trời cao trong xanh lồng lộng (7s - 14s)
        [(7.2, 67), (8.4, 69), (9.6, 72), (10.8, 74), (12.2, 72), (13.2, 69)],
        # Phrase 3: Kỷ niệm quê hương biết mấy thân thương (14s - 21s)
        [(14.5, 74), (15.8, 77), (17.0, 74), (18.2, 72), (19.4, 69), (20.4, 67)],
        # Phrase 4: Cho từng chuyến xe anh qua (21s - 27s)
        [(21.6, 67), (22.8, 69), (24.0, 65), (25.2, 62)]
    ]
    
    for phrase in dan_bau_phrases:
        times = [p[0] for p in phrase] + [phrase[-1][0] + 1.8]
        for i, (start, midi) in enumerate(phrase):
            st_idx = int(start * SR)
            end_idx = min(int(times[i+1] * SR), len(t))
            if st_idx >= len(t):
                break
            seg_t = t[st_idx:end_idx] - start
            f = hz(midi)
            # Smooth vibrato
            vib = 1.0 + 0.008 * np.sin(2 * np.pi * 5.2 * seg_t)
            phase = 2 * np.pi * np.cumsum(f * vib) / SR
            # Monochord harmonic spectrum
            s = np.sin(phase) + 0.4 * np.sin(2 * phase) + 0.18 * np.sin(3 * phase) + 0.08 * np.sin(5 * phase)
            env = np.exp(-seg_t * 1.5)
            att = int(0.03 * SR)
            if len(env) > att:
                env[:att] = np.linspace(0, 1, att)
            sig[st_idx:end_idx] += s * env * 0.4
            
    # Add gentle acoustic guitar arpeggios underneath
    chords = [
        [50, 57, 62, 65],  # Dm
        [46, 53, 58, 62],  # Bb
        [43, 50, 55, 58],  # Gm
        [45, 52, 57, 60],  # Am
    ]
    for b in range(13):
        chord = chords[b % len(chords)]
        bar_start = b * 2.0
        for k, note in enumerate(chord):
            note_time = bar_start + k * 0.5
            st_idx = int(note_time * SR)
            if st_idx >= len(t):
                break
            end_idx = min(st_idx + int(1.2 * SR), len(t))
            seg_t = t[st_idx:end_idx] - note_time
            f = hz(note)
            s = (np.sin(2 * np.pi * f * seg_t) + 0.5 * np.sin(4 * np.pi * f * seg_t)) * np.exp(-seg_t * 4.5)
            sig[st_idx:end_idx] += s * 0.18
            
    return sig

def load_mono(filepath):
    decoded = miniaudio.decode_file(filepath, nchannels=1, sample_rate=SR)
    return np.frombuffer(decoded.samples, dtype=np.int16).astype(float) / 32768.0

def load_stereo(filepath):
    decoded = miniaudio.decode_file(filepath, nchannels=2, sample_rate=SR)
    samples = np.frombuffer(decoded.samples, dtype=np.int16).astype(float) / 32768.0
    return samples.reshape(-1, 2)

def main():
    print("Building Vietnamese Intro Audio Track (120s)...")
    total_samples = 120 * SR
    master_l = np.zeros(total_samples, dtype=float)
    master_r = np.zeros(total_samples, dtype=float)
    
    # 1. Load original full audio track
    print("Loading original video sound effects (blasts, gunfire, timing)...")
    orig = load_stereo('original_intro_audio.wav')
    if len(orig) < total_samples:
        orig = np.pad(orig, ((0, total_samples - len(orig)), (0, 0)))
    else:
        orig = orig[:total_samples]
        
    # Keep the sound effects from original:
    # 42.5s to 50s: The bomb whistle, blast impact, debris, radio cut
    # 90s to 120s: The camera ascent, distant AK-47 shots, title drop impact
    fx_mask = np.zeros(total_samples, dtype=float)
    # Bomb explosion transition: 42.5s to 50s
    idx_42 = int(42.5 * SR)
    idx_43 = int(43.5 * SR)
    idx_50 = int(50.0 * SR)
    idx_52 = int(52.0 * SR)
    fx_mask[idx_42:idx_43] = np.linspace(0, 1, idx_43 - idx_42)
    fx_mask[idx_43:idx_50] = 1.0
    fx_mask[idx_50:idx_52] = np.linspace(1, 0, idx_52 - idx_50)
    
    # In-game battle ambient & title hit: 90s to 120s
    idx_90 = int(90.0 * SR)
    idx_92 = int(92.0 * SR)
    fx_mask[idx_90:idx_92] = np.linspace(0, 0.7, idx_92 - idx_90)
    fx_mask[idx_92:] = 0.7
    
    master_l += orig[:, 0] * fx_mask
    master_r += orig[:, 1] * fx_mask
    
    # 2. Build Radio Section (0.0s to 43.0s)
    print("Synthesizing 1972 Radio broadcast in A-frame shelter...")
    radio_len = int(43.0 * SR)
    radio_sig = np.zeros(radio_len, dtype=float)
    
    # A. Radio switch on click & static tuning (0s - 1.8s)
    t_tune = np.arange(int(1.8 * SR)) / SR
    np.random.seed(42)
    crackle = np.random.normal(0, 0.15, len(t_tune)) * np.sin(2 * np.pi * 120 * t_tune)
    crackle[:int(0.05 * SR)] += np.random.normal(0, 0.8, int(0.05 * SR)) # switch click
    radio_sig[:len(t_tune)] += crackle * 0.4
    
    # B. VOV Brass Fanfare (1.5s - 5.0s)
    fanfare = synth_vov_fanfare(3.5)
    f_start = int(1.5 * SR)
    f_end = min(f_start + len(fanfare), radio_len)
    radio_sig[f_start:f_end] += fanfare[:f_end - f_start] * 0.55
    
    # C. VOV Station ID voice (5.2s - 11.4s)
    vov_id = load_mono('vov_id.mp3')
    v_start = int(5.2 * SR)
    v_end = min(v_start + len(vov_id), radio_len)
    radio_sig[v_start:v_end] += vov_id[:v_end - v_start] * 0.95
    
    # D. Radio announcement (11.6s - 17.6s)
    radio_ann = load_mono('radio_ann.mp3')
    a_start = int(11.6 * SR)
    a_end = min(a_start + len(radio_ann), radio_len)
    radio_sig[a_start:a_end] += radio_ann[:a_end - a_start] * 0.9
    
    # E. Revolutionary Truong Son song (17.5s - 42.0s)
    song = synth_truong_son_song(24.5)
    s_start = int(17.5 * SR)
    s_end = min(s_start + len(song), radio_len)
    radio_sig[s_start:s_end] += song[:s_end - s_start] * 0.75
    
    # Apply 1972 AM Radio Filter to the entire radio signal
    radio_filtered = radio_filter(radio_sig, noise_gain=0.01)
    
    # Sudden radio cut at 42.8s when bomb explodes
    cut_idx = int(42.8 * SR)
    radio_filtered[cut_idx:] = 0.0
    # Add to master (panned slightly center-left where the radio sits on the crate)
    master_l[:radio_len] += radio_filtered * 0.85
    master_r[:radio_len] += radio_filtered * 0.65
    
    # 3. Combat alert call (40.2s - 42.8s) right before the bomb hit
    print("Adding bunker alert shout...")
    alert_voice = load_mono('alert_soldier.mp3')
    al_start = int(40.2 * SR)
    al_end = min(al_start + len(alert_voice), total_samples)
    alert_len = al_end - al_start
    # Filter with distant echo/bunker acoustics
    al_filtered = bandpass(alert_voice[:alert_len], 250, 4000) * 0.9
    master_l[al_start:al_end] += al_filtered * 0.8
    master_r[al_start:al_end] += al_filtered * 0.8
    
    # 4. Pristine Adagio for Strings (50.0s to 112.0s)
    print("Mixing master Adagio for Strings score...")
    adagio = load_stereo('TienTuyen/Assets/_TienTuyen/Audio/Resources/CombatAudio/Music_Menu_Adagio.ogg')
    ad_start = int(50.0 * SR)
    ad_end = int(112.0 * SR)
    ad_dur = ad_end - ad_start
    ad_chunk = adagio[:ad_dur].copy()
    
    # Fade in Adagio softly from 50s to 54s
    fade_in = int(4.0 * SR)
    ad_chunk[:fade_in, 0] *= np.linspace(0, 1, fade_in)
    ad_chunk[:fade_in, 1] *= np.linspace(0, 1, fade_in)
    
    # Smooth fade out before title cut at 112s
    fade_out = int(2.5 * SR)
    ad_chunk[-fade_out:, 0] *= np.linspace(1, 0, fade_out)
    ad_chunk[-fade_out:, 1] *= np.linspace(1, 0, fade_out)
    
    master_l[ad_start:ad_end] += ad_chunk[:, 0] * 0.82
    master_r[ad_start:ad_end] += ad_chunk[:, 1] * 0.82
    
    # 5. Vietnamese Narration (54.0s - 72.0s)
    print("Mixing Vietnamese narration over the Adagio...")
    narration = load_mono('narration_vn.mp3')
    n_start = int(54.0 * SR)
    n_end = min(n_start + len(narration), total_samples)
    n_len = n_end - n_start
    
    # Reverb / room acoustics for the narration
    n_clean = narration[:n_len]
    # Simple warm stereo diffusion
    master_l[n_start:n_end] += n_clean * 0.95
    master_r[n_start:n_end] += n_clean * 0.95
    
    # 6. Normalize and soft clip
    print("Mastering final 24-bit/48kHz audio...")
    peak = max(np.max(np.abs(master_l)), np.max(np.abs(master_r)))
    if peak > 0:
        master_l = np.tanh((master_l / peak) * 1.3) * 0.92
        master_r = np.tanh((master_r / peak) * 1.3) * 0.92
        
    stereo_out = np.vstack([master_l, master_r]).T
    out_int16 = (stereo_out * 32767.0).astype(np.int16)
    
    wav_out = 'TienTuyenIntro_VN.wav'
    import wave
    with wave.open(wav_out, 'wb') as wf:
        wf.setnchannels(2)
        wf.setsampwidth(2)
        wf.setframerate(SR)
        wf.writeframes(out_int16.tobytes())
    print(f"Generated clean WAV: {wav_out} ({len(master_l)/SR:.1f}s)")
    
    # 7. Use ffmpeg to mux into TienTuyenIntro.mp4 preserving pristine 1080p video
    print("Muxing into final TienTuyenIntro.mp4 (H.264 + AAC 48kHz stereo)...")
    mp4_target = 'TienTuyen/Assets/StreamingAssets/TienTuyenIntro.mp4'
    temp_mp4 = 'TienTuyenIntro_VN.mp4'
    
    cmd = [
        ffmpeg_exe, '-y',
        '-i', mp4_target,       # input 0: original 1080p video
        '-i', wav_out,          # input 1: new Vietnamese audio
        '-c:v', 'copy',         # copy video stream untouched
        '-c:a', 'aac', '-b:a', '192k', '-ar', '48000',
        '-map', '0:v:0',        # take video from 0
        '-map', '1:a:0',        # take audio from 1
        '-shortest',
        temp_mp4
    ]
    res = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if res.returncode == 0:
        # Overwrite in Assets/StreamingAssets
        import shutil
        shutil.move(temp_mp4, mp4_target)
        print("Successfully updated TienTuyen/Assets/StreamingAssets/TienTuyenIntro.mp4!")
        
        # Also update in Builds/Windows/TienTuyen_Data/StreamingAssets if it exists
        build_target = 'Builds/Windows/TienTuyen_Data/StreamingAssets/TienTuyenIntro.mp4'
        if os.path.exists(os.path.dirname(build_target)):
            shutil.copy2(mp4_target, build_target)
            print("Successfully updated Builds/Windows/TienTuyen_Data/StreamingAssets/TienTuyenIntro.mp4!")
    else:
        print("FFmpeg error:", res.stderr.decode('utf-8', 'ignore'))

if __name__ == '__main__':
    main()
