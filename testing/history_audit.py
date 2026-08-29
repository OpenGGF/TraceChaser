#!/usr/bin/env python3
import argparse
from dataclasses import dataclass
from pathlib import Path
import subprocess
import sys

try:
    from artifact_policy import (
        audit_contract_pack,
        blob_content_violations,
        BlobSnapshot,
        ContractPackAudit,
        display_path,
        EXACT_LICENSE_PATHS,
        is_regular_git_mode,
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
        ContractPackAudit,
        display_path,
        EXACT_LICENSE_PATHS,
        is_regular_git_mode,
        license_content_violations,
        MACHINE_PATH_PATTERN,
        MAX_BLOB_BYTES,
        path_violations,
    )


CHUNK_BYTES = 64 * 1024
CONTENT_OVERLAP_BYTES = 512


@dataclass(frozen=True, order=True)
class Occurrence:
    commit: str
    path: bytes
    mode: str


@dataclass(frozen=True)
class Violation:
    commit: str
    object_id: str
    path: bytes | None
    reason: str

    def render(self) -> str:
        return (
            f"commit={self.commit} object={self.object_id} "
            f"path={display_path(self.path)} reason={self.reason}"
        )


def find_violations(root: Path) -> list[Violation]:
    root = root.resolve()
    objects = _reachable_objects(root)
    object_metadata = _object_metadata(root, objects)
    occurrences = _committed_blob_occurrences(root, object_metadata)
    contract_audits, contract_objects = _contract_audits(
        root, occurrences, object_metadata
    )
    violations = []

    for object_id, blob_occurrences in occurrences.items():
        for occurrence in blob_occurrences:
            policy_path = occurrence.path.decode("utf-8", "surrogateescape")
            curated_contract_member = (
                policy_path
                in contract_audits.get(
                    occurrence.commit, ContractPackAudit(frozenset(), ())
                ).allowed_paths
            )
            violations.extend(
                Violation(occurrence.commit, object_id, occurrence.path, reason)
                for reason in path_violations(policy_path, curated_contract_member)
            )

    for object_id in sorted(objects):
        object_type, size = object_metadata[object_id]
        if object_type != "blob":
            continue
        reasons = _scan_blob(root, object_id, size)
        if not reasons:
            continue
        blob_occurrences = occurrences.get(object_id)
        if blob_occurrences:
            for occurrence in blob_occurrences:
                policy_path = occurrence.path.decode("utf-8", "surrogateescape")
                curated_gzip_member = (
                    policy_path.endswith(".gz")
                    and policy_path
                    in contract_audits.get(
                        occurrence.commit, ContractPackAudit(frozenset(), ())
                    ).allowed_paths
                )
                occurrence_reasons = reasons
                if curated_gzip_member:
                    occurrence_reasons = [
                        reason for reason in reasons if reason != "archive or BK2 magic"
                    ]
                violations.extend(
                    Violation(occurrence.commit, object_id, occurrence.path, reason)
                    for reason in occurrence_reasons
                )
        else:
            violations.extend(
                Violation("<direct>", object_id, None, reason) for reason in reasons
            )

    for commit, audit in contract_audits.items():
        for policy_path, reason in audit.violations:
            object_id, raw_path = contract_objects[(commit, policy_path)]
            violations.append(Violation(commit, object_id, raw_path, reason))

    violations.extend(_license_content_violations(root, occurrences))

    return sorted(set(violations), key=_violation_sort_key)


