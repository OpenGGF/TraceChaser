#!/usr/bin/env python3
"""Compose a power-on Genesis Plus GX BK2 for the Sonic CD stage recorder.

Purpose: author the input movie for a Sonic CD (USA) native stage trace from a
small JSON schedule instead of recording one by hand. Inputs: a schedule file
and a new output path outside every repository. Origin: TraceChaser lane K2,
2026-11-12 (first Sonic CD native fixture).

Schedule format::

    {"frames": 3800,
     "inputs": [{"first": 599, "last": 599, "button": "Start"}, ...]}

``first``/``last`` are inclusive 0-based BK2 input rows. Row r is the input for
the (r+1)-th FrameAdvance, so a probe that numbers frames from 1 sees row r as
frame r+1. SyncSettings are BizHawk 2.11's GPGXSyncSettings defaults, the boot
recipe the Sonic CD oracle probes used. The movie contains inputs only: no disc,
BIOS or savestate data.
"""

import argparse
import json
import sys
import zipfile
from pathlib import Path

BUTTONS = ("Up", "Down", "Left", "Right", "A", "B", "C", "Start")
MNEMONIC = "UDLRABCS"
SYNC_SETTINGS = {
    "o": {
        "$type": "BizHawk.Emulation.Cores.Consoles.Sega.gpgx.GPGX+GPGXSyncSettings, BizHawk.Emulation.Cores",
        "UseSixButton": False, "ControlTypeLeft": 1, "ControlTypeRight": 1, "Region": 0,
        "ForceVDP": 0, "LoadBIOS": False, "Overscan": 0, "GGExtra": False, "SMSFMSoundChip": 1,
        "GenesisFMSoundChip": 0, "Filter": 1, "LowPassRange": 26214, "LowFreq": 880,
        "HighFreq": 5000, "LowGain": 1.0, "MidGain": 1.0, "HighGain": 1.0,
        "BackdropColor": 4294902015, "SpritesAlwaysOnTop": False,
    }
}
LOG_KEY = ("LogKey:#Power|Reset|#" + "|".join("P1 " + b for b in BUTTONS) + "|#"
           + "|".join("P2 " + b for b in BUTTONS) + "|")


def rows(schedule):
    count = schedule["frames"]
    if type(count) is not int or not 1 <= count <= 216000:
        raise ValueError("frames must be an integer in 1..216000")
    held = [set() for _ in range(count)]
    for entry in schedule["inputs"]:
        first, last, button = entry["first"], entry["last"], entry["button"]
        if button not in BUTTONS:
            raise ValueError(f"unknown button {button}")
        if type(first) is not int or type(last) is not int or not 0 <= first <= last < count:
            raise ValueError(f"input range {first}..{last} outside 0..{count - 1}")
        for row in range(first, last + 1):
            held[row].add(button)
    for buttons in held:
        p1 = "".join(m if b in buttons else "." for b, m in zip(BUTTONS, MNEMONIC))
        yield f"|..|{p1}|........|"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--schedule", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    output = args.output.resolve()
    if output.exists():
        parser.error("output already exists")
    for root in (Path(__file__).resolve().parents[1],):
        if output.is_relative_to(root):
            parser.error("output must be outside the TraceChaser checkout")
    schedule = json.loads(args.schedule.read_text())
    log = "[Input]\n" + LOG_KEY + "\n" + "\n".join(rows(schedule)) + "\n[/Input]\n"
    header = ("MovieVersion BizHawk v2.0.0\nAuthor TraceChaser scd_stage_movie.py\n"
              "Core Genplus-gx\nPlatform GEN\nemuVersion Version 2.11\n"
              "OriginalEmuVersion Version 2.11\nGameName Sonic CD (USA)\nSHA1 \n")
    with zipfile.ZipFile(output, "x", zipfile.ZIP_DEFLATED) as movie:
        for name, text in (("Header.txt", header),
                           ("SyncSettings.json", json.dumps(SYNC_SETTINGS, separators=(",", ":"))),
                           ("Input Log.txt", log), ("Comments.txt", ""), ("Subtitles.txt", "")):
            info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            movie.writestr(info, text)
    print(f"{output}: {schedule['frames']} rows")
    return 0


if __name__ == "__main__":
    sys.exit(main())
