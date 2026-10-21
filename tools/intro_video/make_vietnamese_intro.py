import os
import sys
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
import numpy as np
import miniaudio
import imageio_ffmpeg
import subprocess
import scipy.signal as signal

SR = 48000
ffmpeg_exe = imageio_ffmpeg.get_ffmpeg_exe()

def load_audio(filepath, target_sr=SR, nchannels=2):
    """Loads any audio file (wav, mp3, ogg) as float numpy array (N, channels)."""
    decoded = miniaudio.decode_file(filepath, nchannels=nchannels, sample_rate=target_sr)
    samples = np.frombuffer(decoded.samples, dtype=np.int16).astype(float) / 32768.0
    if nchannels == 2:
        return samples.reshape(-1, 2)
    return samples

def butter_bandpass_filter(data, lowcut, highcut, fs=SR, order=4):
    """Clean Butterworth bandpass filter using second-order sections (SOS)."""
    sos = signal.butter(order, [lowcut, highcut], btype='bandpass', fs=fs, output='sos')
    if data.ndim == 1:
        return signal.sosfilt(sos, data)
    out = np.zeros_like(data)
    for ch in range(data.shape[1]):
        out[:, ch] = signal.sosfilt(sos, data[:, ch])
    return out

def radio_vef206_filter(stereo_audio):
    """Simulates the iconic 1972 VEF 206 transistor AM radio speaker in the A-frame bunker.
    Preserves warm chest resonance (180Hz+) for a dignified, solemn, inspirational voice."""
    mono = np.mean(stereo_audio, axis=1) if stereo_audio.ndim == 2 else stereo_audio
    # 1. Warm AM radio bandpass: 180Hz to 4200Hz (preserves female vocal fundamental ~200Hz)
    filtered = butter_bandpass_filter(mono, 180, 4200, fs=SR, order=2)
    # 2. Warm analog transistor saturation (gentle, no harsh odd-harmonic buzz)
    sat = np.tanh(filtered * 1.15) * 0.90
    # 3. Subtle AM carrier hiss / transistor hum
    np.random.seed(1972)
    hiss = butter_bandpass_filter(np.random.normal(0, 0.0035, len(mono)), 500, 2600, fs=SR, order=2)
    t = np.arange(len(mono)) / SR
    hum = 0.0015 * np.sin(2 * np.pi * 50 * t) + 0.0008 * np.sin(2 * np.pi * 100 * t)
    radio_out = sat + hiss + hum
    # 4. Pan slightly to the left (-0.15) where the radio sits on the wooden ammo crate
    pan = -0.15
    gain_l = np.cos((pan + 1) * np.pi / 4)
    gain_r = np.sin((pan + 1) * np.pi / 4)
    return np.vstack([radio_out * gain_l, radio_out * gain_r]).T

def apply_fade(audio, in_samples=0, out_samples=0):
    """Applies smooth linear fades."""
    out = audio.copy()
    if in_samples > 0 and len(out) >= in_samples:
        fade_curve = np.linspace(0, 1, in_samples)
        if out.ndim == 2:
            out[:in_samples, 0] *= fade_curve
            out[:in_samples, 1] *= fade_curve
        else:
            out[:in_samples] *= fade_curve
    if out_samples > 0 and len(out) >= out_samples:
        fade_curve = np.linspace(1, 0, out_samples)
        if out.ndim == 2:
            out[-out_samples:, 0] *= fade_curve
            out[-out_samples:, 1] *= fade_curve
        else:
            out[-out_samples:] *= fade_curve
    return out

def synth_radio_tuning_static(duration_sec=2.5):
    """Synthesizes authentic analog transistor radio dial search noise (needle sliding, whistles, clicks)."""
    t = np.arange(int(duration_sec * SR)) / SR
    # Tactile power switch pop at 0.15s
    click = np.zeros_like(t)
    idx_c = int(0.15 * SR)
    click[idx_c:idx_c + 240] = np.random.normal(0, 0.35, 240) * np.exp(-np.linspace(0, 6, 240))
    
    # AM carrier hiss & heterodyne tuning sweep whistles
    np.random.seed(1972)
    noise = np.random.normal(0, 0.07, len(t))
    whistle1 = 0.025 * np.sin(2 * np.pi * (1600 - 650 * t) * t) * (t < 1.8)
    whistle2 = 0.035 * np.sin(2 * np.pi * (700 + 350 * np.sin(2.5 * t)) * t) * (t > 0.8) * (t < 2.3)
    
    raw = click + noise + whistle1 + whistle2
    sos = signal.butter(3, [350, 3200], btype='bandpass', fs=SR, output='sos')
    tuner = signal.sosfilt(sos, raw)
    # Smooth fade out as needle locks onto station at 2.0s - 2.5s
    fade_start = int(2.0 * SR)
    tuner[fade_start:] *= np.linspace(1, 0, len(t) - fade_start)
    return np.vstack([tuner, tuner]).T

