from dataclasses import dataclass
import hashlib
import json
from pathlib import PurePosixPath
import re
import zlib
from typing import Mapping


MAX_BLOB_BYTES = 1024 * 1024
MAX_V5_CONTRACT_BYTES = 64 * 1024
V5_CONTRACT_ROOT = "contracts/v5"
V5_CONTRACT_MANIFEST = f"{V5_CONTRACT_ROOT}/manifest.json"
V5_CONTRACT_MANIFEST_FORMAT = "tracechaser-v5-artifact-manifest-v1"
V5_CONTRACT_FILE_SUFFIXES = (
    ".csv.gz",
    ".jsonl.gz",
    ".json.gz",
    ".csv",
    ".jsonl",
    ".json",
    ".md",
)

EXACT_CONTRACT_PATHS = frozenset(
    {
        "contracts/audio/normalization-contract-v1.json",
        "contracts/audio/override-resume-first-divergence-attestation-v1.schema.json",
        "contracts/audio/override-resume-first-divergence-metadata-v1.schema.json",
        "contracts/audio/override-resume-first-divergence-reference-v1.schema.json",
    }
)

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


@dataclass(frozen=True)
class BlobSnapshot:
    size: int
    content: bytes | None
    mode: str = "100644"


@dataclass(frozen=True)
class ContractPackAudit:
    allowed_paths: frozenset[str]
    violations: tuple[tuple[str, str], ...]


def path_violations(path: str, curated_contract_member: bool = False) -> list[str]:
    normalized = PurePosixPath(path).as_posix()
    if normalized in {"", "."}:
        return []
    lowered = normalized.lower()
    basename = PurePosixPath(normalized).name
    lowered_basename = basename.lower()
    violations = []

    if normalized.startswith("contracts/"):
        if normalized in EXACT_CONTRACT_PATHS:
            pass
        elif curated_contract_member:
            pass
        elif normalized.startswith(f"{V5_CONTRACT_ROOT}/"):
            violations.append("unmanifested v5 contract file")
        else:
            violations.append("contract file outside curated exceptions")

    if normalized not in EXACT_LICENSE_PATHS and _looks_like_license_path(normalized):
        violations.append("license or notice outside exact exceptions")

    for component in PurePosixPath(normalized).parts[:-1]:
        if component.lower() in FORBIDDEN_COMPONENTS:
            violations.append(f"forbidden component {component}")
            break

    if lowered_basename in RAW_TRACE_NAMES and not curated_contract_member:
        violations.append(f"raw trace payload {basename}")

    if lowered_basename.endswith("_output.txt") or lowered_basename == "output.txt":
        violations.append("uncurated output text")

    for suffix in FORBIDDEN_SUFFIXES:
        if lowered.endswith(suffix):
            if curated_contract_member and suffix == ".gz":
                continue
            violations.append(f"forbidden suffix {suffix}")
            break

    return violations


def blob_content_violations(
    size: int,
    prefix: bytes,
    contains_machine_path: bool,
    curated_gzip_member: bool = False,
) -> list[str]:
    if size > MAX_BLOB_BYTES:
        return [f"blob exceeds {MAX_BLOB_BYTES} bytes"]

    violations = []
    has_archive_magic = prefix.startswith(ARCHIVE_PREFIXES) or prefix[257:262] == b"ustar"
    is_gzip = prefix.startswith(b"\x1f\x8b")
    if has_archive_magic and not (curated_gzip_member and is_gzip):
        violations.append("archive or BK2 magic")
    if prefix.startswith(EXECUTABLE_PREFIXES):
        violations.append("executable binary magic")
    if prefix[0x100:0x104] == b"SEGA":
        violations.append("Mega Drive ROM magic")
    if contains_machine_path:
        violations.append("machine-local absolute path")
    return violations


