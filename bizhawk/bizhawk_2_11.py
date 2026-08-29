#!/usr/bin/env python3
"""Verify and acquire TraceChaser's exact BizHawk 2.11 Linux runtime."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Callable


TRACECHASER_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_LOCK_PATH = (
    TRACECHASER_ROOT / "dependencies" / "bizhawk-2.11-linux-x64.lock.json"
)


class DependencyError(RuntimeError):
    """A locked runtime archive or installation failed verification."""


@dataclass(frozen=True)
class PreflightReport:
    home: Path
    version: str
    detected_version_raw: str
    expected_version_raw: str
    lua_capabilities: tuple[str, ...]


def load_runtime_lock(path: Path = DEFAULT_LOCK_PATH) -> dict:
    try:
        lock = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise DependencyError(f"runtime archive lock is unavailable: {path}") from error
    if lock.get("schema") != "tracechaser.bizhawk-runtime-archive-lock.v1":
        raise DependencyError(f"runtime archive lock schema is unsupported: {path}")
    if lock.get("consumer") != "official-linux-runtime":
        raise DependencyError(f"runtime archive lock consumer is unsupported: {path}")
    release = lock.get("release")
    if not isinstance(release, dict):
        raise DependencyError(f"runtime archive lock release is missing: {path}")
    for field in (
        "version",
        "assembly_version",
        "assembly_version_raw",
        "archive_name",
        "official_url",
        "sha256",
        "install_directory",
    ):
        if not isinstance(release.get(field), str) or not release[field]:
            raise DependencyError(f"runtime archive lock field is missing: {field}")
    if not re.fullmatch(r"[0-9a-f]{64}", release["sha256"]):
        raise DependencyError("runtime archive SHA-256 is malformed")
    return lock


def _default_version_probe(assembly: Path) -> str:
    executable = shutil.which("monodis")
    if executable is None:
        raise DependencyError(
            "managed-version probe is unavailable: install Mono's monodis"
        )
    process = subprocess.run(
        [executable, "--assembly", str(assembly)],
        capture_output=True,
        text=True,
        check=False,
    )
    raw = (process.stdout + process.stderr).strip()
    if process.returncode != 0:
        raise DependencyError(
            f"managed-version probe failed for {assembly}: detected raw={raw!r}"
        )
    return raw


def _version_evidence(raw: str) -> tuple[str | None, str]:
    match = re.search(r"(?m)^\s*Version:\s*([^\s]+)\s*$", raw)
    if match is None:
        return None, raw
    return match.group(1), match.group(0).strip()


def preflight_installation(
    home: Path,
    lock: dict | None = None,
    *,
    version_probe: Callable[[Path], str] = _default_version_probe,
) -> PreflightReport:
    """Validate one explicit BizHawk home without modifying it."""
    runtime_lock = load_runtime_lock() if lock is None else lock
    release = runtime_lock["release"]
    expected_version = release["assembly_version"]
    expected_raw = release["assembly_version_raw"]
    resolved_home = home.expanduser().resolve()
    if not resolved_home.is_dir():
        raise DependencyError(f"BizHawk home is unavailable: {resolved_home}")

    required_files = runtime_lock.get("required_files")
    if not isinstance(required_files, list) or not required_files:
        raise DependencyError("runtime archive required-file contract is missing")
    for relative in required_files:
        if not isinstance(relative, str) or not relative:
            raise DependencyError("runtime archive required-file contract is malformed")
        path = resolved_home / relative
        if not path.is_file():
            raise DependencyError(
                f"required runtime file is missing: detected raw='missing'; "
                f"expected raw={relative!r}"
            )

    versioned_files = runtime_lock.get("versioned_files")
    if not isinstance(versioned_files, list) or not versioned_files:
        raise DependencyError("runtime archive version contract is missing")
    first_detected_raw = ""
    for relative in versioned_files:
        assembly = resolved_home / relative
        raw_output = version_probe(assembly)
        detected_version, detected_raw = _version_evidence(raw_output)
        if not first_detected_raw:
            first_detected_raw = detected_raw
        if detected_version != expected_version:
            raise DependencyError(
                f"unsupported BizHawk managed version in {relative}: "
                f"detected raw={detected_raw!r}; expected raw={expected_raw!r}"
            )

    capabilities = runtime_lock.get("lua_capabilities")
    if not isinstance(capabilities, list) or not capabilities:
        raise DependencyError("Lua capability contract is missing")
    verified_capabilities: list[str] = []
    seen_capabilities: set[str] = set()
    assembly_cache: dict[str, bytes] = {}
    for capability in capabilities:
        if not isinstance(capability, dict):
            raise DependencyError("Lua capability contract is malformed")
        api = capability.get("api")
        relative = capability.get("assembly")
        library_marker = capability.get("library_marker")
        method_marker = capability.get("method_marker")
        if not all(
            isinstance(value, str) and value
            for value in (api, relative, library_marker, method_marker)
        ):
            raise DependencyError("Lua capability contract is malformed")
        if api in seen_capabilities:
            raise DependencyError(f"Lua capability is duplicated: {api}")
        seen_capabilities.add(api)
        if relative not in assembly_cache:
            assembly_cache[relative] = (resolved_home / relative).read_bytes()
        content = assembly_cache[relative]
        for marker in (library_marker, method_marker):
            if marker.encode("utf-8") not in content:
                raise DependencyError(
                    f"required Lua capability is unavailable: {api}: "
                    f"detected raw='missing'; expected raw={marker!r}"
                )
        example_path = capability.get("example_path")
        example_marker = capability.get("example_marker")
        if (example_path is None) != (example_marker is None):
            raise DependencyError(f"Lua capability example contract is malformed: {api}")
        if example_path is not None:
            if not isinstance(example_path, str) or not isinstance(example_marker, str):
                raise DependencyError(f"Lua capability example contract is malformed: {api}")
            example_content = (resolved_home / example_path).read_bytes()
            if example_marker.encode("utf-8") not in example_content:
                raise DependencyError(
                    f"required Lua capability example is unavailable: {api}: "
                    f"detected raw='missing'; expected raw={example_marker!r}"
                )
        verified_capabilities.append(api)

    return PreflightReport(
        home=resolved_home,
        version=release["version"],
        detected_version_raw=first_detected_raw,
        expected_version_raw=expected_raw,
        lua_capabilities=tuple(verified_capabilities),
    )


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def verify_archive(
    archive: Path,
    lock: dict | None = None,
    *,
    extractor: Callable[[Path], None] | None = None,
) -> str:
    """Verify the complete archive before optionally handing it to an extractor."""
    runtime_lock = load_runtime_lock() if lock is None else lock
    expected = runtime_lock["release"]["sha256"]
    if not archive.is_file():
        raise DependencyError(f"BizHawk archive is unavailable: {archive}")
    detected = _sha256(archive)
    if detected != expected:
        raise DependencyError(
            "BizHawk archive SHA-256 mismatch: "
            f"detected raw={detected!r}; expected raw={expected!r}"
        )
    if extractor is not None:
        extractor(archive)
    return detected


def _extract_archive(archive: Path, staging_root: Path) -> None:
    try:
        with tarfile.open(archive, mode="r:gz") as source:
            source.extractall(staging_root, filter="data")
    except (OSError, tarfile.TarError) as error:
        raise DependencyError(f"BizHawk archive extraction failed: {archive}") from error


def _download_archive(url: str, destination: Path) -> None:
    try:
        with urllib.request.urlopen(url) as response, destination.open("wb") as output:
            shutil.copyfileobj(response, output)
    except OSError as error:
        raise DependencyError(f"official BizHawk download failed: {url}") from error


def acquire_archive(
    repository_root: Path,
    lock: dict | None = None,
    *,
    archive_path: Path | None = None,
    version_probe: Callable[[Path], str] = _default_version_probe,
) -> Path:
    """Install a verified runtime below the checkout-local .dependencies only."""
    runtime_lock = load_runtime_lock() if lock is None else lock
    release = runtime_lock["release"]
    resolved_root = repository_root.resolve()
    if not resolved_root.is_dir():
        raise DependencyError(f"TraceChaser root is unavailable: {resolved_root}")
    dependencies = resolved_root / ".dependencies"
    if dependencies.is_symlink():
        raise DependencyError(f"dependency root must not be a symlink: {dependencies}")
    dependencies.mkdir(mode=0o755, exist_ok=True)
    destination = dependencies / release["install_directory"]
    if destination.exists() or destination.is_symlink():
        raise DependencyError(
            f"BizHawk destination already exists and will not be replaced: {destination}"
        )

    staging_root = Path(
        tempfile.mkdtemp(prefix=".bizhawk-2.11-install.", dir=dependencies)
    )
    try:
        if archive_path is None:
            staged_archive = staging_root / release["archive_name"]
            _download_archive(release["official_url"], staged_archive)
        else:
            staged_archive = archive_path.expanduser().resolve()

        verify_archive(
            staged_archive,
            runtime_lock,
            extractor=lambda archive: _extract_archive(archive, staging_root),
        )
        staged_install = staging_root / release["install_directory"]
        if not staged_install.is_dir() or staged_install.is_symlink():
            raise DependencyError(
                "BizHawk archive layout mismatch: "
                f"expected raw={release['install_directory']!r}; detected raw='missing'"
            )
        preflight_installation(
            staged_install, runtime_lock, version_probe=version_probe
        )
        if destination.exists() or destination.is_symlink():
            raise DependencyError(
                f"BizHawk destination appeared and was left untouched: {destination}"
            )
        os.rename(staged_install, destination)
        return destination
    finally:
        shutil.rmtree(staging_root, ignore_errors=True)


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Acquire or preflight the exact official BizHawk 2.11 Linux runtime."
    )
    subparsers = parser.add_subparsers(dest="command", required=True)
    preflight = subparsers.add_parser("preflight")
    preflight.add_argument("--bizhawk-home", required=True, type=Path)
    acquire = subparsers.add_parser("acquire")
    acquire.add_argument(
        "--archive",
        type=Path,
        help="Use an already-downloaded official archive; no network is used.",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    arguments = _build_parser().parse_args(argv)
    try:
        lock = load_runtime_lock()
        if arguments.command == "preflight":
            report = preflight_installation(arguments.bizhawk_home, lock)
            print("BizHawk runtime preflight: PASS")
            print(f"home: {report.home}")
            print(f"detected raw: {report.detected_version_raw}")
            print(f"expected raw: {report.expected_version_raw}")
            print(f"Lua capabilities: {len(report.lua_capabilities)} verified")
        else:
            destination = acquire_archive(
                TRACECHASER_ROOT, lock, archive_path=arguments.archive
            )
            print(f"Installed exact BizHawk 2.11 Linux runtime at {destination}")
        return 0
    except DependencyError as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