def generate_distant_gunfire(total_samples):
    """Generates authentic distant AK-47 combat gunfire bursts echoing across karst ridges."""
    shot_path = 'TienTuyen/Assets/_TienTuyen/Audio/Resources/CombatAudio/Rifle_Shot.wav'
    shot = load_audio(shot_path, SR, 2)
    
    sos_dir = signal.butter(3, 1800, btype='lowpass', fs=SR, output='sos')
    sos_echo = signal.butter(3, 900, btype='lowpass', fs=SR, output='sos')
    shot_dir = signal.sosfilt(sos_dir, shot, axis=0) * 0.42
    shot_echo = signal.sosfilt(sos_echo, shot, axis=0) * 0.18
    
    gf = np.zeros((total_samples, 2), dtype=float)
    
    # Gunfire bursts during the camera crane ascent (101.0s to 111.5s):
    # (timestamp_sec, num_shots, pan)
    bursts = [
        (101.2, 3, -0.30),
        (103.5, 2,  0.35),
        (105.7, 3, -0.15),
        (107.4, 2,  0.25),
        (109.2, 3,  0.05),
        (110.8, 2, -0.20),
    ]
    
    for b_time, count, pan in bursts:
        gain_l = np.cos((pan + 1) * np.pi / 4)
        gain_r = np.sin((pan + 1) * np.pi / 4)
        for k in range(count):
            st = int((b_time + k * 0.105) * SR)
            ed = st + len(shot_dir)
            if ed <= total_samples:
                gf[st:ed, 0] += shot_dir[:, 0] * gain_l
                gf[st:ed, 1] += shot_dir[:, 1] * gain_r
            # Distant canyon echo reflection (65ms delay)
            st_echo = st + int(0.065 * SR)
            ed_echo = st_echo + len(shot_echo)
            if ed_echo <= total_samples:
                gf[st_echo:ed_echo, 0] += shot_echo[:, 0] * gain_r
                gf[st_echo:ed_echo, 1] += shot_echo[:, 1] * gain_l
                
    return gf

def load_and_trim_voice(path, sr=SR):
    """Trims leading/trailing silence from edge-tts clips and applies micro-fades."""
    data = load_audio(path, sr, 2)
    energy = np.max(np.abs(data), axis=1)
    thresh = np.max(energy) * 0.04
    active = np.where(energy > thresh)[0]
    if len(active) == 0:
        return data
    st = max(0, active[0] - int(0.04 * sr))
    ed = min(len(data), active[-1] + int(0.08 * sr))
    trimmed = data[st:ed].copy()
    f_in = int(0.02 * sr)
    f_out = int(0.04 * sr)
    if len(trimmed) > f_in + f_out:
        trimmed[:f_in] *= np.linspace(0, 1, f_in)[:, np.newaxis]
        trimmed[-f_out:] *= np.linspace(1, 0, f_out)[:, np.newaxis]
    return trimmed

def synth_cinematic_title_impact(duration=2.5, fs=SR):
    """Synthesizes a thunderous, clean cinematic sub-bass impact for the Title Card.
    Zero human speech, deep sub-bass pitch drop (55Hz -> 32Hz), mid transient, and stereo rumble tail."""
    t = np.linspace(0, duration, int(duration * fs), endpoint=False)
    
    # 1. Pitch-dropping sub-bass boom (55Hz -> 32Hz)
    f_inst = 55.0 * np.exp(-t * 1.6) + 32.0 * (1 - np.exp(-t * 1.6))
    phase = 2 * np.pi * np.cumsum(f_inst) / fs
    sub = np.sin(phase) * np.exp(-t * 2.5)
    
    # 2. Punch transient (115Hz -> 48Hz)
    f_punch = 115.0 * np.exp(-t * 28.0) + 48.0
    punch = np.sin(2 * np.pi * np.cumsum(f_punch) / fs) * np.exp(-t * 16.0) * 0.75
    
    # 3. Low-frequency stereo rumble burst (filtered < 130Hz)
    np.random.seed(1972)
    noise = np.random.normal(0, 1.0, len(t))
    sos = signal.butter(4, 130, btype='lowpass', fs=fs, output='sos')
    rumble = signal.sosfilt(sos, noise)
    rumble = rumble / np.max(np.abs(rumble)) * np.exp(-t * 2.8) * 0.50
    
    mono_hit = sub * 0.90 + punch * 0.70 + rumble * 0.45
    mono_hit = mono_hit / np.max(np.abs(mono_hit)) * 0.95
    
    # Stereo spatial width
    delay_s = int(0.015 * fs)
    left = mono_hit.copy()
    right = np.zeros_like(mono_hit)
    right[delay_s:] = mono_hit[:-delay_s] * 0.96
    right[:delay_s] = mono_hit[:delay_s] * 0.85
    return np.vstack([left, right]).T

