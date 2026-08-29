#!/usr/bin/env python3
import argparse
import posixpath
import re
from pathlib import Path, PurePosixPath
import subprocess
import sys


AGENT_LINK_PATTERN = re.compile(r"\[[^]]+\]\(([^)\s]+)(?:\s+[^)]*)?\)")
HISTORICAL_BOUNDARY_PATTERN = re.compile(
    r"^(#{1,6})\s+Pre-v5 historical(?: evidence| capture notes)?(?:\s*[:—-].*)?\s*$",
    re.IGNORECASE,
)
HEADING_PATTERN = re.compile(r"^(#{1,6})\s+")
FENCE_PATTERN = re.compile(r"^[ \t]*(`{3,}|~{3,})")
OBSOLETE_IGNORE_PATTERN = re.compile(
    r"\.gitignore.{0,160}(?:makes?|making).{0,80}new files.{0,80}invisible",
    re.IGNORECASE | re.DOTALL,
)
FORMER_ROOT_COMMAND_PATTERN = re.compile(
    r"(?:tools/(?:bizhawk(?:-headless)?|traces)(?:/|\b)|"
    r"docs/BizHawk-2\.11-(?:linux|win)-x64(?:/|\b)|"
    r"(?<!/)src/test/resources/(?:traces|audio)(?:/|\b)|"
    r"\$PWD/target|%CD%/target|trace_output/|bizhawk-headless/\.scratch/)",
    re.IGNORECASE,
)
def find_violations(root: Path) -> list[str]:
    root = root.resolve()
    markdown = _tracked_markdown(root)
    tracked_paths = set(markdown)
    violations = []
    for path, content in markdown.items():
        active_content, _ = _active_markdown(content)
        for match in AGENT_LINK_PATTERN.finditer(active_content):
            target = match.group(1).strip("<>").split("#", 1)[0]
            if PurePosixPath(target).name not in {"AGENTS.md", "CLAUDE.md"}:
                continue
            resolved = posixpath.normpath(
                posixpath.join(PurePosixPath(path).parent.as_posix(), target)
            )
            if resolved not in tracked_paths:
                violations.append(
                    f"path={path} reason=dangling active agent-doc link {target}"
                )
        if OBSOLETE_IGNORE_PATTERN.search(active_content):
            violations.append(f"path={path} reason=obsolete broad-ignore guidance")
        normalized_content = active_content.replace("\\", "/")
        if FORMER_ROOT_COMMAND_PATTERN.search(normalized_content):
            violations.append(f"path={path} reason=active former-root command")
    return sorted(set(violations))


def _active_content(content: str) -> str:
    return _active_markdown(content)[0]


def _active_fenced_code_blocks(content: str) -> list[str]:
    return _active_markdown(content)[1]


def _active_markdown(content: str) -> tuple[str, list[str]]:
    """Scan headings and fences once, preserving nested historical scope."""
    active_lines: list[str] = []
    blocks: list[str] = []
    historical_levels: list[int] = []
    fence_marker: str | None = None
    fence_active = False
    current: list[str] = []
    for line in content.splitlines(keepends=True):
        fence = FENCE_PATTERN.match(line)
        if fence_marker is not None:
            if fence and fence.group(1)[0] == fence_marker[0] \
                    and len(fence.group(1)) >= len(fence_marker):
                if fence_active:
                    blocks.append("".join(current))
                    active_lines.append(line)
                current = []
                fence_marker = None
                fence_active = False
            else:
                current.append(line)
                if fence_active:
                    active_lines.append(line)
            continue
        if fence:
            fence_marker = fence.group(1)
            fence_active = not historical_levels
            if fence_active:
                active_lines.append(line)
            continue
        stripped = line.rstrip("\r\n")
        heading = HEADING_PATTERN.match(stripped)
        if heading:
            level = len(heading.group(1))
            while historical_levels and historical_levels[-1] >= level:
                historical_levels.pop()
            historical = HISTORICAL_BOUNDARY_PATTERN.match(stripped)
            if historical:
                historical_levels.append(level)
                continue
        if not historical_levels:
            active_lines.append(line)
    return "".join(active_lines), blocks


def _tracked_markdown(root: Path) -> dict[str, str]:
    result = subprocess.run(
        ["git", "-C", str(root), "ls-files", "-z", "--", "*.md"],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=True,
    )
    markdown = {}
    for raw_path in result.stdout.split(b"\x00"):
        if not raw_path:
            continue
        path = raw_path.decode("utf-8", "surrogateescape")
        content = subprocess.run(
            ["git", "-C", str(root), "show", f":{path}"],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=True,
        ).stdout
        markdown[path] = content.decode("utf-8", "replace")
    return markdown


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Audit active agent guidance links")
    parser.add_argument("--root", type=Path, default=Path.cwd())
    options = parser.parse_args(arguments)

    violations = find_violations(options.root)
    if violations:
        for violation in violations:
            print(violation)
        return 1
    print("documentation policy: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
