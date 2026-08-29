"""Synthetic monodis-backed BizHawk installation fixtures for launcher tests."""

from __future__ import annotations

import shlex
from pathlib import Path


def write_install(home: Path, lock: dict) -> None:
    for relative in lock["required_files"]:
        path = home / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"fixture")
    for capability in lock["lua_capabilities"]:
        if "example_path" in capability:
            (home / capability["example_path"]).write_text(
                f"{capability['example_marker']}(true)\n", encoding="utf-8"
            )


def write_monodis(executable: Path, lock: dict, version: str = "2.11.0.0") -> None:
    method_lines = ["Method Table"]
    attribute_lines = ["Custom Attributes Table"]
    current_class = None
    for method_id, capability in enumerate(lock["lua_capabilities"], start=1):
        library_class = capability["library_class"]
        if library_class != current_class:
            method_lines.append(f"########## {library_class}")
            current_class = library_class
        method_lines.append(
            f"{method_id}: instance default void {capability['managed_method']} ()  "
            f"(param: {method_id} impl_flags: cil managed )"
        )
        registered_name = capability["registered_name"]
        attribute_lines.append(
            f"{method_id}: MethodDef: {method_id}: instance void class "
            "BizHawk.Client.Common.LuaMethodAttribute::'.ctor'(string, string) "
            f'["{registered_name}\u0003cap", "cap"]'
        )
    method_path = executable.with_suffix(".methods")
    attribute_path = executable.with_suffix(".attributes")
    method_path.write_text("\n".join(method_lines) + "\n", encoding="utf-8")
    attribute_path.write_text("\n".join(attribute_lines) + "\n", encoding="utf-8")
    executable.write_text(
        "#!/bin/sh\n"
        "case \"$1\" in\n"
        f"  --assembly) printf 'Version: {version}\\n' ;;\n"
        f"  --method) cat {shlex.quote(str(method_path))} ;;\n"
        f"  --customattr) cat {shlex.quote(str(attribute_path))} ;;\n"
        "  *) exit 2 ;;\n"
        "esac\n",
        encoding="utf-8",
    )
    executable.chmod(0o755)
