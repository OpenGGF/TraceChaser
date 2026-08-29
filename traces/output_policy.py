#!/usr/bin/env python3
"""Require an explicit output root outside producer and consumer checkouts."""

from __future__ import annotations

import argparse
from pathlib import Path


def require_external_output_root(output_root: Path, tracechaser_root: Path,
                                 input_repository_root: Path) -> Path:
    if not output_root.is_absolute():
        raise ValueError("output root must be an explicit absolute path outside both source trees")
    resolved = output_root.resolve()
    protected = (tracechaser_root.resolve(), input_repository_root.resolve())
    if any(resolved == root or resolved.is_relative_to(root) for root in protected):
        raise ValueError("output root must remain outside both source trees")
    return resolved


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tracechaser-root", required=True, type=Path)
    parser.add_argument("--input-repository-root", required=True, type=Path)
    parser.add_argument("--output-root", required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        print(require_external_output_root(
            args.output_root, args.tracechaser_root, args.input_repository_root))
    except ValueError as error:
        parser.error(str(error))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
