"""Cuts the trailer capture into the finished trailer.

Usage: python trailer_edit.py <capture dir> <cards dir> <score.wav> <out.mp4>

The capture dir is written by IntroCinematic in -trailer mode. Stage 1 renders every
segment (graded shots, number cards, black) as 1080p30 with an exact frame count;
stage 2 joins them and lays captions, labels, the title, letterbox and the score on top.
The cut points are tied to trailer_score.py (100 BPM, one bar = 2.4 s).
"""
import json
import os
import subprocess
import sys

from cutkit import GRADE, read_timeline, run

CAPTURE, CARDS, SCORE, OUT = (os.path.abspath(a) for a in sys.argv[1:5])
WORK = os.path.join(os.path.dirname(OUT), "trailer_work")
os.makedirs(WORK, exist_ok=True)
FPS = 30
BAR = 2.4
DISSOLVE = .6
T_II = 24.0
T_TITLE = 75.6
T_BOOM = 88.2
STARTS, EVENTS = read_timeline(CAPTURE)

# Act I: (shot, seconds into the shot, seconds on screen), joined by dissolves.
OPENING = [("aerial", .5, 6.0), ("camp", .3, 4.4), ("t_silhouette", .3, 4.6), ("flag", .3, 4.6), ("hero", .2, 5.0)]


def first(kind, shot):
    return EVENTS[(kind, shot)][0]


# The stinger opens as early as the capture allows, at most 2.2 s before the blast.
STING_LEAD = min(66, int((first("kill", "t_sting") - .05) * FPS)) / FPS
T_FINALE_END = 85.2

# Acts II to IV, back to back from T_II:
# ("card", png, seconds) | ("black", seconds) | ("shot", name, offset, seconds on screen, speed, gain)
SEQUENCE = [
    ("card", "card_n6", BAR),
    ("shot", "t_spawn", .9, BAR, 1, 1.0),
    ("shot", "t_wide", 1.2, BAR, 1, 1.0),
    ("card", "card_n4", BAR),
    ("shot", "t_infantry", .8, BAR / 2, 1, 1.0),
    ("shot", "t_shooter", .5, BAR / 2, 1, 1.0),
    ("shot", "t_charger", first("charge", "t_charger") - .75, BAR / 2, 1, 1.0),
    ("shot", "t_elite", 1.2, BAR / 2, 1, 1.0),
    ("card", "card_n3", BAR),
    ("shot", "t_rifle", .6, BAR, 1, 1.1),
    ("shot", "t_smg", .6, BAR, 1, 1.1),
    ("shot", "t_shotgun", .5, BAR, 1, 1.1),
    ("card", "card_n1", BAR),
    ("shot", "t_lone", .8, BAR, 1, .8),
    ("shot", "track", 1.0, BAR, 1, .9),
    ("shot", "shoulder", .8, BAR, 1, .9),
    ("shot", "enemies", .2, BAR, 1, .9),
    ("shot", "t_orbit", .1, 2 * BAR, 1, .9),
    ("shot", "reverse", .6, BAR / 2, 1, .9),
    ("shot", "t_crane", .8, BAR, 1, .9),
    ("shot", "slowmo", .2, 2 * BAR, .5, .8),
    ("shot", "finale", 0, 12.0, 2 / 3, .5),
    ("black", T_BOOM - STING_LEAD - T_FINALE_END),
    # The stinger cuts to black three frames after the blast, which lands on the score's hit.
    ("shot", "t_sting", first("kill", "t_sting") - STING_LEAD, STING_LEAD + .1, 1, 1.3),
    ("black", .7),
    ("card", "card_slate", 7.0),
]
FLASH = {"card_n6", "card_n4", "card_n3", "card_n1", "track"}

T_FADE = 84.4  # the finale and its title fade to black over 0.8 s
# Overlays: (png, start, end, fade in, fade out)
OVERLAYS = [
    ("cap_road", 1.2, 5.0, .7, .7), ("cap_through", 6.0, 9.2, .7, .7), ("cap_enemy", 9.9, 13.2, .7, .7),
    ("cap_people", 14.0, 17.4, .7, .7),
    ("label_infantry", 33.6, 34.8, .1, .2), ("label_shooter", 34.8, 36.0, .1, .2),
    ("label_charger", 36.0, 37.2, .1, .2), ("label_elite", 37.2, 38.4, .1, .2),
    ("label_rifle", 40.8, 43.2, .15, .3), ("label_smg", 43.2, 45.6, .15, .3), ("label_shotgun", 45.6, 48.0, .15, .3),
    ("cap_hold", 68.8, 72.9, .6, .6),
    ("card_title", T_TITLE, T_FADE + .8, .25, .8),
]


