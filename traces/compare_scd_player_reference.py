#!/usr/bin/env python3
"""Cross-check a Sonic CD stage trace against an independent player reference.

Purpose: prove the native ``--scd-trace`` rows read the right RAM by comparing
them, frame for frame, with the separately implemented OpenGGF Sonic CD
player-reference probe (``player_reference_probe.cs``, which polls 68K RAM
through its own source-derived layout and accepts a frame only after its
R11A program-identity windows match). Inputs: a trace directory and the probe's
``player.jsonl`` and ``av.jsonl`` from a run with the same disc, BIOS and input
schedule. Origin: TraceChaser lane K2, 2026-11-12.

Trace row N is matched to probe frame ``bk2_frame_offset + N + 1`` (both sample
after the FrameAdvance that consumed BK2 row ``bk2_frame_offset + N``). Every
row must have an accepted probe frame; a missing or unaccepted frame fails.
"""

import argparse
import csv
import json
import sys
from pathlib import Path


def load_jsonl(path):
    with Path(path).open(encoding="utf-8") as stream:
        return [json.loads(line) for line in stream if line.strip()]


def expected_columns(player):
    f, g = player["fields"], player["globals"]
    return {
        "camera_x": (g["cameraX"] >> 16) & 0xFFFF,
        "camera_y": (g["cameraY"] >> 16) & 0xFFFF,
        "rings": g["rings"],
        "gameplay_frame_counter": g["levelFrames"],
        "vblank_counter": g["levelVIntCounter"] & 0xFFFF,
        "player_x": f["oX"],
        "player_y": f["oY"],
        "player_x_speed": f["oXVel"],
        "player_y_speed": f["oYVel"],
        "player_g_speed": f["oPlayerGVel"],
        "player_angle": f["oAngle"],
        "player_x_sub": f["oXSub"],
        "player_y_sub": f["oYSub"],
        "player_routine": f["oRoutine"],
        "player_status_byte": f["oFlags"],
        "player_animation_id": f["oAnim"],
        "player_mapping_frame": f["oMapFrame"],
    }


def compare(trace, player_rows, av_rows):
    metadata = json.loads((trace / "metadata.json").read_text(encoding="utf-8"))
    offset = metadata["bk2_frame_offset"]
    probe = {row["frame"]: row for row in player_rows}
    av = {row["frame"]: row for row in av_rows}
    lag = {e["frame"]: e["lagged"] for e in load_jsonl(trace / "aux_state.jsonl")
           if e.get("event") == "lag_state"}
    with (trace / "physics.csv").open(encoding="utf-8", newline="") as stream:
        rows = list(csv.DictReader(stream))
    errors, checked = [], 0
    if len(rows) != metadata["trace_frame_count"]:
        errors.append(f"physics has {len(rows)} rows; metadata says {metadata['trace_frame_count']}")
    for row in rows:
        n = int(row["frame"], 16)
        frame = offset + n + 1
        player = probe.get(frame)
        if player is None or not player.get("accepted"):
            errors.append(f"row {n}: probe frame {frame} missing or not accepted as R11A")
            continue
        for column, value in expected_columns(player).items():
            checked += 1
            if int(row[column], 16) != value:
                errors.append(f"row {n} (frame {frame}) {column}: trace {row[column]} reference {value:04X}")
        checked += 1
        if lag.get(n) != av[frame]["lag"]:
            errors.append(f"row {n} (frame {frame}) lag: trace {lag.get(n)} reference {av[frame]['lag']}")
    first = probe.get(offset + 1)
    if first is not None and first.get("accepted"):
        g = first["globals"]
        for key, value in (("zone_id", g["zone"]), ("act", g["act"] + 1), ("time_zone_id", g["timeZone"])):
            checked += 1
            if metadata[key] != value:
                errors.append(f"metadata {key}: trace {metadata[key]} reference {value}")
    return rows, checked, errors


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("trace", type=Path)
    parser.add_argument("--player", type=Path, required=True, help="probe player.jsonl")
    parser.add_argument("--av", type=Path, required=True, help="probe av.jsonl")
    args = parser.parse_args(argv)
    rows, checked, errors = compare(args.trace, load_jsonl(args.player), load_jsonl(args.av))
    if not rows or checked == 0:
        print("scd reference comparison failed: nothing was compared", file=sys.stderr)
        return 1
    if errors:
        print(f"scd reference comparison FAILED: {len(errors)} mismatches over {len(rows)} rows",
              file=sys.stderr)
        print(*errors[:20], sep="\n", file=sys.stderr)
        return 1
    print(f"scd reference comparison passed: {len(rows)} rows, {checked} values")
    return 0


if __name__ == "__main__":
    sys.exit(main())