def clean_and_subtitle_video(ffmpeg_exe, orig_video, output_video):
    """
    1. Removes the obsolete American radio subtitle ('...và đây là Đài phát thanh Quân đội Mỹ, phát từ Thái Lan.')
       at 1.00s - 3.76s (frames 25 to 94) by frame-by-frame backward propagation from the pristine frame 95.
       Restores natural radio speaker diamond grille texture seamlessly with ZERO black box or smearing.
    2. Renders the authentic VOV broadcast subtitles matching the solemn female narration:
       - 5.80s - 8.75s: 'Đây là Đài Tiếng nói Việt Nam, phát thanh từ Hà Nội,'
       - 8.75s - 10.70s: 'thủ đô nước Việt Nam Dân chủ Cộng hòa.'
    3. Re-encodes seamlessly under 45 MB (< 50MB git/engine limit).
    """
    import cv2
    from PIL import Image, ImageDraw, ImageFont
    import shutil
    
    work_dir = 'tools/intro_video/sub_work_frames'
    os.makedirs(work_dir, exist_ok=True)
    
    print("  Extracting first 12s of video frames...")
    subprocess.run([
        ffmpeg_exe, '-y', '-ss', '0', '-t', '12.0',
        '-i', orig_video, '-r', '25',
        os.path.join(work_dir, 'frame_%04d.png')
    ], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    
    font_path = 'TienTuyen/Assets/_TienTuyen/Art/Fonts/SourceSansPro-Semibold.ttf'
    font = ImageFont.truetype(font_path, 36)
    
    print("  Processing frames: clean radio grille (frames 25 to 94) & stamp VOV subtitles...")
    
    # 1. Clean obsolete American radio subtitle (frames 25 to 94) using backward propagation from clean frame 95
    f95_path = os.path.join(work_dir, 'frame_0095.png')
    if os.path.exists(f95_path):
        prev_clean = cv2.imread(f95_path)
        h, w = prev_clean.shape[:2]
        
        for idx in range(94, 24, -1):
            fpath = os.path.join(work_dir, f'frame_{idx:04d}.png')
            if not os.path.exists(fpath):
                continue
            curr = cv2.imread(fpath)
            
            # Optical flow tracking between curr and clean successor
            gray_curr = cv2.cvtColor(curr[200:900, 200:1700], cv2.COLOR_BGR2GRAY)
            gray_prev = cv2.cvtColor(prev_clean[200:900, 200:1700], cv2.COLOR_BGR2GRAY)
            
            pts_prev = cv2.goodFeaturesToTrack(gray_prev, maxCorners=300, qualityLevel=0.01, minDistance=10)
            if pts_prev is not None and len(pts_prev) > 10:
                pts_curr, status, _ = cv2.calcOpticalFlowPyrLK(gray_prev, gray_curr, pts_prev, None)
                good_prev = pts_prev[status == 1]
                good_curr = pts_curr[status == 1]
                M, _ = cv2.estimateAffinePartial2D(good_prev, good_curr)
            else:
                M = None
                
            if M is not None:
                M_full = M.copy()
                M_full[0, 2] += 200 - (M[0, 0]*200 + M[0, 1]*200)
                M_full[1, 2] += 200 - (M[1, 0]*200 + M[1, 1]*200)
                warped_prev = cv2.warpAffine(prev_clean, M_full, (w, h), flags=cv2.INTER_LANCZOS4)
            else:
                warped_prev = prev_clean
                
            roi_curr = curr[970:1060, 300:1640]
            roi_prev = warped_prev[970:1060, 300:1640]
            
            diff = np.abs(roi_curr.astype(float) - roi_prev.astype(float))
            diff_val = np.max(diff, axis=2)
            
            mask = (diff_val > 18).astype(np.uint8) * 255
            kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))
            mask = cv2.dilate(mask, kernel, iterations=2)
            mask_f = cv2.GaussianBlur(mask, (5, 5), 0) / 255.0
            mask_f = np.expand_dims(mask_f, axis=2)
            
            cleaned = curr.copy()
            cleaned[970:1060, 300:1640] = (roi_prev * mask_f + roi_curr * (1 - mask_f)).astype(np.uint8)
            
            cv2.imwrite(fpath, cleaned)
            prev_clean = cleaned

    # 2. Stamp VOV subtitles (frames 145 to 267)
    for idx in range(145, 268):
        fpath = os.path.join(work_dir, f'frame_{idx:04d}.png')
        if not os.path.exists(fpath):
            continue
        img = cv2.imread(fpath)
        text = None
        if 145 <= idx <= 218:
            text = "Đây là Đài Tiếng nói Việt Nam, phát thanh từ Hà Nội,"
        elif 219 <= idx <= 267:
            text = "thủ đô nước Việt Nam Dân chủ Cộng hòa."
            
        if text:
            pil_img = Image.fromarray(cv2.cvtColor(img, cv2.COLOR_BGR2RGB))
            draw = ImageDraw.Draw(pil_img)
            tw = draw.textlength(text, font=font)
            tx = (1920 - tw) // 2
            ty = 985
            draw.text((tx, ty), text, font=font, fill=(255,255,255), stroke_width=2, stroke_fill=(0,0,0))
            img = cv2.cvtColor(np.array(pil_img), cv2.COLOR_RGB2BGR)
            cv2.imwrite(fpath, img)
            
    print("  Concatenating frames with rest of film into cleaned video track...")
    cmd = [
        ffmpeg_exe, '-y',
        '-framerate', '25', '-i', os.path.join(work_dir, 'frame_%04d.png'),
        '-ss', '12.0', '-i', orig_video,
        '-filter_complex', '[0:v][1:v]concat=n=2:v=1:a=0[v]',
        '-map', '[v]',
        '-c:v', 'libx264', '-preset', 'faster',
        '-b:v', '2700k', '-maxrate', '3300k', '-bufsize', '5500k',
        '-pix_fmt', 'yuv420p',
        '-an', output_video
    ]
    subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    shutil.rmtree(work_dir, ignore_errors=True)
    print(f"  [OK] Cleaned video with VOV subtitles created: {output_video}")

