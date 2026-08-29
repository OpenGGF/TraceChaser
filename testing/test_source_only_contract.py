"""Repository-facing contract for standalone documentation and CI."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = ROOT / ".github" / "workflows" / "source-only.yml"
REQUIRED_DOCS = (
    "capture-s1.md",
    "capture-s2.md",
    "capture-s3k.md",
    "native-headless.md",
    "lua-probes.md",
    "validate-compare-publish.md",
    "scratch-and-security.md",
    "contributing.md",
    "releasing.md",
    "trace-v5.md",
)


class SourceOnlyWorkflowContractTests(unittest.TestCase):
    def test_all_standalone_guides_are_present_and_linked_from_readme(self) -> None:
        readme = (ROOT / "README.md").read_text(encoding="utf-8")
        for name in REQUIRED_DOCS:
            with self.subTest(document=name):
                self.assertTrue((ROOT / "docs" / name).is_file())
                self.assertIn(f"docs/{name}", readme)
        self.assertNotIn("forthcoming", readme.lower())

    def test_source_only_workflow_has_exact_offline_gates_and_skip_accounting(self) -> None:
        source = WORKFLOW.read_text(encoding="utf-8")
        required = (
            "runs-on: ubuntu-24.04",
            "actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683",
            "fetch-depth: 0",
            "python3 -m unittest discover -s testing -p 'test_*.py' -v",
            "python3 testing/repository_policy.py --root .",
            "python3 testing/history_audit.py --root .",
            "git ls-files -z -- '*.sh'",
            "bash -n",
            "LUA_BIN=lua5.4",
            "python3 traces/validate_v5_conformance.py contracts/v5",
            "python3 testing/documentation_policy.py --root .",
            "git diff --check",
            "rg_status",
            "monodis_status",
            "PowerShell is unavailable on PATH",
            "pwsh is unavailable",
        )
        for token in required:
            with self.subTest(token=token):
                self.assertIn(token, source)

        provision = source.index("Install pinned source-only toolchain")
        checkout = source.index("actions/checkout@")
        tests = source.index("python3 -m unittest discover")
        self.assertLess(provision, checkout)
        self.assertLess(checkout, tests)

    def test_native_integration_is_optional_and_preflights_verified_cache(self) -> None:
        source = WORKFLOW.read_text(encoding="utf-8")
        native = source[source.index("native-integration:") :]
        self.assertIn("workflow_dispatch", native)
        self.assertIn("self-hosted", native)
        self.assertIn("bizhawk/preflight_bizhawk_2_11.sh", native)
        self.assertIn("BIZHAWK_HOME", native)
        self.assertIn("bizhawk-headless/test.sh", native)
        source_only = source[source.index("source-only:") : source.index("native-integration:")]
        self.assertNotIn("needs: native-integration", source_only)
        self.assertNotIn("bizhawk-headless/test.sh", source_only)


if __name__ == "__main__":
    unittest.main()