def _contract_audits(
    root: Path,
    occurrences: dict[str, tuple[Occurrence, ...]],
    object_metadata: dict[str, tuple[str, int]],
) -> tuple[
    dict[str, ContractPackAudit],
    dict[tuple[str, str], tuple[str, bytes]],
]:
    files_by_commit: dict[str, dict[str, BlobSnapshot]] = {}
    contract_objects: dict[tuple[str, str], tuple[str, bytes]] = {}
    snapshots: dict[tuple[str, str], BlobSnapshot] = {}
    for object_id, blob_occurrences in occurrences.items():
        for occurrence in blob_occurrences:
            policy_path = occurrence.path.decode("utf-8", "surrogateescape")
            if not policy_path.startswith("contracts/"):
                continue
            snapshot_key = (object_id, occurrence.mode)
            if snapshot_key not in snapshots:
                size = 0
                content = None
                if is_regular_git_mode(occurrence.mode):
                    object_type, size = object_metadata[object_id]
                    if object_type == "blob" and size <= MAX_BLOB_BYTES:
                        content = _git_bytes(root, "cat-file", "blob", object_id).stdout
                snapshots[snapshot_key] = BlobSnapshot(size, content, occurrence.mode)
            files_by_commit.setdefault(occurrence.commit, {})[policy_path] = snapshots[
                snapshot_key
            ]
            contract_objects[(occurrence.commit, policy_path)] = (
                object_id,
                occurrence.path,
            )
    audits = {
        commit: audit_contract_pack(files)
        for commit, files in files_by_commit.items()
    }
    return audits, contract_objects


def _license_content_violations(
    root: Path,
    occurrences: dict[str, tuple[Occurrence, ...]],
) -> list[Violation]:
    violations = []
    for path in EXACT_LICENSE_PATHS:
        path_bytes = path.encode("utf-8")
        matching = {
            object_id: tuple(
                occurrence
                for occurrence in blob_occurrences
                if occurrence.path == path_bytes
            )
            for object_id, blob_occurrences in occurrences.items()
            if any(occurrence.path == path_bytes for occurrence in blob_occurrences)
        }
        for object_id, blob_occurrences in matching.items():
            content = subprocess.run(
                ["git", "-C", str(root), "cat-file", "blob", object_id],
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                check=True,
            ).stdout
            reasons = license_content_violations(path, content)
            if reasons:
                violations.extend(
                    Violation(
                        occurrence.commit,
                        object_id,
                        occurrence.path,
                        reason,
                    )
                    for occurrence in blob_occurrences
                    for reason in reasons
                )
    return violations


def _reachable_objects(root: Path) -> set[str]:
    result = _git_bytes(root, "rev-list", "--objects", "--no-object-names", "--all")
    return {line.decode("ascii") for line in result.stdout.splitlines() if line}


def _object_metadata(root: Path, objects: set[str]) -> dict[str, tuple[str, int]]:
    process = subprocess.run(
        ["git", "-C", str(root), "cat-file", "--batch-check=%(objectname) %(objecttype) %(objectsize)"],
        input="".join(f"{object_id}\n" for object_id in sorted(objects)),
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


def _committed_blob_occurrences(
    root: Path,
    object_metadata: dict[str, tuple[str, int]],
) -> dict[str, tuple[Occurrence, ...]]:
    occurrences: dict[str, set[Occurrence]] = {}
    commits = sorted(
        object_id
        for object_id, (object_type, _size) in object_metadata.items()
        if object_type == "commit"
    )
    for commit in commits:
        tree = _git_bytes(root, "ls-tree", "-r", "-z", "--full-tree", commit).stdout
        for record in tree.split(b"\x00"):
            if not record:
                continue
            metadata, separator, path = record.partition(b"\t")
            if not separator:
                raise RuntimeError(f"malformed ls-tree record in commit {commit}")
            mode, object_type, object_id_bytes = metadata.split(b" ")
            policy_path = path.decode("utf-8", "surrogateescape")
            if object_type != b"blob" and not policy_path.startswith(
                "contracts/v5/"
            ):
                continue
            object_id = object_id_bytes.decode("ascii")
            occurrences.setdefault(object_id, set()).add(
                Occurrence(commit, path, mode.decode("ascii"))
            )
    return {
        object_id: tuple(sorted(blob_occurrences))
        for object_id, blob_occurrences in occurrences.items()
    }


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

    return blob_content_violations(size, bytes(prefix), found_machine_path)


def _violation_sort_key(violation: Violation) -> tuple[str, str, bytes, str]:
    return (
        violation.commit,
        violation.object_id,
        violation.path if violation.path is not None else b"",
        violation.reason,
    )


def _git_bytes(root: Path, *arguments: str) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        ["git", "-C", str(root), *arguments],
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