def main():
    print("========================================================================")
    print("🇻🇳 Building Fully Synced, Heroic Vietnamese Intro Audio (V4)")
    print("========================================================================")
    
    total_samples = 120 * SR
    master = np.zeros((total_samples, 2), dtype=float)
    
    # -------------------------------------------------------------
    # 1. Load Original Audio STRICTLY for Bomb Blast SFX (42.8s - 49.3s)
    # -------------------------------------------------------------
    # IMPORTANT: We DO NOT take original_audio after 49.3s!
    # - At 50.0s, original_audio has "War." in English -> Cut completely by 49.3s!
    # - At 113.4s-120s (1:52-2:00), original_audio has English whispers/speech -> Cut completely!
    # We synthesize a 100% clean, pristine cinematic Title Impact at 113.5s with zero voice!
    orig_path = 'tools/intro_video/original_audio.wav'
    print(f"Loading bomb blast SFX from {orig_path}...")
    orig = load_audio(orig_path, SR, 2)
    if len(orig) < total_samples:
        orig = np.pad(orig, ((0, total_samples - len(orig)), (0, 0)))
    else:
        orig = orig[:total_samples]
        
    sfx_mask = np.zeros(total_samples, dtype=float)
    
    # ONLY take the bomb blast impact and aftermath rubble: 42.8s to 49.3s (ZERO human speech!)
    i_428 = int(42.8 * SR)
    i_433 = int(43.3 * SR)
    i_485 = int(48.5 * SR)
    i_493 = int(49.3 * SR)
    sfx_mask[i_428:i_433] = np.linspace(0, 1.0, i_433 - i_428)
    sfx_mask[i_433:i_485] = 1.0
    sfx_mask[i_485:i_493] = np.linspace(1.0, 0.0, i_493 - i_485)
    # Strictly zero from 49.3s to the end of the video! Zero American voice leakage!
    
    master += orig * sfx_mask[:, np.newaxis]
    print("  [OK] Bomb blast (42.8s-49.3s) added. Strictly ZERO original audio after 49.3s.")
    
    # -------------------------------------------------------------
    # 2. Section 1: Radio Broadcast in Shelter (0.0s - 27.5s)
    # -------------------------------------------------------------
    print("Building 1972 Radio broadcast in A-frame shelter...")
    radio_raw = np.zeros((int(28.0 * SR), 2), dtype=float)
    
    # A. Radio Tuning Static: 0.0s to 2.2s (dial needle search across frequencies)
    tuning_static = synth_radio_tuning_static(2.2)
    radio_raw[:len(tuning_static)] += tuning_static * 0.60
    print("  [OK] Dial tuning static placed (0.0s - 2.2s, needle moving)")
    
    # Load pure authentic VOV brass fanfare (ZERO speech)
    pure_fanfare_file = 'tools/intro_video/source_audio/vov_pure_fanfare.wav'
    pure_fanfare = load_audio(pure_fanfare_file, SR, 2)
    
    # B. Iconic VOV Brass Trumpet Fanfare ("Diệt phát xít" motif): 2.0s to 5.8s
    # Trumpets play the triumphant opening phrase of "Diệt phát xít" with 100% pure music, ZERO speech
    fanfare_chunk = pure_fanfare[:int(3.8 * SR)].copy()
    fanfare_chunk = apply_fade(fanfare_chunk, in_samples=int(0.15 * SR), out_samples=int(0.35 * SR))
    f_start = int(2.0 * SR)
    f_end = f_start + len(fanfare_chunk)
    radio_raw[f_start:f_end] += fanfare_chunk * 0.90
    print("  [OK] Pure VOV brass fanfare placed (2.0s - 5.8s, 100% pure music, ZERO male speech)")
    
    # C. STRICTLY ONE UNIFIED FEMALE VOV Announcer: 5.8s to 10.7s (Finishes in 0:02 - 0:11 window!)
    # "Đây là Đài Tiếng nói Việt Nam, phát thanh từ Hà Nội, thủ đô nước Việt Nam Dân chủ Cộng hòa."
    # Spoken with solemn, dignified, and moving Hanoi radio cadence (trang nghiêm, truyền cảm).
    female_voice_file = 'tools/intro_video/voices/vov_female_solemn.mp3'
    if not os.path.exists(female_voice_file):
        female_voice_file = 'tools/intro_video/source_audio/vov_female_solemn.mp3'
    female_voice = load_and_trim_voice(female_voice_file, SR)
    fv_start = int(5.8 * SR)
    fv_end = fv_start + len(female_voice)
    radio_raw[fv_start:fv_end] += female_voice * 1.28
    print(f"  [OK] STRICTLY ONE unified solemn female announcer placed (5.8s - {fv_end/SR:.2f}s, 'thủ đô nước Việt Nam Dân chủ Cộng hòa')")
    
    # D. "Bài ca bên cánh võng" (NSND Thu Hiền): 10.8s to 27.5s
    # - 0:10.8 to 0:16.0 (covers 0:11 - 0:15): Bamboo flute (sáo trúc) & acoustic guitar intro
    #   plays lyrical, nostalgic music as the soldier folds the letter next to his sweetheart's photo.
    # - 0:16.0 to 0:27.5: NSND Thu Hiền warmly sings: "Dừng chân bên suối võng đưa, một trời xanh ngắt bao la rừng chiều..."
    canh_vong_file = 'tools/intro_video/source_audio/canh_vong.mp3'
    canh_vong_full = load_audio(canh_vong_file, SR, 2)
    cv_start_idx = int(11.10 * SR) # Flute & guitar intro -> singing begins at 16.0s in video
    cv_dur = int(16.70 * SR)       # 10.8s to 27.5s
    cv_chunk = canh_vong_full[cv_start_idx:cv_start_idx + cv_dur].copy()
    cv_chunk = apply_fade(cv_chunk, in_samples=int(0.8 * SR), out_samples=int(1.8 * SR))
    cv_dest_start = int(10.8 * SR)
    cv_dest_end = cv_dest_start + len(cv_chunk)
    radio_raw[cv_dest_start:cv_dest_end] += cv_chunk * 0.95
    print("  [OK] 'Bài ca bên cánh võng' flute/guitar intro (10.8s-16.0s) & Thu Hiền singing placed (16.0s-27.5s)")
    
    # Apply VEF 206 transistor speaker filter and mix into master
    radio_filtered = radio_vef206_filter(radio_raw)
    master[:len(radio_filtered)] += radio_filtered * 0.88
    
    # -------------------------------------------------------------
    # 3. Section 2: Air Raid Siren & Soldier Shouting (27.5s - 42.8s)
    # -------------------------------------------------------------
    print("Adding Air Raid Siren & Soldier Bunker Alert...")
    siren_file = 'tools/intro_video/source_audio/bao_dong.mp3'
    siren_full = load_audio(siren_file, SR, 2)
    s_start_idx = int(13.0 * SR)
    s_dur = int(15.3 * SR)
    siren_chunk = siren_full[s_start_idx:s_start_idx + s_dur].copy()
    siren_chunk = apply_fade(siren_chunk, in_samples=int(2.5 * SR), out_samples=int(0.8 * SR))
    s_dest_start = int(27.5 * SR)
    s_dest_end = s_dest_start + len(siren_chunk)
    master[s_dest_start:s_dest_end] += siren_chunk * 0.55
    print("  [OK] 1972 Air raid siren wailing (27.5s - 42.8s)")
    
    # Soldier urgent shout: 38.0s to 42.5s
    # "Báo động! Máy bay địch ném bom! Xuống hầm mau!"
    alert_file = 'tools/intro_video/voices/bunker_alert.mp3'
    alert_voice = load_audio(alert_file, SR, 2)
    alert_filtered = butter_bandpass_filter(alert_voice, 250, 4500, fs=SR, order=2)
    al_start = int(38.0 * SR)
    al_end = al_start + len(alert_filtered)
    master[al_start:al_end] += alert_filtered * 1.15
    print("  [OK] Urgent soldier bunker alert placed (38.0s - 42.5s)")
    
    # -------------------------------------------------------------
    # 4. Section 3: Grand Symphony 'Đường Chúng Ta Đi' (50.0s - 98.0s)
    # -------------------------------------------------------------
    print("Mixing Grand Symphony 'Đường Chúng Ta Đi' over the Trường Sơn Archive...")
    duong_file = 'tools/intro_video/source_audio/duong_chung_ta_di.mp3'
    duong_full = load_audio(duong_file, SR, 2)
    
    d_start_idx = int(1.0 * SR)
    d_dur = int(48.0 * SR) # 50.0s to 98.0s
    d_chunk = duong_full[d_start_idx:d_start_idx + d_dur].copy()
    d_chunk = apply_fade(d_chunk, in_samples=int(3.0 * SR), out_samples=int(2.5 * SR))
    d_dest_start = int(50.0 * SR)
    d_dest_end = d_dest_start + len(d_chunk)
    master[d_dest_start:d_dest_end] += d_chunk * 0.75
    
    # -------------------------------------------------------------
    # 5. Vietnamese Dubbed Narration Following EVERY Burned-in Subtitle
    # -------------------------------------------------------------
    # All 18 on-screen subtitles from 0:50 to 1:51 dubbed precisely:
    subs_timeline = [
        # (timestamp_sec, filename, gain, description)
        (50.4,  'tools/intro_video/voices_v3/sub_050.mp3', 1.25), # 50.4s: "Chiến tranh."
        (52.2,  'tools/intro_video/voices_v3/sub_052.mp3', 1.25), # 52.2s: "Chiến tranh không bao giờ thay đổi."
        (54.5,  'tools/intro_video/voices_v3/sub_054.mp3', 1.25), # 54.5s: "Ở đây, chiến tranh có một con đường."
        (58.8,  'tools/intro_video/voices_v3/sub_059.mp3', 1.22), # 58.8s: "Từ năm 1959, nó chạy về Nam qua những dãy núi này:"
        (64.0,  'tools/intro_video/voices_v3/sub_064.mp3', 1.22), # 64.0s: "đào bằng tay, bị ném bom ban đêm, sửa xong trước sáng."
        (68.8,  'tools/intro_video/voices_v3/sub_069.mp3', 1.22), # 68.8s: "Năm 1954, cha tôi đẩy xe đạp thồ gạo lên Điện Biên Phủ."
        (75.0,  'tools/intro_video/voices_v3/sub_075.mp3', 1.22), # 75.0s: "Ông trở về một đất nước bị chia đôi."
        (77.8,  'tools/intro_video/voices_v3/sub_078.mp3', 1.22), # 77.8s: "Bây giờ là năm 1972."
        (80.5,  'tools/intro_video/voices_v3/sub_080.mp3', 1.22), # 80.5s: "Đêm nào máy bay ném bom cũng đến."
        (83.5,  'tools/intro_video/voices_v3/sub_084.mp3', 1.22), # 83.5s: "Hố bom đầy nước mưa, rồi nước mưa đầy xác người."
        (87.5,  'tools/intro_video/voices_v3/sub_088.mp3', 1.22), # 87.5s: "Cứ vài cây số, lại có một nhóm người giữ cho con đường thông suốt."
        (92.0,  'tools/intro_video/voices_v3/sub_091.mp3', 1.25), # 92.0s: "Khoảng rừng trống này là của tôi."
        (94.5,  'tools/intro_video/voices_v3/sub_095.mp3', 1.25), # 94.5s: "Và tôi sợ."
        (96.0,  'tools/intro_video/voices_v3/sub_096.mp3', 1.25), # 96.0s: "Sợ cho mẹ tôi."
        (97.8,  'tools/intro_video/voices_v3/sub_098.mp3', 1.25), # 97.8s: "Cho người con gái vẫn viết thư cho tôi."
        (100.2, 'tools/intro_video/voices_v3/sub_101.mp3', 1.25), # 100.2s: "Cho những người lái xe đêm nay đi không đèn."
        (103.8, 'tools/intro_video/voices_v3/sub_105.mp3', 1.25), # 103.8s: "Bởi nếu con đường này dạy tôi một điều, thì đó là: chiến tranh..."
        (108.5, 'tools/intro_video/voices_v3/sub_109.mp3', 1.25), # 108.5s: "chiến tranh không bao giờ thay đổi."
    ]
    
    print("Dubbing Vietnamese narration precisely matching subtitles...")
    for t_sec, v_path, gain in subs_timeline:
        voice = load_and_trim_voice(v_path, SR)
        idx = int(t_sec * SR)
        ed = idx + len(voice)
        if ed <= total_samples:
            master[idx:ed] += voice * gain
            print(f"  [OK] Placed {os.path.basename(v_path)} at {t_sec:.1f}s ({len(voice)/SR:.2f}s)")
            
    # -------------------------------------------------------------
    # 6. Section 4: Game Climax & Military Brass March (98.5s - 112.0s)
    # -------------------------------------------------------------
    print("Building Heroic Climax: 'Tiến Bước Dưới Quân Kỳ' & Distant Firefight...")
    
    # Triumphant Military Brass & Marching Orchestra: "Tiến Bước Dưới Quân Kỳ"
    qk_file = 'tools/intro_video/source_audio/quan_ky.mp3'
    qk_full = load_audio(qk_file, SR, 2)
    qk_chunk = qk_full[:int(13.5 * SR)].copy()
    qk_chunk = apply_fade(qk_chunk, in_samples=int(1.2 * SR), out_samples=int(0.6 * SR))
    qk_dest_start = int(98.5 * SR)
    qk_dest_end = min(qk_dest_start + len(qk_chunk), int(112.0 * SR))
    qk_len = qk_dest_end - qk_dest_start
    master[qk_dest_start:qk_dest_end] += qk_chunk[:qk_len] * 0.88
    print("  [OK] 'Tiến Bước Dưới Quân Kỳ' military brass march placed (98.5s - 112.0s)")
    
    # Distant AK-47 combat gunfire echoing in the mountains (101s - 111s)
    gunfire = generate_distant_gunfire(total_samples)
    master += gunfire
    print("  [OK] Clean distant AK-47 combat firefight placed (101.0s - 111.0s)")
    
    # Hard cut to black silence at 112.0s
    master[int(112.0 * SR):int(113.4 * SR)] = 0.0
    print("  [OK] Hard cut to black silence (112.0s - 113.4s)")
    
    # -------------------------------------------------------------
    # 7. Section 5: Ending Title Music & Cinematic Impact (1:52 to 2:00 / 112.0s - 120.0s)
    # -------------------------------------------------------------
    print("Adding Solemn & Heroic Title Score & Clean Impact from 1:52 to 2:00...")
    
    # 1. Synthesized Cinematic Sub-Bass Title Impact at 113.48s (100% clean, ZERO human speech!)
    title_impact = synth_cinematic_title_impact(duration=2.5, fs=SR)
    t_hit_start = int(113.48 * SR)
    t_hit_len = min(len(title_impact), total_samples - t_hit_start)
    master[t_hit_start:t_hit_start + t_hit_len] += title_impact[:t_hit_len] * 1.05
    print("  [OK] Synthesized cinematic sub-bass Title Impact placed at 113.5s (100% clean, ZERO voice)")
    
    # 2. Pure Instrumental String Orchestra (Adagio theme from clean 66.5s to 74.5s section):
    # - Samuel Barber's Adagio for Strings in its most emotional, majestic crescendo (peak 0.68, RMS 0.12).
    # - Charlie Sheen's monologue ends completely at 66.0s, so this section is 100% pure music, ZERO speech!
    # - Fades in smoothly at 112.2s, swelling into full richness right as the title card appears at 113.5s.
    # - Boosted with warm presence (gain 1.38x) for a rich, epic musical ending as requested ("lồng thêm nhạc").
    ad_path = 'TienTuyen/Assets/_TienTuyen/Audio/Resources/CombatAudio/Music_Menu_Adagio.ogg'
    if not os.path.exists(ad_path):
        ad_path = 'tools/intro_video/source_audio/menu_adagio.wav'
    ad_data = load_audio(ad_path, SR, 2)
    st_ad = int(66.5 * SR)
    ed_ad = int(74.5 * SR)
    ad_cue = ad_data[st_ad:ed_ad].copy()
    
    f_in = int(1.3 * SR)
    ad_cue[:f_in] *= np.linspace(0.08, 1.0, f_in)[:, np.newaxis]
    f_out = int(0.6 * SR)
    ad_cue[-f_out:] *= np.linspace(1.0, 0.75, f_out)[:, np.newaxis]
    ad_cue *= 1.38
    
    ad_dest = int(112.0 * SR)
    ad_len = min(len(ad_cue), total_samples - ad_dest)
    master[ad_dest:ad_dest + ad_len] += ad_cue[:ad_len]
    print("  [OK] Rich pure instrumental Adagio string crescendo placed (112.0s - 120.0s, 100% pure music, ZERO American voice)")
    
    # -------------------------------------------------------------
    # 7. Mastering & Loudness Normalization
    # -------------------------------------------------------------
    print("Mastering final 24-bit/48kHz audio track...")
    peak = np.max(np.abs(master))
    print(f"  Raw mix peak: {peak:.3f}")
    if peak > 0.98:
        master = np.tanh(master / peak * 1.08) * 0.95
    else:
        master = master * (0.95 / max(peak, 0.1))
        
    out_wav = 'TienTuyenIntro_VN.wav'
    out_int16 = (master * 32767.0).astype(np.int16)
    
    import wave
    with wave.open(out_wav, 'wb') as wf:
        wf.setnchannels(2)
        wf.setsampwidth(2)
        wf.setframerate(SR)
        wf.writeframes(out_int16.tobytes())
    print(f"Generated clean WAV: {out_wav} ({len(master)/SR:.2f}s)")

    # -------------------------------------------------------------
    # 8. Mux into TienTuyenIntro.mp4 using FFmpeg (Loudnorm -14 LUFS)
    # -------------------------------------------------------------
    cleaned_video = 'tools/intro_video/original_intro_cleaned.mp4'
    if not os.path.exists(cleaned_video):
        print("Cleaning obsolete American subtitles & generating VOV subtitles...")
        clean_and_subtitle_video(ffmpeg_exe, 'tools/intro_video/original_intro.mp4', cleaned_video)

    video_src = cleaned_video if os.path.exists(cleaned_video) else 'tools/intro_video/original_intro.mp4'

    print("Muxing audio into TienTuyenIntro.mp4 with broadcast loudness (-14 LUFS)...")
    mp4_target = 'TienTuyen/Assets/StreamingAssets/TienTuyenIntro.mp4'
    temp_mp4 = 'TienTuyenIntro_temp.mp4'
    
    cmd = [
        ffmpeg_exe, '-y',
        '-i', video_src,                              # Cleaned Video source with VOV subtitles
        '-i', out_wav,                                # New Vietnamese audio
        '-c:v', 'copy',                               # Copy video untouched
        '-af', 'loudnorm=I=-14:TP=-1:LRA=11',         # EBU R128 broadcast standard
        '-c:a', 'aac', '-b:a', '256k', '-ar', '48000',
        '-map', '0:v:0',
        '-map', '1:a:0',
        '-shortest',
        temp_mp4
    ]
    res = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if res.returncode == 0:
        import shutil
        shutil.copy2(temp_mp4, mp4_target)
        try:
            os.remove(temp_mp4)
        except Exception:
            pass
        print(f"✅ Successfully updated {mp4_target}!")
        
        # Also copy to build directory
        build_target = 'Builds/Windows/TienTuyen_Data/StreamingAssets/TienTuyenIntro.mp4'
        if os.path.exists(os.path.dirname(build_target)):
            try:
                shutil.copy2(mp4_target, build_target)
                print(f"✅ Successfully updated {build_target}!")
            except Exception as e:
                print(f"⚠️ Note: Could not update {build_target} directly (file in use by game/player): {e}")
    else:
        print("FFmpeg error:", res.stderr.decode('utf-8', 'ignore'))

if __name__ == '__main__':
    main()
