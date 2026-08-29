#!/usr/bin/env python3
import argparse
import posixpath
import re
from pathlib import Path, PurePosixPath
import subprocess
import sys


AGENT_LINK_PATTERN = re.compile(r"\[[^]]+\]\(([^)\s]+)(?:\s+[^)]*)?\)")
HISTORICAL_BOUNDARY_PATTERN = re.compile(
    r"^(#{1,6})\s+Pre-v5 historical(?: evidence)?(?:\s*[:—-].*)?\s*$",
    re.IGNORECASE,
)
HEADING_PATTERN = re.compile(r"^(#{1,6})\s+")
OBSOLETE_IGNORE_PATTERN = re.compile(
    r"\.gitignore.{0,160}(?:makes?|making).{0,80}new files.{0,80}invisible",
    re.IGNORECASE | re.DOTALL,
)
FORMER_ROOT_COMMAND_PATTERN = re.compile(
    r"(?m)^[ \t]*(?:\$[ \t]+)?(?:python3?|pwsh|powershell|bash|sh|"
    r"tools/|docs/|src/test/|[A-Z][A-Z0-9_]*=)[^\n]*"
    r"(?:tools/(?:bizhawk(?:-headless)?|traces)/|"
    r"docs/BizHawk-2\.11-linux-x64|"
    r"(?<!OpenGGF/)src/test/resources/(?:traces|audio)/)"
)


def find_violations(root: Path) -> list[str]:
    root = root.resolve()
    markdown = _tracked_markdown(root)
    tracked_paths = set(markdown)
    violations = []
    for path, content in markdown.items():
        active_content = _active_content(content)
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
        for block in _active_fenced_code_blocks(content):
            if FORMER_ROOT_COMMAND_PATTERN.search(block):
                violations.append(f"path={path} reason=active former-root command")
    return sorted(set(violations))


def _active_content(content: str) -> str:
    active_lines = []
    historical_level: int | None = None
    for line in content.splitlines(keepends=True):
        stripped = line.rstrip("\r\n")
        historical = HISTORICAL_BOUNDARY_PATTERN.match(stripped)
        if historical:
            historical_level = len(historical.group(1))
            continue
        heading = HEADING_PATTERN.match(stripped)
        if historical_level is not None:
            if heading and len(heading.group(1)) <= historical_level:
                historical_level = None
            else:
                continue
        active_lines.append(line)
    return "".join(active_lines)


def _fenced_code_blocks(content: str) -> list[str]:
    blocks: list[str] = []
    current: list[str] | None = None
    for line in content.splitlines(keepends=True):
        if line.lstrip().startswith("```"):
            if current is None:
                current = []
            else:
                blocks.append("".join(current))
                current = None
        elif current is not None:
            current.append(line)
    return blocks


def _active_fenced_code_blocks(content: str) -> list[str]:
    """Extract active fences while retaining fence state through history."""
    blocks: list[str] = []
    current: list[str] | None = None
    historical_level: int | None = None
    active_fence = False
    for line in content.splitlines(keepends=True):
        if line.lstrip().startswith("```"):
            if current is None:
                active_fence = historical_level is None
                current = []
            else:
                if active_fence:
                    blocks.append("".join(current))
                current = None
                active_fence = False
            continue
        if current is not None:
            current.append(line)
            continue
        stripped = line.rstrip("\r\n")
        historical = HISTORICAL_BOUNDARY_PATTERN.match(stripped)
        if historical:
            historical_level = len(historical.group(1))
            continue
        heading = HEADING_PATTERN.match(stripped)
        if historical_level is not None and heading and len(heading.group(1)) <= historical_level:
            historical_level = None
    return blocks


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
