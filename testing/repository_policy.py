#!/usr/bin/env python3
import argparse
from pathlib import Path
import subprocess
import sys

try:
    from artifact_policy import (
        audit_contract_pack,
        blob_content_violations,
        BlobSnapshot,
        display_path,
        license_content_violations,
        MACHINE_PATH_PATTERN,
        MAX_BLOB_BYTES,
        path_violations,
    )
except ModuleNotFoundError:
    from testing.artifact_policy import (
        audit_contract_pack,
        blob_content_violations,
        BlobSnapshot,
        display_path,
        license_content_violations,
        MACHINE_PATH_PATTERN,
        MAX_BLOB_BYTES,
        path_violations,
    )


def find_violations(root: Path) -> list[str]:
    root = root.resolve()
    violations = []
    tracked = _tracked_blobs(root)
    snapshots = {
        path.decode("utf-8", "surrogateescape"): _blob_snapshot(root, object_id)
        for object_id, path in tracked
    }
    contract_audit = audit_contract_pack(snapshots)
    for object_id, path in tracked:
        policy_path = path.decode("utf-8", "surrogateescape")
        rendered_path = display_path(path)
        curated_contract_member = policy_path in contract_audit.allowed_paths
        violations.extend(
            f"path={rendered_path} reason={reason}"
            for reason in path_violations(policy_path, curated_contract_member)
        )

        snapshot = snapshots[policy_path]
        if snapshot.content is None and snapshot.size == 0:
            continue
        content = snapshot.content or b""
        violations.extend(
            f"path={rendered_path} reason={reason}"
            for reason in blob_content_violations(
                snapshot.size,
                content[:512],
                bool(MACHINE_PATH_PATTERN.search(content)),
                curated_contract_member and policy_path.endswith(".gz"),
            )
        )
        violations.extend(
            f"path={rendered_path} reason={reason}"
            for reason in license_content_violations(policy_path, content)
        )
    violations.extend(
        f"path={path} reason={reason}" for path, reason in contract_audit.violations
    )
    return sorted(set(violations))


def _blob_snapshot(root: Path, object_id: str) -> BlobSnapshot:
    object_type = _git(root, "cat-file", "-t", object_id).stdout.strip()
    if object_type != b"blob":
        return BlobSnapshot(0, None)
    size = int(_git(root, "cat-file", "-s", object_id).stdout)
    content = None
    if size <= MAX_BLOB_BYTES:
        content = _git(root, "cat-file", "blob", object_id).stdout
    return BlobSnapshot(size, content)


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
