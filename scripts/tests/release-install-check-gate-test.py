#!/usr/bin/env python3
"""Unit tests for scripts/ci/release-install-check-gate.py."""
import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "release_install_check_gate",
    ROOT / "scripts" / "ci" / "release-install-check-gate.py",
)
gate = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(gate)

TITLE = gate.COMMIT_ALL_TITLE


def make_run(run_id, sha, status="completed", conclusion="success", title=TITLE, created_at="2026-11-28T01:23:00Z"):
    return {
        "id": run_id,
        "head_sha": sha,
        "status": status,
        "conclusion": conclusion,
        "display_title": title,
        "created_at": created_at,
        "html_url": f"https://github.com/rzvdaniel/automaton/actions/runs/{run_id}",
    }


class ReleaseInstallCheckGateTests(unittest.TestCase):
    def test_exact_commit_passing_passes(self):
        ok, msg = gate.evaluate_gate(
            "aaa11112222",
            ["aaa11112222", "00000001111"],
            [make_run(10, "aaa11112222")],
            lambda _a, _b: [],
        )
        self.assertTrue(ok)
        self.assertIn("aaa11112222", msg)

    def test_latest_run_on_commit_failed_refuses_even_if_older_passed(self):
        runs = [
            make_run(10, "aaa11112222", conclusion="success", created_at="2026-11-28T01:00:00Z"),
            make_run(11, "aaa11112222", conclusion="failure", created_at="2026-11-28T02:00:00Z"),
        ]
        ok, msg = gate.evaluate_gate("aaa11112222", ["aaa11112222"], runs, lambda _a, _b: [])
        self.assertFalse(ok)
        self.assertIn("conclusion: failure", msg)

    def test_in_progress_run_on_commit_refuses(self):
        runs = [make_run(12, "aaa11112222", status="in_progress", conclusion="")]
        ok, msg = gate.evaluate_gate("aaa11112222", ["aaa11112222"], runs, lambda _a, _b: [])
        self.assertFalse(ok)
        self.assertIn("pending", msg)

    def test_non_commit_all_runs_are_ignored(self):
        runs = [
            make_run(20, "aaa11112222", title="Release install check (commit restore)"),
            make_run(21, "aaa11112222", title="Release install check (workflow_dispatch)"),
            make_run(22, "aaa11112222", title="Release install check (schedule)"),
            make_run(23, "aaa11112222", title="Release install check (workflow_run)"),
        ]
        ok, msg = gate.evaluate_gate("aaa11112222", ["aaa11112222"], runs, lambda _a, _b: [])
        self.assertFalse(ok)
        self.assertIn("No commit install check", msg)

    def test_ancestor_passed_with_docs_and_test_only_diff_passes(self):
        runs = [make_run(30, "anc11112222")]
        diffs = {
            ("anc11112222", "head2223333"): [
                "docs/maintainers/release-install-check.md",
                "src/automaton-vue/docs/install.md",
                "src/UnitTests/Automaton.Api.Tests/SomeTests.cs",
                ".agents/skills/pr-review/SKILL.md",
                "docker/README.md",
                "README.md",
            ]
        }
        ok, msg = gate.evaluate_gate(
            "head2223333",
            ["head2223333", "anc11112222"],
            runs,
            lambda a, b: diffs[(a, b)],
        )
        self.assertTrue(ok)
        self.assertIn("no installer/server paths changed since", msg)

    def test_ancestor_passed_with_server_or_installer_diff_refuses(self):
        runs = [make_run(31, "anc11112222")]
        for changed_path in [
            "src/Automaton.Api/Controllers/AppsController.cs",
            "src/automaton-studio-vue/src/App.vue",
            "src/automaton-store-vue/src/main.ts",
            "docker/install/restore.sh",
            "docker/Dockerfile.server",
            "scripts/install-automaton.sh",
            "scripts/update-automaton.sh",
            "scripts/ci/release-install-check.sh",
            "shared/ai-guidance/rules.md",
            "system-apps/automaton-infrastructure/app.json",
            ".github/workflows/server-release.yml",
        ]:
            with self.subTest(path=changed_path):
                ok, msg = gate.evaluate_gate(
                    "head2223333",
                    ["head2223333", "anc11112222"],
                    runs,
                    lambda _a, _b, p=changed_path: [p],
                )
                self.assertFalse(ok)
                self.assertIn(changed_path, msg)

    def test_latest_checked_ancestor_failed_blocks_even_if_older_passed(self):
        runs = [
            make_run(40, "older111111", conclusion="success"),
            make_run(41, "newer222222", conclusion="failure"),
        ]
        ok, msg = gate.evaluate_gate(
            "head333333",
            ["head333333", "newer222222", "older111111"],
            runs,
            lambda _a, _b: ["docs/readme.md"],
        )
        self.assertFalse(ok)
        self.assertIn("newer222222", msg)

    def test_skip_install_check_reason_and_booleans(self):
        for bad in ("true", "TRUE", "false", "yes", "no", "1", "0"):
            with self.subTest(bad=bad):
                ok, msg = gate.evaluate_gate("aaa", ["aaa"], [], lambda _a, _b: [], skip_reason=bad)
                self.assertFalse(ok)
                self.assertIn("emergency reason", msg)

        ok, msg = gate.evaluate_gate(
            "aaa", ["aaa"], [], lambda _a, _b: [], skip_reason="emergency hotfix for #1884"
        )
        self.assertTrue(ok)
        self.assertIn("emergency hotfix for #1884", msg)

    def test_git_helpers_on_real_repo(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            git = lambda *args: subprocess.check_output(["git", "-C", str(repo), *args], text=True).strip()
            git("init", "-q")
            git("config", "user.email", "ci@example.com")
            git("config", "user.name", "CI")
            (repo / "docs").mkdir()
            (repo / "docs" / "a.md").write_text("one\n")
            git("add", ".")
            git("commit", "-q", "-m", "first")
            first = git("rev-parse", "HEAD")
            (repo / "docs" / "a.md").write_text("two\n")
            git("add", ".")
            git("commit", "-q", "-m", "second")
            second = git("rev-parse", "HEAD")

            orig_cwd = Path.cwd()
            try:
                import os
                os.chdir(repo)
                self.assertEqual(gate.git_ancestors(second), [second, first])
                self.assertEqual(gate.git_diff_names(first, second), ["docs/a.md"])
            finally:
                os.chdir(orig_cwd)


if __name__ == "__main__":
    unittest.main()
