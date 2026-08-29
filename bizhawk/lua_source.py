"""Lua source scanning shared by runtime and source-only contracts."""

from __future__ import annotations

import re
from collections.abc import Iterable
from pathlib import Path


LUA_API_REFERENCE_PATTERN = re.compile(
    r"\b(client|emu|event|mainmemory|memory|movie|joypad)\."
    r"([A-Za-z_][A-Za-z0-9_]*)\b"
)
NON_API_EVENT_FIELDS = frozenset(
    {
        "event.begin_pc",
        "event.completion_pc",
        "event.kind",
        "event.raw_chip_events",
        "event.source_cpu",
    }
)
LUA_IDENTIFIER = r"[A-Za-z_][A-Za-z0-9_]*"


def _long_bracket_close(source: str, start: int) -> str | None:
    if start >= len(source) or source[start] != "[":
        return None
    cursor = start + 1
    while cursor < len(source) and source[cursor] == "=":
        cursor += 1
    if cursor >= len(source) or source[cursor] != "[":
        return None
    return "]" + "=" * (cursor - start - 1) + "]"


def strip_lua_comments_and_strings(source: str) -> str:
    """Blank Lua comments and string literals while preserving positions/newlines."""
    executable: list[str] = []
    line_comment = False
    long_close: str | None = None
    quote: str | None = None
    escaped = False
    index = 0
    while index < len(source):
        current = source[index]
        if line_comment:
            if current == "\n":
                line_comment = False
                executable.append("\n")
            else:
                executable.append(" ")
            index += 1
            continue
        if long_close is not None:
            if source.startswith(long_close, index):
                executable.append(" " * len(long_close))
                index += len(long_close)
                long_close = None
            else:
                executable.append("\n" if current == "\n" else " ")
                index += 1
            continue
        if quote is not None:
            executable.append("\n" if current == "\n" else " ")
            if escaped:
                escaped = False
            elif current == "\\":
                escaped = True
            elif current == quote:
                quote = None
            index += 1
            continue
        if source.startswith("--", index):
            close = _long_bracket_close(source, index + 2)
            if close is not None:
                opener_length = len(close) + 2
                executable.append(" " * opener_length)
                index += opener_length
                long_close = close
            else:
                executable.append("  ")
                index += 2
                line_comment = True
            continue
        close = _long_bracket_close(source, index)
        if close is not None:
            executable.append(" " * len(close))
            index += len(close)
            long_close = close
            continue
        if current in ("'", '"'):
            executable.append(" ")
            quote = current
            index += 1
            continue
        executable.append(current)
        index += 1
    return "".join(executable)


def has_lua_api_call(source: str, api: str) -> bool:
    """Recognize one exact namespace.method( token sequence in executable Lua."""
    api_match = re.fullmatch(rf"({LUA_IDENTIFIER})\.({LUA_IDENTIFIER})", api)
    if api_match is None:
        return False
    namespace, method = map(re.escape, api_match.groups())
    executable = strip_lua_comments_and_strings(source)
    return re.search(
        rf"(?<![A-Za-z0-9_]){namespace}\s*\.\s*{method}\s*\(", executable
    ) is not None


def collect_lua_api_references(
    recorder_paths: Iterable[Path], probe_root: Path
) -> set[str]:
    """Return executable BizHawk API references from recorders and all probes."""
    paths = (*recorder_paths, *sorted(probe_root.rglob("*.lua")))
    required: set[str] = set()
    for path in paths:
        if not path.is_file():
            continue
        executable = strip_lua_comments_and_strings(path.read_text(encoding="utf-8"))
        required.update(
            f"{namespace}.{method}"
            for namespace, method in LUA_API_REFERENCE_PATTERN.findall(executable)
        )
    required.difference_update(NON_API_EVENT_FIELDS)
    return required