def frames(seconds):
    return int(round(seconds * FPS))


def encode(vf_input, vf, seconds, out):
    run(vf_input + ["-vf", vf + ",format=yuv420p", "-frames:v", str(frames(seconds)), "-an",
                    "-c:v", "libx264", "-preset", "slow", "-crf", "14", "-r", str(FPS), out])


def shot(name, offset, seconds, speed, gain, fade=.03, flash=False):
    """A graded clip of `seconds` on screen and its game audio (stereo wav of the same length)."""
    start = STARTS[name] + offset
    tag = f"{name}_{offset:.2f}_{seconds:.2f}"
    video, audio = os.path.join(WORK, tag + ".mp4"), os.path.join(WORK, tag + ".wav")
    captured = seconds * speed + .1
    # Full speed blends frame pairs (a touch of motion blur); half speed uses every captured frame.
    timing = "" if speed == .5 else "tmix=frames=2,"
    vf = f"{timing}setpts=PTS/{speed},fps={FPS},scale=1920:1080:flags=lanczos,{GRADE}"
    if flash:
        vf += ",fade=t=in:d=0.3:color=white"
    if name == "finale":
        vf += f",fade=t=out:st={T_FADE - (T_TITLE - BAR):.3f}:d=0.8"  # the finale starts a bar before the title
    if not (os.environ.get("REUSE_CLIPS") and os.path.exists(video)):
        encode(["-ss", f"{start:.4f}", "-t", f"{captured:.4f}", "-i", os.path.join(CAPTURE, "raw.mp4")], vf, seconds, video)
    af = (f"asetrate={int(48000 * speed)},aresample=48000," if speed != 1 else "") + \
         f"volume={gain},apad,atrim=duration={frames(seconds) / FPS:.4f}," \
         f"afade=t=in:d={fade},afade=t=out:st={frames(seconds) / FPS - fade:.4f}:d={fade}"
    run(["-f", "f32le", "-ar", "48000", "-ac", "2", "-ss", f"{start:.4f}", "-t", f"{captured:.4f}",
         "-i", os.path.join(CAPTURE, "game_audio.f32"), "-af", af, audio])
    return video, audio


def card(png, seconds, flash):
    out = os.path.join(WORK, f"{png}.mp4")
    vf = "noise=alls=6:allf=t" + (",fade=t=in:d=0.25:color=white" if flash else ",fade=t=in:d=1.0")
    encode(["-loop", "1", "-framerate", str(FPS), "-i", os.path.join(CARDS, png + ".png")], vf, seconds, out)
    return out


def black(seconds, k):
    out = os.path.join(WORK, f"black_{k}.mp4")
    encode(["-f", "lavfi", "-i", f"color=black:s=1920x1080:r={FPS}"], "null", seconds, out)
    return out


def opening():
    """Act I: dissolves, fade in from black, fade out before the first card."""
    clips = [shot(name, offset, seconds, 1, .5, fade=DISSOLVE) for name, offset, seconds in OPENING]
    inputs, filters = [], []
    for video, _ in clips:
        inputs += ["-i", video]
    length, previous = OPENING[0][2], "0:v"
    starts = [0.0]
    for i in range(1, len(OPENING)):
        filters.append(f"[{previous}][{i}:v]xfade=transition=fade:duration={DISSOLVE}:offset={length - DISSOLVE:.3f}[o{i}]")
        starts.append(length - DISSOLVE)
        length += OPENING[i][2] - DISSOLVE
        previous = f"o{i}"
    filters.append(f"[{previous}]fade=t=in:d=1.2,fade=t=out:st={length - .6:.3f}:d=0.6,"
                   f"tpad=stop_mode=add:stop_duration={T_II - length:.3f}:color=black,format=yuv420p[v]")
    out = os.path.join(WORK, "act1.mp4")
    run(inputs + ["-filter_complex", ";".join(filters), "-map", "[v]", "-frames:v", str(frames(T_II)),
                  "-c:v", "libx264", "-preset", "slow", "-crf", "14", "-r", str(FPS), out])
    return out, [(audio, at) for (_, audio), at in zip(clips, starts)]


