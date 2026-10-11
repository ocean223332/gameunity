"""Helpers shared by the intro and trailer edits: ffmpeg, the colour grade, the capture timeline."""
import os
import re
import subprocess

CAPTURE_FPS = 60
GRADE = ("eq=gamma=1.22:contrast=1.08:saturation=1.15:brightness=0.015,"
         "colorbalance=rs=-0.05:bs=0.05:rm=0.02:bm=-0.01:rh=0.06:gh=0.02:bh=-0.05")


def run(args):
    subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-y"] + args, check=True)


def read_timeline(capture):
    """Shot start times and staged events from IntroCinematic's timeline.txt, in seconds.

    Returns ({shot: start}, {(event, shot): [times since that shot's start]}).
    """
    starts, events = {}, {}
    with open(os.path.join(capture, "timeline.txt")) as f:
        for line in f:
            m = re.match(r"(\w+) (\w+) (\d+)$", line.strip())
            if not m:
                continue
            kind, shot, frame = m.group(1), m.group(2), int(m.group(3)) / CAPTURE_FPS
            if kind == "shot":
                starts[shot] = frame
            elif kind != "end":
                events.setdefault((kind, shot), []).append(frame - starts[shot])
    return starts, events
