#!/usr/bin/env python3
"""Require an explicit output root outside producer and consumer checkouts."""

from __future__ import annotations

import argparse
import os
from pathlib import Path


def require_external_output_root(output_root: Path, tracechaser_root: Path,
                                 input_repository_root: Path) -> Path:
    if not output_root.is_absolute():
        raise ValueError("output root must be an explicit absolute path outside both source trees")
    for label, root in (
        ("TraceChaser root", tracechaser_root),
        ("consumer root", input_repository_root),
    ):
        if not root.is_absolute() or not root.is_dir():
            raise ValueError(f"{label} must be an existing absolute directory")
    resolved = output_root.resolve()
    protected = (tracechaser_root.resolve(), input_repository_root.resolve())
    if any(resolved == root or resolved.is_relative_to(root) for root in protected):
        raise ValueError("output root must remain outside both source trees")
    return resolved


def require_consumer_fixture_root(fixture_root: Path, input_repository_root: Path) -> Path:
    if not fixture_root.is_absolute():
        raise ValueError("fixture root must be an explicit absolute path")
    resolved = fixture_root.resolve(strict=True)
    consumer = input_repository_root.resolve(strict=True)
    if resolved != consumer and not resolved.is_relative_to(consumer):
        raise ValueError("fixture root must belong to the explicit consumer checkout")
    return resolved


def require_lua_output_request(encoded: str) -> Path:
    try:
        fields = bytes.fromhex(encoded).split(b"\0")
    except ValueError as error:
        raise ValueError("malformed Lua path-policy request") from error
    if len(fields) != 3 or any(not field for field in fields):
        raise ValueError("malformed Lua path-policy request")
    output_root, supplied_tracechaser, consumer_root = (
        Path(os.fsdecode(field)) for field in fields
    )
    installed_tracechaser = Path(__file__).resolve().parents[1]
    if supplied_tracechaser.resolve() != installed_tracechaser:
        raise ValueError("TraceChaser root does not own the installed path-policy helper")
    return require_external_output_root(
        output_root, installed_tracechaser, consumer_root
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tracechaser-root", type=Path)
    parser.add_argument("--input-repository-root", type=Path)
    parser.add_argument("--output-root", type=Path)
    parser.add_argument("--fixture-root", type=Path)
    parser.add_argument("--lua-request-hex")
    args = parser.parse_args(argv)
    try:
        if args.lua_request_hex is not None:
            if any(value is not None for value in (
                args.tracechaser_root, args.input_repository_root,
                args.output_root, args.fixture_root,
            )):
                raise ValueError("Lua path-policy request cannot be combined with other inputs")
            print(require_lua_output_request(args.lua_request_hex))
            return 0
        if None in (args.tracechaser_root, args.input_repository_root, args.output_root):
            raise ValueError("tracechaser, consumer, and output roots are required")
        print(require_external_output_root(
            args.output_root, args.tracechaser_root, args.input_repository_root))
        if args.fixture_root is not None:
            require_consumer_fixture_root(args.fixture_root, args.input_repository_root)
    except ValueError as error:
        parser.error(str(error))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
