import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("release_summary", Path(__file__).with_name("release-summary.py"))
rs = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rs)

REPO = "rzvdaniel/automaton"
TAG = "server-v0.2.200"
TAG_SHA = "8c4b0163d1f5c533a6125731175bf169264897a3"
AFTER_SHA = "f4bc3ba861f91ef80992b1c3b56da36cece415cd"


class FakeSummaryClient:
    def __init__(self):
        self.items = [
            {"status": "Verify", "content": {"type": "Issue", "repository": REPO, "number": 1871, "title": "Refresh checkout"}},
            {"status": "Verify", "content": {"type": "Issue", "repository": REPO, "number": 1884, "title": "Security headers"}},
            {"status": "Verify", "content": {"type": "Issue", "repository": "rzvdaniel/automaton-apps", "number": 526, "title": "Apps card"}},
        ]
        self.issues = {
            1871: {"number": 1871, "title": "Refresh checkout", "html_url": f"https://github.com/{REPO}/issues/1871", "labels": [{"name": "verified"}]},
            1884: {"number": 1884, "title": "Security headers", "html_url": f"https://github.com/{REPO}/issues/1884", "labels": []},
        }
        self.prs = [
            {"number": 1872, "title": "Refresh checkout (#1871)", "body": "Refs #1871.", "merged_at": "2026-11-28T05:00:00Z", "merge_commit_sha": TAG_SHA},
            {"number": 1885, "title": "Security headers (#1884)", "body": "Refs #1884.", "merged_at": "2026-11-28T09:00:00Z", "merge_commit_sha": AFTER_SHA},
        ]
        self.artifacts = {1871: [{"name": "proof", "number": 1}], 1884: []}
        self.install_runs = [{"head_sha": TAG_SHA, "status": "completed", "conclusion": "success", "html_url": "https://run/ic"}]
        self.pinned = [
            {"issue": {"number": 1236, "title": "Plan", "state": "CLOSED"}},
            {"issue": {"number": 1900, "title": "Release 0.2.199", "state": "OPEN"}},
        ]
        self.existing_release_issue = None
        self.created = []
        self.pinned_calls = []
        self.unpinned_calls = []
        self.posted_comments = []
        self.updated_comments = []
        self.existing_comments = []

    def api(self, path: str):
        if path == f"repos/{REPO}/git/ref/tags/{TAG}":
            return {"object": {"type": "commit", "sha": TAG_SHA}}
        if path.startswith(f"repos/{REPO}/pulls?"):
            return self.prs
        if path.startswith(f"repos/{REPO}/issues/"):
            rest = path[len(f"repos/{REPO}/issues/"):]
            num = int(rest.split("/")[0].split("?")[0])
            if "/artifacts" in rest:
                return self.artifacts.get(num, [])
            return self.issues[num]
        if path == f"repos/{REPO}/compare/{TAG}...{TAG_SHA}?per_page=1":
            return {"status": "identical"}
        if path == f"repos/{REPO}/compare/{TAG}...{AFTER_SHA}?per_page=1":
            return {"status": "ahead"}
        if path.startswith(f"repos/{REPO}/actions/workflows/release-install-check.yml/runs"):
            return {"workflow_runs": self.install_runs}
        raise AssertionError(f"Unexpected api path: {path}")

    def project_items(self, owner: str, number: str):
        return self.items

    def graphql(self, query: str, **variables):
        return {"data": {"repository": {"pinnedIssues": {"nodes": self.pinned}}}}

    def find_issue_by_title(self, repo: str, title: str):
        return self.existing_release_issue

    def create_issue(self, repo: str, title: str, body: str):
        issue = {"number": 1901, "title": title, "state": "OPEN", "url": f"https://github.com/{repo}/issues/1901"}
        self.created.append(issue)
        return issue

    def reopen_issue(self, repo: str, number: int):
        pass

    def pin_issue(self, repo: str, number: int):
        self.pinned_calls.append(number)

    def unpin_issue(self, repo: str, number: int):
        self.unpinned_calls.append(number)

    def list_comments(self, repo: str, number: int):
        return self.existing_comments

    def post_comment(self, repo: str, number: int, body: str):
        self.posted_comments.append((number, body))
        return {"id": 55}

    def update_comment(self, repo: str, comment_id: int, body: str):
        self.updated_comments.append((comment_id, body))
        return {"id": comment_id}


class ReleaseSummaryTests(unittest.TestCase):
    def test_ready_to_deploy_yes_and_pins_new_release_issue(self):
        c = FakeSummaryClient()
        summary, issue, body = rs.publish_summary(c, REPO, "rzvdaniel/17", "server-v0.2.200", run_url="https://run/v")
        self.assertTrue(summary["ready"])
        self.assertEqual(len(summary["verified"]), 1)
        self.assertEqual(len(summary["skipped"]), 2)
        self.assertEqual(issue["number"], 1901)
        self.assertEqual(c.unpinned_calls, [1900])
        self.assertEqual(c.pinned_calls, [1901])
        self.assertEqual(len(c.posted_comments), 1)
        self.assertIn("ready to deploy: yes", body)
        self.assertIn("### verified (1)", body)
        self.assertIn("### verified-partial (0)", body)
        self.assertIn("### failed (0)", body)
        self.assertIn("[proof](https://github.com/rzvdaniel/automaton/issues/1871)", body)

    def test_ready_to_deploy_no_when_partial_failed_or_unlabeled(self):
        c = FakeSummaryClient()
        c.issues[1871]["labels"] = [{"name": "verified-partial"}]
        summary, _, body = rs.collect_release_status(c, REPO, "rzvdaniel/17", "0.2.200"), None, ""
        self.assertFalse(summary["ready"])
        self.assertIn("1 verified-partial", summary["ready_reason"])

        c2 = FakeSummaryClient()
        c2.issues[1871]["labels"] = []
        summary2 = rs.collect_release_status(c2, REPO, "rzvdaniel/17", "0.2.200")
        self.assertFalse(summary2["ready"])
        self.assertEqual(len(summary2["failed"]), 1)
        self.assertIn("unverified", summary2["failed"][0]["reason"])

        c3 = FakeSummaryClient()
        c3.install_runs[0]["conclusion"] = "failure"
        summary3 = rs.collect_release_status(c3, REPO, "rzvdaniel/17", "0.2.200")
        self.assertFalse(summary3["ready"])
        self.assertIn("install check failure", summary3["ready_reason"])

    def test_updates_existing_summary_comment_instead_of_duplicating(self):
        c = FakeSummaryClient()
        c.existing_release_issue = {"number": 1901, "title": "Release 0.2.200", "state": "OPEN", "url": f"https://github.com/{REPO}/issues/1901"}
        c.pinned.append({"issue": {"number": 1901, "title": "Release 0.2.200", "state": "OPEN"}})
        c.existing_comments = [{"id": 77, "body": "<!-- release-summary: 0.2.200 -->\nold"}]
        rs.publish_summary(c, REPO, "rzvdaniel/17", "0.2.200")
        self.assertEqual(c.posted_comments, [])
        self.assertEqual(len(c.updated_comments), 1)
        self.assertEqual(c.updated_comments[0][0], 77)
        self.assertEqual(c.pinned_calls, [])


if __name__ == "__main__":
    unittest.main()
