#!/usr/bin/env python3
import argparse
from dataclasses import dataclass
import hashlib
from pathlib import Path
import subprocess
import sys

try:
    from artifact_policy import (
        ARCHIVE_PREFIXES,
        EXECUTABLE_PREFIXES,
        EXACT_LICENSE_SHA256,
        MACHINE_PATH_PATTERN,
        MAX_BLOB_BYTES,
        path_violations,
    )
except ModuleNotFoundError:
    from testing.artifact_policy import (
        ARCHIVE_PREFIXES,
        EXECUTABLE_PREFIXES,
        EXACT_LICENSE_SHA256,
        MACHINE_PATH_PATTERN,
        MAX_BLOB_BYTES,
        path_violations,
    )


CHUNK_BYTES = 64 * 1024
CONTENT_OVERLAP_BYTES = 512


@dataclass(frozen=True, order=True)
class Violation:
    commit: str
    object_id: str
    path: str
    reason: str

    def render(self) -> str:
        return (
            f"commit={self.commit} object={self.object_id} "
            f"path={self.path} reason={self.reason}"
        )


def find_violations(root: Path) -> list[Violation]:
    root = root.resolve()
    object_paths = _reachable_objects(root)
    object_metadata = _object_metadata(root, object_paths)
    violations = []

    for path in _historical_paths(root, object_paths, object_metadata):
        reasons = path_violations(path)
        if not reasons:
            continue
        commit, object_id = _locate_path(root, path)
        violations.extend(Violation(commit, object_id, path, reason) for reason in reasons)

    for object_id, paths in object_paths.items():
        object_type, size = object_metadata[object_id]
        if object_type != "blob":
            continue
        path = min(paths) if paths else "<unknown>"
        reasons = _scan_blob(root, object_id, size)
        if not reasons:
            continue
        commit = _locate_object(root, object_id, path)
        violations.extend(Violation(commit, object_id, path, reason) for reason in reasons)

    violations.extend(_license_content_violations(root))

    return sorted(set(violations))


def _license_content_violations(root: Path) -> list[Violation]:
    violations = []
    for path, expected_sha256 in EXACT_LICENSE_SHA256.items():
        for commit, object_id in _blob_versions_for_path(root, path).values():
            content = subprocess.run(
                ["git", "-C", str(root), "cat-file", "blob", object_id],
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                check=True,
            ).stdout
            if hashlib.sha256(content).hexdigest() != expected_sha256:
                violations.append(
                    Violation(commit, object_id, path, "unapproved license or notice content")
                )
    return violations


def _blob_versions_for_path(root: Path, path: str) -> dict[str, tuple[str, str]]:
    commits = _git(root, "log", "--all", "--format=%H", "--", path).stdout.splitlines()
    versions = {}
    for commit in commits:
        tree = _git(root, "ls-tree", commit, "--", path)
        if not tree.stdout:
            continue
        metadata, _, _ = tree.stdout.partition("\t")
        _mode, object_type, object_id = metadata.split()
        if object_type == "blob":
            versions.setdefault(object_id, (commit, object_id))
    return versions


def _reachable_objects(root: Path) -> dict[str, set[str]]:
    result = _git(root, "rev-list", "--objects", "--all")
    objects: dict[str, set[str]] = {}
    for line in result.stdout.splitlines():
        object_id, separator, path = line.partition(" ")
        objects.setdefault(object_id, set())
        if separator and path:
            objects[object_id].add(path)
    return objects


def _object_metadata(root: Path, objects: dict[str, set[str]]) -> dict[str, tuple[str, int]]:
    process = subprocess.run(
        ["git", "-C", str(root), "cat-file", "--batch-check=%(objectname) %(objecttype) %(objectsize)"],
        input="".join(f"{object_id}\n" for object_id in objects),
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=True,
    )
    metadata = {}
    for line in process.stdout.splitlines():
        object_id, object_type, size = line.split()
        metadata[object_id] = (object_type, int(size))
    return metadata


def _historical_paths(
    root: Path,
    object_paths: dict[str, set[str]],
    object_metadata: dict[str, tuple[str, int]],
) -> list[str]:
    paths = {
        path
        for object_id, values in object_paths.items()
        if object_metadata[object_id][0] == "blob"
        for path in values
    }
    result = _git(root, "log", "--all", "--format=", "--name-only", "--no-renames")
    paths.update(line for line in result.stdout.splitlines() if line.strip())
    return sorted(paths)


def _locate_path(root: Path, path: str) -> tuple[str, str]:
    result = _git(root, "log", "--all", "--format=%H", "--diff-filter=AM", "--", path)
    for commit in reversed(result.stdout.splitlines()):
        tree = _git(root, "ls-tree", commit, "--", path)
        if not tree.stdout:
            continue
        metadata, _, _ = tree.stdout.partition("\t")
        _mode, object_type, object_id = metadata.split()
        if object_type == "blob":
            return commit, object_id
    raise RuntimeError(f"could not locate reachable path {path!r}")


def _locate_object(root: Path, object_id: str, path: str) -> str:
    arguments = ["log", "--all", "--format=%H", f"--find-object={object_id}"]
    if path != "<unknown>":
        arguments.extend(["--", path])
    result = _git(root, *arguments)
    commits = result.stdout.splitlines()
    if not commits:
        raise RuntimeError(f"could not locate reachable blob {object_id}")
    return commits[-1]


def _scan_blob(root: Path, object_id: str, size: int) -> list[str]:
    if size > MAX_BLOB_BYTES:
        return [f"blob exceeds {MAX_BLOB_BYTES} bytes"]

    process = subprocess.Popen(
        ["git", "-C", str(root), "cat-file", "blob", object_id],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    assert process.stdout is not None
    prefix = bytearray()
    overlap = b""
    found_machine_path = False
    while True:
        chunk = process.stdout.read(CHUNK_BYTES)
        if not chunk:
            break
        if len(prefix) < 512:
            prefix.extend(chunk[: 512 - len(prefix)])
        window = overlap + chunk
        if MACHINE_PATH_PATTERN.search(window):
            found_machine_path = True
        overlap = window[-CONTENT_OVERLAP_BYTES:]
    stderr = process.stderr.read() if process.stderr is not None else b""
    return_code = process.wait()
    if return_code:
        raise RuntimeError(f"git cat-file failed for {object_id}: {stderr!r}")

    reasons = []
    prefix_bytes = bytes(prefix)
    if prefix_bytes.startswith(ARCHIVE_PREFIXES) or prefix_bytes[257:262] == b"ustar":
        reasons.append("archive or BK2 magic")
    if prefix_bytes.startswith(EXECUTABLE_PREFIXES):
        reasons.append("executable binary magic")
    if prefix_bytes[0x100:0x104] == b"SEGA":
        reasons.append("Mega Drive ROM magic")
    if found_machine_path:
        reasons.append("machine-local absolute path")
    return reasons


def _git(root: Path, *arguments: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", "-C", str(root), *arguments],
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=True,
    )


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Audit every Git blob reachable from all refs")
    parser.add_argument("--root", type=Path, default=Path.cwd())
    options = parser.parse_args(arguments)

    violations = find_violations(options.root)
    if violations:
        for violation in violations:
            print(violation.render())
        return 1
    print("history audit: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
