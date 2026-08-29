#!/usr/bin/env python3
import argparse
from pathlib import Path
import subprocess
import sys

try:
    from artifact_policy import (
        blob_content_violations,
        display_path,
        license_content_violations,
        MACHINE_PATH_PATTERN,
        MAX_BLOB_BYTES,
        path_violations,
    )
except ModuleNotFoundError:
    from testing.artifact_policy import (
        blob_content_violations,
        display_path,
        license_content_violations,
        MACHINE_PATH_PATTERN,
        MAX_BLOB_BYTES,
        path_violations,
    )


def find_violations(root: Path) -> list[str]:
    root = root.resolve()
    violations = []
    for object_id, path in _tracked_blobs(root):
        policy_path = path.decode("utf-8", "surrogateescape")
        rendered_path = display_path(path)
        violations.extend(
            f"path={rendered_path} reason={reason}"
            for reason in path_violations(policy_path)
        )

        object_type = _git(root, "cat-file", "-t", object_id).stdout.strip()
        if object_type != b"blob":
            continue
        size = int(_git(root, "cat-file", "-s", object_id).stdout)
        content = b""
        if size <= MAX_BLOB_BYTES:
            content = _git(root, "cat-file", "blob", object_id).stdout
        violations.extend(
            f"path={rendered_path} reason={reason}"
            for reason in blob_content_violations(
                size,
                content[:512],
                bool(MACHINE_PATH_PATTERN.search(content)),
            )
        )
        violations.extend(
            f"path={rendered_path} reason={reason}"
            for reason in license_content_violations(policy_path, content)
        )
    return sorted(set(violations))


def _tracked_blobs(root: Path) -> list[tuple[str, bytes]]:
    output = _git(root, "ls-files", "--stage", "-z").stdout
    tracked = []
    for record in output.split(b"\x00"):
        if not record:
            continue
        metadata, separator, path = record.partition(b"\t")
        if not separator:
            raise RuntimeError("malformed git ls-files record")
        _mode, object_id, _stage = metadata.split(b" ")
        tracked.append((object_id.decode("ascii"), path))
    return tracked


def _git(root: Path, *arguments: str) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        ["git", "-C", str(root), *arguments],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=True,
    )


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Audit tracked repository artifacts")
    parser.add_argument("--root", type=Path, default=Path.cwd())
    options = parser.parse_args(arguments)

    violations = find_violations(options.root)
    if violations:
        for violation in violations:
            print(violation)
        return 1
    print("repository policy: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
