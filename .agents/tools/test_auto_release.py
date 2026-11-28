import importlib.util
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path

spec = importlib.util.spec_from_file_location("auto_release", Path(__file__).with_name("auto-release.py"))
ar = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ar)

NOW = datetime(2026, 11, 28, 14, 0, 0, tzinfo=timezone.utc)
REPO = "rzvdaniel/automaton"
MAIN_SHA = "aaaa111122223333444455556666777788889999"


class FakeClient:
    def __init__(self):
        self.dispatched = []
        self.active_runs = []
        self.releases = [
            {
                "tag_name": "server-v0.2.200",
                "draft": False,
                "prerelease": False,
                "published_at": "2026-11-27T08:00:00Z",
            }
        ]
        self.tags = [{"ref": "refs/tags/server-v0.2.200"}]
        self.items = [
            {"status": "Verify", "content": {"type": "Issue", "repository": REPO, "number": 1884, "title": "Security headers"}},
            {"status": "Verify", "content": {"type": "Issue", "repository": "rzvdaniel/automaton-apps", "number": 526, "title": "App issue"}},
        ]
        self.ahead_by = 1
        self.ahead_commits = [{"sha": MAIN_SHA, "commit": {"message": "Fix headers (#1884) (#1885)"}}]
        self.closed_prs = [
            {
                "number": 1885,
                "title": "Fix headers (#1884)",
                "body": "Refs #1884.",
                "merged_at": "2026-11-28T05:00:00Z",
                "merge_commit_sha": MAIN_SHA,
            }
        ]
        self.main_runs = [
            {
                "id": 100,
                "name": "Tests",
                "path": ".github/workflows/tests.yml",
                "event": "push",
                "head_sha": MAIN_SHA,
                "status": "completed",
                "conclusion": "success",
                "html_url": "https://github.com/o/r/actions/runs/100",
            },
            {
                "id": 101,
                "name": "Agent issue",
                "path": ".github/workflows/agent-issue.yml",
                "event": "issues",
                "head_sha": MAIN_SHA,
                "status": "completed",
                "conclusion": "failure",
                "html_url": "https://github.com/o/r/actions/runs/101",
            },
        ]
        self.install_runs = [
            {
                "id": 200,
                "head_sha": MAIN_SHA,
                "status": "completed",
                "conclusion": "success",
                "html_url": "https://github.com/o/r/actions/runs/200",
            }
        ]

    def api(self, path: str):
        if path.startswith(f"repos/{REPO}/actions/runs?status="):
            status = path.split("status=")[1].split("&")[0]
            return {"workflow_runs": [r for r in self.active_runs if r["status"] == status]}
        if path.startswith(f"repos/{REPO}/releases"):
            return self.releases
        if path.startswith(f"repos/{REPO}/git/matching-refs/tags/"):
            return self.tags
        if path == f"repos/{REPO}/commits/main":
            return {"sha": MAIN_SHA}
        if path.startswith(f"repos/{REPO}/compare/"):
            return {"ahead_by": self.ahead_by, "commits": self.ahead_commits}
        if path.startswith(f"repos/{REPO}/pulls?"):
            return self.closed_prs
        if path.startswith(f"repos/{REPO}/actions/runs?branch=main"):
            return {"workflow_runs": self.main_runs}
        if path.startswith(f"repos/{REPO}/actions/workflows/release-install-check.yml/runs"):
            return {"workflow_runs": self.install_runs}
        raise AssertionError(f"Unexpected api call: {path}")

    def project_items(self, owner: str, number: str):
        return self.items

    def dispatch_release(self, repo: str, version: str):
        self.dispatched.append((repo, version))


