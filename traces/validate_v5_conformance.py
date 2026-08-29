#!/usr/bin/env python3
"""Validate the committed trace-v5 semantic conformance pack."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys

if __package__ in {None, ""}:
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from traces.v5_conformance import validate_pack


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", type=Path,
                        default=Path(__file__).resolve().parents[1] / "contracts" / "v5")
    options = parser.parse_args()
    errors = validate_pack(options.root)
    if errors:
        print(*errors, sep="\n")
        return 1
    print(f"trace v5 conformance: PASS ({options.root})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