def main():
    act1, sounds = opening()
    videos, at, blacks = [act1], T_II, 0
    for item in SEQUENCE:
        if item[0] == "card":
            videos.append(card(item[1], item[2], item[1] in FLASH))
            at += frames(item[2]) / FPS
        elif item[0] == "black":
            blacks += 1
            videos.append(black(item[1], blacks))
            at += frames(item[1]) / FPS
        else:
            _, name, offset, seconds, speed, gain = item
            video, audio = shot(name, offset, seconds, speed, gain, flash=name in FLASH)
            videos.append(video)
            sounds.append((audio, at))
            at += frames(seconds) / FPS
    total = at
    listing = os.path.join(WORK, "cut.txt")
    with open(listing, "w") as f:
        f.writelines(f"file '{v}'\n".replace("\\", "/") for v in videos)
    cut = os.path.join(WORK, "cut.mp4")
    run(["-f", "concat", "-safe", "0", "-i", listing, "-c", "copy", cut])

    # Stage 2: overlays, letterbox, fades, and the sound.
    inputs, filters = ["-i", cut], []
    for png, start, end, _, _ in OVERLAYS:
        inputs += ["-loop", "1", "-framerate", str(FPS), "-t", f"{end - start:.3f}", "-i", os.path.join(CARDS, png + ".png")]
    first_audio = len(OVERLAYS) + 1
    for audio, _ in sounds:
        inputs += ["-i", audio]
    inputs += ["-i", SCORE]
    # Darken the finale under the title.
    filters.append(f"color=black:s=1920x1080:r={FPS}:d={total:.3f},format=rgba,"
                   f"fade=t=in:st={T_TITLE - .4}:d=0.6:alpha=1,fade=t=out:st={T_FADE}:d=0.8:alpha=1,"
                   f"colorchannelmixer=aa=0.55[dim]")
    filters.append("[0:v][dim]overlay=shortest=1[v0]")
    current = "v0"
    for j, (png, start, end, fade_in, fade_out) in enumerate(OVERLAYS):
        hold = end - start
        filters.append(f"[{j + 1}:v]format=rgba,fade=t=in:d={fade_in}:alpha=1,"
                       f"fade=t=out:st={hold - fade_out:.3f}:d={fade_out}:alpha=1,"
                       f"setpts=PTS-STARTPTS+{start}/TB[c{j}]")
        filters.append(f"[{current}][c{j}]overlay=eof_action=pass[v{j + 1}]")
        current = f"v{j + 1}"
    filters.append(f"[{current}]drawbox=x=0:y=0:w=iw:h=60:color=black:t=fill,drawbox=x=0:y=ih-60:w=iw:h=60:color=black:t=fill,"
                   f"fade=t=out:st={total - 1.2:.3f}:d=1.2,format=yuv420p[video]")
    labels = []
    for i, (_, start) in enumerate(sounds):
        ms = int(round(start * 1000))
        filters.append(f"[{first_audio + i}:a]adelay={ms}|{ms}[g{i}]")
        labels.append(f"[g{i}]")
    filters.append("".join(labels) + f"amix=inputs={len(labels)}:normalize=0[game]")
    filters.append(f"[game][{first_audio + len(sounds)}:a]amix=inputs=2:normalize=0:duration=longest,atrim=duration={total:.3f}[mixed]")

    graph = ";".join(filters)
    probe = subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-y"] + inputs +
                           ["-filter_complex", graph + ";[video]nullsink;[mixed]loudnorm=I=-14:TP=-1:LRA=14:print_format=json[aout]",
                            "-map", "[aout]", "-f", "null", "-"], capture_output=True, text=True)
    if probe.returncode:
        sys.exit(probe.stderr[-3000:])
    stats = json.loads(probe.stderr[probe.stderr.rindex("{"):probe.stderr.rindex("}") + 1])
    norm = (f"loudnorm=I=-14:TP=-1:LRA=14:measured_I={stats['input_i']}:measured_TP={stats['input_tp']}:"
            f"measured_LRA={stats['input_lra']}:measured_thresh={stats['input_thresh']}:"
            f"offset={stats['target_offset']}:linear=true,aresample=48000")
    run(inputs + ["-filter_complex", graph + f";[mixed]{norm}[aout]", "-map", "[video]", "-map", "[aout]",
                  "-c:v", "libx264", "-preset", "slow", "-crf", "17", "-profile:v", "high", "-pix_fmt", "yuv420p",
                  "-c:a", "aac", "-b:a", "256k", "-movflags", "+faststart", "-t", f"{total:.3f}", OUT])
    print(f"{OUT}: {total:.2f}s")


if __name__ == "__main__":
    main()