class AutoReleaseTests(unittest.TestCase):
    def run_eval(self, client: FakeClient, max_per_day: int = 2, dry_run: bool = False, **kw):
        return ar.evaluate(
            client,
            repo=REPO,
            board="rzvdaniel/17",
            max_per_day=max_per_day,
            dry_run=dry_run,
            now_utc=NOW,
            check_local=kw.pop("check_local", False),
            **kw,
        )

    def test_dispatches_next_patch_when_ready(self):
        c = FakeClient()
        res = self.run_eval(c)
        self.assertEqual(res["action"], "released")
        self.assertEqual(res["version"], "0.2.201")
        self.assertEqual(c.dispatched, [(REPO, "0.2.201")])
        self.assertEqual(res["waiting"], [{"issue": 1884, "title": "Security headers", "pr": 1885}])
        # Ignores failed Agent issue run on main SHA
        self.assertIn("Dispatched `server-v0.2.201`", ar.format_summary(res))

    def test_dry_run_does_not_dispatch(self):
        c = FakeClient()
        res = self.run_eval(c, dry_run=True)
        self.assertEqual(res["action"], "dry-run")
        self.assertEqual(res["version"], "0.2.201")
        self.assertEqual(c.dispatched, [])

    def test_skips_unused_existing_tag_collision(self):
        c = FakeClient()
        c.tags.append({"ref": "refs/tags/server-v0.2.201"})
        res = self.run_eval(c)
        self.assertEqual(res["version"], "0.2.202")

    def test_skips_when_release_verify_or_upgrade_in_progress(self):
        for wf, label in (
            (".github/workflows/server-release.yml", "release"),
            (".github/workflows/verify-release.yml", "Verify run"),
            (".github/workflows/test-server-upgrade.yml", "test-server upgrade"),
        ):
            c = FakeClient()
            c.active_runs = [{"id": 9, "path": wf, "name": "x", "status": "in_progress", "html_url": "https://run/9"}]
            res = self.run_eval(c)
            self.assertEqual(res["action"], "skip")
            self.assertIn(label, res["reason"])
            self.assertEqual(c.dispatched, [])

    def test_respects_cadence_limit(self):
        c = FakeClient()
        c.releases = [
            {"tag_name": "server-v0.2.200", "draft": False, "prerelease": False, "published_at": "2026-11-28T06:00:00Z"},
            {"tag_name": "server-v0.2.199", "draft": False, "prerelease": False, "published_at": "2026-11-28T01:00:00Z"},
        ]
        res = self.run_eval(c, max_per_day=2)
        self.assertEqual(res["action"], "skip")
        self.assertIn("cadence reached (2/2", res["reason"])

        res_zero = self.run_eval(c, max_per_day=0)
        self.assertEqual(res_zero["action"], "skip")
        self.assertIn("cadence is 0", res_zero["reason"])

    def test_skips_when_no_verify_cards_have_unreleased_merged_prs(self):
        c = FakeClient()
        c.closed_prs[0]["merge_commit_sha"] = "old0000"
        c.ahead_commits = [{"sha": MAIN_SHA, "commit": {"message": "Docs tweak (#9999)"}}]
        res = self.run_eval(c)
        self.assertEqual(res["action"], "skip")
        self.assertIn("no Verify cards have merged PRs outside", res["reason"])

    def test_skips_when_main_is_red_or_running(self):
        c = FakeClient()
        c.main_runs[0]["conclusion"] = "failure"
        res = self.run_eval(c)
        self.assertEqual(res["action"], "skip")
        self.assertIn("main is red: Tests failure", res["reason"])

        c2 = FakeClient()
        c2.main_runs.append(
            {
                "id": 102,
                "name": "Build production images",
                "path": ".github/workflows/build.yml",
                "event": "push",
                "head_sha": "older_sha",
                "status": "completed",
                "conclusion": "failure",
                "html_url": "https://github.com/o/r/actions/runs/102",
            }
        )
        res2 = self.run_eval(c2)
        self.assertEqual(res2["action"], "skip")
        self.assertIn("main is red: latest Build production images failure", res2["reason"])

    def test_skips_when_install_check_failed_or_missing_or_running(self):
        c = FakeClient()
        c.install_runs[0]["conclusion"] = "failure"
        res = self.run_eval(c)
        self.assertEqual(res["action"], "skip")
        self.assertIn("install check on main failure", res["reason"])

        c2 = FakeClient()
        c2.install_runs = []
        res2 = self.run_eval(c2)
        self.assertEqual(res2["action"], "skip")
        self.assertIn("no completed release install check", res2["reason"])

        c3 = FakeClient()
        c3.install_runs[0]["status"] = "in_progress"
        res3 = self.run_eval(c3)
        self.assertEqual(res3["action"], "skip")
        self.assertIn("install check on main is in progress", res3["reason"])

    def test_skips_when_local_verify_pending(self):
        c = FakeClient()
        with tempfile.TemporaryDirectory() as tmp:
            vf = Path(tmp) / "version"
            sf = Path(tmp) / "verified"
            vf.write_text("server-v0.2.200\n", encoding="utf-8")
            sf.write_text("server-v0.2.199\n", encoding="utf-8")
            res = self.run_eval(c, check_local=True, version_file=vf, state_file=sf)
            self.assertEqual(res["action"], "skip")
            self.assertIn("waiting for Verify release", res["reason"])


if __name__ == "__main__":
    unittest.main()
