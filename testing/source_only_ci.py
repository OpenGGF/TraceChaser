#!/usr/bin/env python3
"""Fail closed on source-only unittest failures or skips."""

from __future__ import annotations

import argparse
import re
from pathlib import Path


SKIP_LINE = re.compile(r"^.* \.\.\. skipped .*$", re.MULTILINE)


def audit_unittest_result(output: str, status: int) -> None:
    if status != 0:
        raise ValueError(f"unittest exited with status {status}")
    skips = SKIP_LINE.findall(output)
    if skips:
        raise ValueError("source-only unittest skip is forbidden: " + skips[0])
    if not re.search(r"^Ran [1-9][0-9]* tests? in ", output, re.MULTILINE):
        raise ValueError("unittest result is missing a nonempty run summary")
    if not re.search(r"^OK$", output, re.MULTILINE):
        raise ValueError("unittest result is missing the exact zero-skip OK summary")


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--log", required=True, type=Path)
    parser.add_argument("--status", required=True, type=int)
    options = parser.parse_args(arguments)
    try:
        output = options.log.read_text(encoding="utf-8")
        audit_unittest_result(output, options.status)
    except (OSError, UnicodeDecodeError, ValueError) as error:
        parser.error(str(error))
    print("source-only unittest audit: PASS (zero failures, errors, or skips)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
