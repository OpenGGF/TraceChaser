import hashlib
from pathlib import PurePosixPath
import re


MAX_BLOB_BYTES = 1024 * 1024

EXACT_LICENSE_PATHS = frozenset(
    {
        "LICENSE",
        "bizhawk-headless/native/gpgx-audio-observer/notices/zstd-LICENSE",
    }
)

EXACT_LICENSE_SHA256 = {
    "LICENSE": "3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986",
    "bizhawk-headless/native/gpgx-audio-observer/notices/zstd-LICENSE": (
        "7055266497633c9025b777c78eb7235af13922117480ed5c674677adc381c9d8"
    ),
}

FORBIDDEN_COMPONENTS = frozenset(
    {
        ".dependencies",
        ".gradle",
        ".mypy_cache",
        ".pytest_cache",
        ".scratch",
        "__pycache__",
        "bin",
        "build",
        "dist",
        "node_modules",
        "obj",
        "out",
        "packages",
        "scratch",
        "target",
    }
)

FORBIDDEN_SUFFIXES = (
    ".tar.gz",
    ".tar.xz",
    ".physics.csv.gz",
    ".aux_state.jsonl.gz",
    ".hardware_timing.jsonl.gz",
    ".bk2",
    ".gen",
    ".smd",
    ".rom",
    ".32x",
    ".exe",
    ".dll",
    ".pdb",
    ".so",
    ".dylib",
    ".o",
    ".class",
    ".jar",
    ".zip",
    ".7z",
    ".rar",
    ".tar",
    ".tgz",
    ".gz",
    ".xz",
    ".log",
    ".out",
)

RAW_TRACE_NAMES = frozenset(
    {
        "physics.csv",
        "physics.csv.gz",
        "aux_state.jsonl",
        "aux_state.jsonl.gz",
        "hardware_timing.jsonl",
        "hardware_timing.jsonl.gz",
        "hardware_timing_interstitial.jsonl",
        "run_manifest.json",
    }
)

MACHINE_PATH_PATTERN = re.compile(
    rb"(?i)(?:[a-z]:[\\/](?:users|documents[ ]and[ ]settings)[\\/]"
    rb"[^\\/\x00-\x20\"']+|/(?:home|users)/[^/\x00-\x20\"']+)"
)

ARCHIVE_PREFIXES = (
    b"PK\x03\x04",
    b"PK\x05\x06",
    b"PK\x07\x08",
    b"7z\xbc\xaf\x27\x1c",
    b"Rar!\x1a\x07",
    b"\x1f\x8b",
    b"\xfd7zXZ\x00",
    b"BZh",
)

EXECUTABLE_PREFIXES = (
    b"MZ",
    b"\x7fELF",
    b"\xfe\xed\xfa\xce",
    b"\xfe\xed\xfa\xcf",
    b"\xce\xfa\xed\xfe",
    b"\xcf\xfa\xed\xfe",
)


def path_violations(path: str) -> list[str]:
    normalized = PurePosixPath(path).as_posix()
    if normalized in {"", "."}:
        return []
    lowered = normalized.lower()
    basename = PurePosixPath(normalized).name
    lowered_basename = basename.lower()
    violations = []

    if normalized not in EXACT_LICENSE_PATHS and _looks_like_license_path(normalized):
        violations.append("license or notice outside exact exceptions")

    for component in PurePosixPath(normalized).parts[:-1]:
        if component.lower() in FORBIDDEN_COMPONENTS:
            violations.append(f"forbidden component {component}")
            break

    if lowered_basename in RAW_TRACE_NAMES:
        violations.append(f"raw trace payload {basename}")

    if lowered_basename.endswith("_output.txt") or lowered_basename == "output.txt":
        violations.append("uncurated output text")

    for suffix in FORBIDDEN_SUFFIXES:
        if lowered.endswith(suffix):
            violations.append(f"forbidden suffix {suffix}")
            break

    return violations


def blob_content_violations(
    size: int,
    prefix: bytes,
    contains_machine_path: bool,
) -> list[str]:
    if size > MAX_BLOB_BYTES:
        return [f"blob exceeds {MAX_BLOB_BYTES} bytes"]

    violations = []
    if prefix.startswith(ARCHIVE_PREFIXES) or prefix[257:262] == b"ustar":
        violations.append("archive or BK2 magic")
    if prefix.startswith(EXECUTABLE_PREFIXES):
        violations.append("executable binary magic")
    if prefix[0x100:0x104] == b"SEGA":
        violations.append("Mega Drive ROM magic")
    if contains_machine_path:
        violations.append("machine-local absolute path")
    return violations


def license_content_violations(path: str, content: bytes) -> list[str]:
    expected_sha256 = EXACT_LICENSE_SHA256.get(path)
    if expected_sha256 is None:
        return []
    if hashlib.sha256(content).hexdigest() != expected_sha256:
        return ["unapproved license or notice content"]
    return []


def display_path(path: bytes | None) -> str:
    if path is None:
        return "<unknown>"
    rendered = []
    for value in path:
        if value == 0x5C:
            rendered.append("\\\\")
        elif value == 0x0A:
            rendered.append("\\n")
        elif value == 0x0D:
            rendered.append("\\r")
        elif value == 0x09:
            rendered.append("\\t")
        elif 0x20 <= value <= 0x7E:
            rendered.append(chr(value))
        else:
            rendered.append(f"\\x{value:02x}")
    return "".join(rendered)


def _looks_like_license_path(path: str) -> bool:
    parts = PurePosixPath(path).parts
    basename = parts[-1].lower()
    return (
        basename.startswith("license")
        or basename.startswith("copying")
        or basename.startswith("notice")
        or any(component.lower() in {"licenses", "notices"} for component in parts[:-1])
    )