def audit_contract_pack(files: Mapping[str, BlobSnapshot]) -> ContractPackAudit:
    contract_paths = sorted(path for path in files if path.startswith("contracts/"))
    v5_paths = [path for path in contract_paths if path.startswith(f"{V5_CONTRACT_ROOT}/")]
    if not v5_paths:
        return ContractPackAudit(frozenset(EXACT_CONTRACT_PATHS & files.keys()), ())

    allowed_paths = set(EXACT_CONTRACT_PATHS & files.keys())
    violations: list[tuple[str, str]] = []
    manifest_blob = files.get(V5_CONTRACT_MANIFEST)
    if manifest_blob is None:
        violations.extend((path, "v5 contract manifest is missing") for path in v5_paths)
        return ContractPackAudit(frozenset(allowed_paths), tuple(sorted(set(violations))))
    if not is_regular_git_mode(manifest_blob.mode):
        violations.append(
            (V5_CONTRACT_MANIFEST, "v5 contract entry is not a regular Git file")
        )
        return ContractPackAudit(frozenset(allowed_paths), tuple(violations))
    if manifest_blob.size > MAX_V5_CONTRACT_BYTES or manifest_blob.content is None:
        violations.append((V5_CONTRACT_MANIFEST, "v5 contract manifest exceeds 65536 bytes"))
        return ContractPackAudit(frozenset(allowed_paths), tuple(violations))

    try:
        manifest = json.loads(manifest_blob.content)
    except (UnicodeDecodeError, json.JSONDecodeError):
        violations.append((V5_CONTRACT_MANIFEST, "v5 contract manifest is not valid UTF-8 JSON"))
        return ContractPackAudit(frozenset(allowed_paths), tuple(violations))
    if (
        not isinstance(manifest, dict)
        or manifest.get("format") != V5_CONTRACT_MANIFEST_FORMAT
        or not isinstance(manifest.get("files"), list)
    ):
        violations.append((V5_CONTRACT_MANIFEST, "v5 contract manifest shape is invalid"))
        return ContractPackAudit(frozenset(allowed_paths), tuple(violations))

    allowed_paths.add(V5_CONTRACT_MANIFEST)
    entries = manifest["files"]
    listed_paths = [entry.get("path") for entry in entries if isinstance(entry, dict)]
    if (
        len(listed_paths) != len(entries)
        or any(not isinstance(path, str) for path in listed_paths)
        or listed_paths != sorted(listed_paths)
        or len(listed_paths) != len(set(listed_paths))
    ):
        violations.append(
            (V5_CONTRACT_MANIFEST, "v5 contract manifest paths must be unique and sorted")
        )
        listed_paths = []

    valid_full_paths = set()
    for entry in entries:
        if not isinstance(entry, dict):
            continue
        relative_path = entry.get("path")
        if not _safe_contract_relative_path(relative_path):
            violations.append(
                (V5_CONTRACT_MANIFEST, "v5 contract manifest path escapes pack boundary")
            )
            continue
        full_path = f"{V5_CONTRACT_ROOT}/{relative_path}"
        valid_full_paths.add(full_path)
        blob = files.get(full_path)
        if blob is None:
            violations.append((V5_CONTRACT_MANIFEST, f"listed v5 contract file is missing: {relative_path}"))
            continue
        reasons = _contract_entry_violations(entry, blob, relative_path)
        violations.extend((full_path, reason) for reason in reasons)
        if not reasons:
            allowed_paths.add(full_path)

    for path in v5_paths:
        if path != V5_CONTRACT_MANIFEST and path not in valid_full_paths:
            violations.append((path, "unmanifested v5 contract file"))

    return ContractPackAudit(
        frozenset(allowed_paths),
        tuple(sorted(set(violations))),
    )


def _safe_contract_relative_path(path: object) -> bool:
    if not isinstance(path, str) or not path or "\\" in path:
        return False
    candidate = PurePosixPath(path)
    return (
        not candidate.is_absolute()
        and path == candidate.as_posix()
        and all(component not in {"", ".", ".."} for component in candidate.parts)
        and not path.startswith("../")
    )


