"""Cuts the captured footage into the finished intro film.

Usage: python edit.py <capture dir> <cards dir> <score.wav> <out.mp4>

The capture dir holds raw.mp4 (60 fps), game_audio.f32 and timeline.txt written
by IntroCinematic. Stage 1 renders each shot to a graded 1080p30 clip with its
game audio; stage 2 assembles them with dissolves, cards, letterbox and the score.
"""
import json
import os
import subprocess
import sys

from cutkit import GRADE, read_timeline, run

CAPTURE, CARDS, SCORE, OUT = (os.path.abspath(a) for a in sys.argv[1:5])
WORK = os.path.join(os.path.dirname(OUT), "edit_work")
os.makedirs(WORK, exist_ok=True)
BAR = 4 * 60 / 90
DISSOLVE = .6

# (shot, seconds into the shot, seconds of capture used, playback speed, audio gain)
OPENING = [("aerial", 0, 8.0, 1, .55), ("camp", .3, 5.0, 1, .6), ("flag", .3, 5.5, 1, .6), ("hero", .2, 5.3, 1, .6)]
FIGHT = [("enemies", .2, 1.5 * BAR, 1, 1.0), ("shoulder", .8, BAR, 1, 1.0), ("track", 1.0, BAR, 1, 1.0),
         ("reverse", .6, BAR, 1, 1.0), ("slowmo", .2, BAR, .5, .8), ("finale", 0, 8.0, 2 / 3, .7)]
T_DROP = 22.667
FREEZE = 2.5  # the last finale frame holds under the title
CARDS_AT = [("card_place", 1.6, 6.8), ("card_camp", 8.6, 12.0), ("card_flag", 13.0, 16.9), ("card_hero", 18.2, 21.3)]
T_TITLE = 44.0


def render_clip(name, offset, seconds, speed, gain, fade):
    start = read_timeline(CAPTURE)[0][name] + offset
    video, audio = os.path.join(WORK, name + ".mp4"), os.path.join(WORK, name + ".wav")
    if os.environ.get("REUSE_CLIPS") and os.path.exists(video) and os.path.exists(audio):
        return video, audio, seconds / speed
    # Full speed blends frame pairs (a touch of motion blur); slow motion uses every captured frame.
    timing = "" if speed == .5 else "tmix=frames=2,"
    vf = f"{timing}setpts=PTS/{speed},fps=30,scale=1920:1080:flags=lanczos,{GRADE}"
    run(["-ss", f"{start:.4f}", "-t", f"{seconds:.4f}", "-i", os.path.join(CAPTURE, "raw.mp4"),
         "-vf", vf, "-an", "-c:v", "libx264", "-preset", "slow", "-crf", "14", "-pix_fmt", "yuv420p", video])
    out_seconds = seconds / speed
    af = (f"asetrate={int(48000 * speed)},aresample=48000," if speed != 1 else "") + \
         f"volume={gain},afade=t=in:d={fade},afade=t=out:st={out_seconds - fade:.3f}:d={fade}"
    run(["-f", "f32le", "-ar", "48000", "-ac", "2", "-ss", f"{start:.4f}", "-t", f"{seconds:.4f}",
         "-i", os.path.join(CAPTURE, "game_audio.f32"), "-af", af, audio])
    return video, audio, out_seconds


