#!/usr/bin/env python3
"""Generate the synthetic trace-v5 semantic conformance pack."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys

if __package__ in {None, ""}:
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from traces.v5_conformance import build_pack


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("destination", type=Path)
    options = parser.parse_args()
    build_pack(options.destination)
    print(f"generated trace v5 conformance pack: {options.destination}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
