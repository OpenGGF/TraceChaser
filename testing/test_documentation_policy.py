import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SCANNER = REPOSITORY_ROOT / "testing" / "documentation_policy.py"


class DocumentationPolicyIntegrationTest(unittest.TestCase):
    def setUp(self):
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.repository = Path(self._temporary_directory.name)
        self._git("init", "-q", "-b", "main")
        self._write("AGENTS.md", "root guidance\n")
        self._write("CLAUDE.md", "root guidance\n")

    def tearDown(self):
        self._temporary_directory.cleanup()

    def test_rejects_dangling_active_agent_links_and_obsolete_ignore_claims(self):
        self._write(
            "component/README.md",
            "# Component\n\n"
            "Read [the guidance](CLAUDE.md) before editing. The `.gitignore` "
            "rule makes new files here invisible to `git status`.\n",
        )
        self._git("add", ".")

        result = self._audit()

        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        self.assertIn(
            "path=component/README.md reason=dangling active agent-doc link CLAUDE.md",
            result.stdout,
        )
        self.assertIn(
            "path=component/README.md reason=obsolete broad-ignore guidance",
            result.stdout,
        )

    def test_ignores_links_and_old_claims_below_explicit_historical_boundary(self):
        self._write(
            "component/docs/spec.md",
            "# Spec\n\n"
            "Read [root guidance](../../AGENTS.md).\n\n"
            "## Pre-v5 historical evidence\n\n"
            "The old [nested guidance](../AGENTS.md) and `.gitignore` rule made "
            "new files here invisible to `git status`.\n",
        )
        self._git("add", ".")

        result = self._audit()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual("documentation policy: PASS\n", result.stdout)

    def test_tracechaser_active_guidance_chain_is_valid(self):
        result = subprocess.run(
            [sys.executable, str(SCANNER), "--root", str(REPOSITORY_ROOT)],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual("documentation policy: PASS\n", result.stdout)

    def _audit(self):
        return subprocess.run(
            [sys.executable, str(SCANNER), "--root", str(self.repository)],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )

    def _write(self, relative_path, content):
        path = self.repository / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content)

    def _git(self, *arguments):
        return subprocess.run(
            ["git", "-C", str(self.repository), *arguments],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=True,
        )


if __name__ == "__main__":
    unittest.main()