def main():
    clips = []
    for name, offset, seconds, speed, gain in OPENING:
        clips.append(render_clip(name, offset, seconds, speed, gain, DISSOLVE))
    for name, offset, seconds, speed, gain in FIGHT:
        clips.append(render_clip(name, offset, seconds, speed, gain, .03))

    inputs, filters = [], []
    for video, _, _ in clips:
        inputs += ["-i", video]
    for video, audio, _ in clips:
        inputs += ["-i", audio]
    n = len(clips)
    card_inputs = len(inputs) // 2
    for card, start, end in CARDS_AT + [("card_title", T_TITLE, 0)]:
        inputs += ["-loop", "1", "-framerate", "30", "-i", os.path.join(CARDS, card + ".png")]
    inputs += ["-i", SCORE]

    # Opening: dissolves, then the soldier fades to black before the drop.
    length = clips[0][2]
    previous = "0:v"
    for i in range(1, len(OPENING)):
        out = f"o{i}"
        filters.append(f"[{previous}][{i}:v]xfade=transition=fade:duration={DISSOLVE}:offset={length - DISSOLVE:.3f}[{out}]")
        length += clips[i][2] - DISSOLVE
        previous = out
    filters.append(f"[{previous}]fade=t=in:d=1.2,fade=t=out:st={length - .6:.3f}:d=0.6[opening]")
    gap = T_DROP - length
    filters.append(f"color=black:s=1920x1080:r=30:d={gap:.3f},format=yuv420p[gap]")

    # The fight: hard cuts, with a white flash on the drop and on the finale.
    fight = []
    for k, i in enumerate(range(len(OPENING), n)):
        chain = f"[{i}:v]"
        name = FIGHT[k][0]
        steps = []
        if name in ("enemies", "finale"):
            steps.append("fade=t=in:d=0.3:color=white")
        if name == "finale":
            steps.append(f"tpad=stop_mode=clone:stop_duration={FREEZE}")
        filters.append(chain + (",".join(steps) if steps else "null") + f"[f{k}]")
        fight.append(f"[f{k}]")
    total = T_DROP + sum(c[2] for c in clips[len(OPENING):]) + FREEZE
    filters.append("[opening][gap]" + "".join(fight) + f"concat=n={2 + len(fight)}:v=1:a=0,settb=1/30,setpts=N/30/TB[cut]")

    # Darken under the title, cards, letterbox, final fade.
    filters.append(f"color=black:s=1920x1080:r=30:d={total:.3f},format=rgba,"
                   f"fade=t=in:st={T_TITLE - 1.2}:d=1.4:alpha=1,colorchannelmixer=aa=0.6[dim]")
    filters.append("[cut][dim]overlay=shortest=1[v0]")
    current = "v0"
    for j, (card, start, end) in enumerate(CARDS_AT + [("card_title", T_TITLE, total)]):
        idx = card_inputs + j
        hold = end - start
        fade_in = 0.25 if card == "card_title" else 0.7
        fade_out = f",fade=t=out:st={hold - .7:.3f}:d=0.7:alpha=1" if card != "card_title" else ""
        filters.append(f"[{idx}:v]trim=duration={hold:.3f},format=rgba,fade=t=in:d={fade_in}:alpha=1{fade_out},"
                       f"setpts=PTS-STARTPTS+{start}/TB[c{j}]")
        filters.append(f"[{current}][c{j}]overlay=eof_action=pass[v{j + 1}]")
        current = f"v{j + 1}"
    filters.append(f"[{current}]drawbox=x=0:y=0:w=iw:h=60:color=black:t=fill,"
                   f"drawbox=x=0:y=ih-60:w=iw:h=60:color=black:t=fill,"
                   f"fade=t=out:st={total - 1.5:.3f}:d=1.5,format=yuv420p[video]")

    # Audio: each shot's game sound at its place in the cut, under the score.
    starts, at = [], 0.0
    for i, clip in enumerate(clips[:len(OPENING)]):
        starts.append(at)
        at += clip[2] - DISSOLVE
    at = T_DROP
    for clip in clips[len(OPENING):]:
        starts.append(at)
        at += clip[2]
    labels = []
    for i, start in enumerate(starts):
        ms = int(start * 1000)
        filters.append(f"[{n + i}:a]adelay={ms}|{ms}[g{i}]")
        labels.append(f"[g{i}]")
    filters.append("".join(labels) + f"amix=inputs={n}:normalize=0,afade=t=out:st={T_TITLE + .5}:d=3[game]")
    score = card_inputs + len(CARDS_AT) + 1
    filters.append(f"[{score}:a]volume=1.0[music]")
    filters.append(f"[game][music]amix=inputs=2:normalize=0:duration=longest,atrim=duration={total:.3f}[mixed]")

    # Two-pass loudness to -14 LUFS, true peak -1 dB.
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