def _contract_entry_violations(
    entry: dict[str, object],
    blob: BlobSnapshot,
    relative_path: str,
) -> list[str]:
    violations = []
    if (
        relative_path != relative_path.lower()
        or not relative_path.endswith(V5_CONTRACT_FILE_SUFFIXES)
    ):
        violations.append("v5 contract file type is not admissible")
    if not is_regular_git_mode(blob.mode):
        violations.append("v5 contract entry is not a regular Git file")
    if blob.size > MAX_V5_CONTRACT_BYTES or blob.content is None:
        return [*violations, "v5 contract file exceeds 65536 bytes"]
    if entry.get("stored_size") != blob.size:
        violations.append("v5 contract stored size mismatch")
    if not _valid_sha256(entry.get("stored_sha256")) or (
        hashlib.sha256(blob.content).hexdigest() != entry.get("stored_sha256")
    ):
        violations.append("v5 contract stored SHA-256 mismatch")

    if relative_path.endswith(".gz"):
        required_identity_fields = {
            "stored_size",
            "stored_sha256",
            "logical_size",
            "logical_sha256",
        }
        if not required_identity_fields.issubset(entry):
            violations.append(
                "v5 contract gzip stored and logical identity fields are required"
            )
            return violations
        logical, gzip_reason = _decode_deterministic_gzip(blob.content)
        if gzip_reason is not None:
            violations.append(gzip_reason)
            return violations
        assert logical is not None
        violations.extend(
            blob_content_violations(
                len(logical),
                logical[:512],
                bool(MACHINE_PATH_PATTERN.search(logical)),
            )
        )
        if entry.get("logical_size") != len(logical):
            violations.append("v5 contract logical size mismatch")
        if not _valid_sha256(entry.get("logical_sha256")) or (
            hashlib.sha256(logical).hexdigest() != entry.get("logical_sha256")
        ):
            violations.append("v5 contract logical SHA-256 mismatch")
    return violations


def _decode_deterministic_gzip(content: bytes) -> tuple[bytes | None, str | None]:
    offset = 0
    logical = bytearray()
    while offset < len(content):
        if content[offset : offset + 2] != b"\x1f\x8b":
            reason = (
                "v5 contract gzip stream has trailing data"
                if offset
                else "v5 contract gzip member header is malformed"
            )
            return None, reason
        if len(content) - offset < 10 or content[offset + 2] != 8:
            return None, "v5 contract gzip member header is malformed"
        if content[offset + 3] != 0 or content[offset + 4 : offset + 8] != b"\x00" * 4:
            return None, "v5 contract gzip member header is not deterministic"

        compressed_start = offset + 10
        inflater = zlib.decompressobj(wbits=-zlib.MAX_WBITS)
        remaining = MAX_V5_CONTRACT_BYTES - len(logical)
        try:
            member = inflater.decompress(content[compressed_start:], remaining + 1)
        except zlib.error:
            return None, "v5 contract gzip payload is malformed"
        if len(member) > remaining:
            return None, "v5 contract logical file exceeds 65536 bytes"
        if not inflater.eof:
            return None, "v5 contract gzip payload is malformed"

        compressed_size = len(content[compressed_start:]) - len(inflater.unused_data)
        trailer_start = compressed_start + compressed_size
        if len(content) - trailer_start < 8:
            return None, "v5 contract gzip payload is malformed"
        expected_crc = int.from_bytes(content[trailer_start : trailer_start + 4], "little")
        expected_size = int.from_bytes(
            content[trailer_start + 4 : trailer_start + 8], "little"
        )
        if (
            zlib.crc32(member) != expected_crc
            or (len(member) & 0xFFFFFFFF) != expected_size
        ):
            return None, "v5 contract gzip payload is malformed"

        logical.extend(member)
        offset = trailer_start + 8

    if offset == 0:
        return None, "v5 contract gzip member header is malformed"
    return bytes(logical), None


def is_regular_git_mode(mode: str) -> bool:
    return mode in {"100644", "100755"}


def _valid_sha256(value: object) -> bool:
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


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
