import importlib.util
import os
import re
import unittest

HERE = os.path.dirname(__file__)
spec = importlib.util.spec_from_file_location("release_report", os.path.join(HERE, "release-report.py"))
rr = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rr)

HIGH = ["/src/Automaton.Identity/"]
SECURITY = ["/.github/workflows/"]
BODY = """Refs #1.

- **Thing:** did a thing.

**How verified:** ran tests https://example.com/shot.png
**Risk:** low
**How to revert:** revert this PR
"""
REVIEW = {"authorAssociation": "OWNER", "state": "COMMENTED", "submittedAt": "2026-01-01T00:00:00Z",
          "body": "APPROVE\nTier: medium\n\nFindings\n- one\n- two\n\nSecurity: checked", "commit": {"oid": "abc"}}
DOWN_OK = "protected override void Up(MigrationBuilder b)\n        {\n            b.AddColumn();\n        }\n\n        protected override void Down(MigrationBuilder b)\n        {\n            b.DropColumn();\n        }\n"
DROP = "protected override void Up(MigrationBuilder b)\n        {\n            b.DropTable(name: \"X\");\n        }\n\n        protected override void Down(MigrationBuilder b)\n        {\n            b.CreateTable(name: \"X\");\n        }\n"


def pr(files=("src/a.cs",), reviews=(REVIEW,), body=BODY):
    return dict(number=7, title="T", url="https://github.com/o/r/pull/7", body=body, reviews=list(reviews), comments=[],
                commits=[], statusCheckRollup=[], headRefOid="abc", labels=[])


class Parts(unittest.TestCase):
    def test_sections(self):
        self.assertEqual(rr.section(BODY, "How to revert"), "revert this PR")
        self.assertIn("did a thing", rr.what_changed(BODY))
        self.assertNotIn("How verified", rr.what_changed(BODY))

    def test_findings_count(self):
        self.assertEqual(rr.findings_count(REVIEW["body"]), 2)
        self.assertIsNone(rr.findings_count("APPROVE"))
        self.assertEqual(rr.findings_count("APPROVE\nImportant: 1, Nit: 2, Pre-existing: 0"), 3)
        self.assertEqual(rr.findings_count("Important: 0 findings"), 0)

    def test_migration_notes(self):
        self.assertIn("Down reverses it without losing data", rr.migration_note("A", DOWN_OK))
        note = rr.migration_note("B", DROP)
        self.assertIn("drops or rewrites data", note)
        self.assertIn("NOT restored", note)
        self.assertIn("does not undo", rr.migration_note("C", "protected override void Up(MigrationBuilder b)\n        {\n            b.AddColumn();\n        }\n"))


class Facts(unittest.TestCase):
    def facts(self, files, texts=None, **kw):
        return rr.pr_facts(pr(**kw), list(files), texts or {}, HIGH, SECURITY)

    def test_plain_change_is_not_risky(self):
        f = self.facts(["src/a.cs"])
        self.assertEqual((f["tier"], f["risks"], f["findings"], f["security"]), ("medium", [], 2, True))
        self.assertEqual(f["proofs"], ["https://example.com/shot.png"])
        self.assertEqual(f["verdict"], "approve")

    def test_risky_reasons(self):
        self.assertIn("workflow or CI change", self.facts([".github/workflows/x.yml"])["risks"])
        f = self.facts(["src/Automaton.Identity/a.cs"])
        self.assertEqual(f["tier"], "high")
        self.assertIn("sign-in, access or authorization", f["risks"])
        self.assertIn("infrastructure or deploy", self.facts(["scripts/update-automaton.sh"])["risks"])
        path = "src/Automaton.Infrastructure/Migrations/1_X.cs"
        f = self.facts([path], {path: DROP})
        self.assertIn("data-dropping migration (rows are not restored by Down)", f["risks"])

    def test_reviewer_raised_tier(self):
        raised = dict(REVIEW, body="APPROVE\nTier: high - drops a column\nSecurity: ok")
        f = self.facts(["src/a.cs"], reviews=[raised])
        self.assertEqual(f["tier"], "high")
        self.assertIn("reviewer raised the tier to high", f["risks"])

    def test_no_review(self):
        f = self.facts(["src/a.cs"], reviews=[])
        self.assertEqual((f["verdict"], f["security"], f["findings"]), ("none", False, None))


class Render(unittest.TestCase):
    def test_layout(self):
        risky = rr.pr_facts(pr(), [".github/workflows/x.yml"], {}, HIGH, SECURITY)
        text = rr.render([risky, rr.pr_facts(pr(), ["src/a.cs"], {}, HIGH, SECURITY)], "Server 1")
        self.assertLess(text.index("### Risky"), text.index("<details>"))
        self.assertEqual(text.count("<details>"), 2)
        self.assertEqual(text.count("</details>"), 2)
        self.assertIn("workflow or CI change", text.split("### Pull requests")[0])
        self.assertIn("Nothing flagged", rr.render([], "Server 1"))


class Secrets(unittest.TestCase):
    def test_oauth_token_only_in_the_release_workflow(self):
        folder = os.path.join(HERE, "..", "..", ".github", "workflows")
        for name in os.listdir(folder):
            text = open(os.path.join(folder, name), encoding="utf-8").read()
            if name != "server-release.yml":
                self.assertNotIn("CLAUDE_CODE_OAUTH_TOKEN", text, name)

    def test_action_is_pinned_to_a_sha(self):
        text = open(os.path.join(HERE, "..", "..", ".github", "workflows", "server-release.yml"), encoding="utf-8").read()
        for ref in re.findall(r"uses: anthropics/claude-code-action@(\S+)", text):
            self.assertRegex(ref, r"^[0-9a-f]{40}$")


if __name__ == "__main__":
    unittest.main()
