import importlib.util, os, unittest

spec = importlib.util.spec_from_file_location("gate", os.path.join(os.path.dirname(__file__), "merge-gate.py"))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)

OWNERS = "# comment\n/src/Automaton.Identity/ @o\n/scripts/update-automaton.sh @o  # why\n\n# --- security pass ---\n/.github/workflows/ @o\n"
PATTERNS = gate.owner_patterns(OWNERS)
SECURITY = gate.security_patterns(OWNERS)
BODY = "Refs #1.\n**How verified:** x\n**Risk:** medium\n**How to revert:** revert this PR"
GREEN = [{"name": "tests", "conclusion": "SUCCESS"}]


def review(body, commit="abc", state="COMMENTED", at="2026-01-01T00:00:00Z"):
    return {"body": body, "state": state, "commit": {"oid": commit}, "submittedAt": at,
            "authorAssociation": "OWNER"}


def pr(paths, reviews=(), labels=(), body=BODY, checks=GREEN):
    return {"headRefOid": "abc", "body": body, "paths": list(paths), "reviews": list(reviews),
            "labels": [{"name": n} for n in labels], "statusCheckRollup": checks, "comments": [], "commits": []}


def failed(result):
    return sorted(name for outcome, name, _ in result[1] if outcome == "FAIL")


class Tier(unittest.TestCase):
    def test_patterns_skip_comments(self):
        self.assertEqual(PATTERNS, ["/src/Automaton.Identity/", "/scripts/update-automaton.sh"])
        self.assertEqual(SECURITY, ["/.github/workflows/"])

    def test_owner_path_is_high(self):
        self.assertEqual(gate.path_tier(["README.md", "src/Automaton.Identity/a.cs"], PATTERNS), "high")
        self.assertEqual(gate.path_tier([".github/workflows/tests.yml"], PATTERNS), "medium")
        self.assertEqual(gate.path_tier(["scripts/update-automaton.sh"], PATTERNS), "high")

    def test_workflows_are_high_in_the_real_codeowners(self):
        root = os.path.join(os.path.dirname(__file__), "..", "..", ".github", "CODEOWNERS")
        patterns = gate.owner_patterns(open(root).read())
        self.assertEqual(gate.path_tier([".github/workflows/deploy-production.yml"], patterns), "high")

    def test_docs_only_is_low(self):
        self.assertEqual(gate.path_tier(["docs/roadmap/README.md", "src/Automaton.Api/Documentation/Admin/apps.md"], PATTERNS), "low")

    def test_agent_guidance_and_code_are_medium(self):
        for path in ("AGENTS.md", ".agents/skills/pr-review/SKILL.md", ".claude/skills/a/SKILL.md", "docs/x.sh",
                     ".github/pull_request_template.md", "shared/ai-guidance/seed/cli/project-files/AGENTS.md",
                     "src/Automaton.Api/Program.cs", "scripts/host-storage.sh"):
            self.assertEqual(gate.path_tier(["README.md", path], PATTERNS), "medium", path)


class Gate(unittest.TestCase):
    def test_low_passes_without_review(self):
        self.assertEqual(failed(gate.check(pr(["README.md"]), PATTERNS)), [])

    def test_medium_needs_review_of_final_commit(self):
        code = ["src/Automaton.Api/Program.cs"]
        self.assertEqual(failed(gate.check(pr(code), PATTERNS)), ["review"])
        self.assertEqual(failed(gate.check(pr(code, [review("APPROVE", commit="old")]), PATTERNS)), ["review"])
        self.assertEqual(failed(gate.check(pr(code, [review("CHANGES NEEDED\n...")]), PATTERNS)), ["review"])
        self.assertEqual(failed(gate.check(pr(code, [review("**APPROVE**\n\nTier: medium")]), PATTERNS)), [])

    def test_review_from_an_outsider_does_not_count(self):
        outsider = dict(review("APPROVE\nSecurity: fine", at="2026-01-03T00:00:00Z"), authorAssociation="NONE")
        self.assertEqual(failed(gate.check(pr(["src/a.cs"], [outsider]), PATTERNS)), ["review"])
        changes = review("CHANGES NEEDED", at="2026-01-02T00:00:00Z")
        self.assertEqual(failed(gate.check(pr(["src/a.cs"], [changes, outsider]), PATTERNS)), ["review"])

    def test_latest_review_wins(self):
        reviews = [review("APPROVE", at="2026-01-01T00:00:00Z"), review("", state="CHANGES_REQUESTED", at="2026-01-02T00:00:00Z")]
        self.assertEqual(failed(gate.check(pr(["src/a.cs"], reviews), PATTERNS)), ["review"])

    def test_a_later_short_review_does_not_lower_the_tier(self):
        first = review("APPROVE\nTier: high - drops a column\nSecurity: ok", at="2026-01-01T00:00:00Z")
        later = review("APPROVE\nTier: medium\nNits fixed, no new code", at="2026-01-02T00:00:00Z")
        tier, rows = gate.check(pr(["src/a.cs"], [first, later], labels=["owner-approved"]), PATTERNS)
        self.assertEqual(tier, "high")
        self.assertEqual(failed((tier, rows)), ["security"])

    def test_dismissed_review_does_not_count(self):
        gone = dict(review("APPROVE"), state="DISMISSED")
        self.assertEqual(failed(gate.check(pr(["src/a.cs"], [gone]), PATTERNS)), ["review"])

    def test_reviewer_can_raise_the_tier(self):
        tier, rows = gate.check(pr(["src/a.cs"], [review("APPROVE\nTier: high - drops a column")]), PATTERNS)
        self.assertEqual(tier, "high")
        self.assertEqual(failed((tier, rows)), ["owner", "security"])

    def test_security_path_needs_security_but_not_owner(self):
        paths = [".github/workflows/tests.yml"]
        self.assertEqual(failed(gate.check(pr(paths, [review("APPROVE")]), PATTERNS, SECURITY)), ["security"])
        ok = review("APPROVE\nTier: medium\nSecurity: checked the token permissions")
        self.assertEqual(gate.check(pr(paths, [ok]), PATTERNS, SECURITY)[0], "medium")
        self.assertEqual(failed(gate.check(pr(paths, [ok]), PATTERNS, SECURITY)), [])

    def test_high_needs_security_and_owner(self):
        paths = ["src/Automaton.Identity/a.cs"]
        ok = review("APPROVE\nTier: high\n\nSecurity: checked the token permissions")
        self.assertEqual(failed(gate.check(pr(paths, [ok]), PATTERNS)), ["owner"])
        self.assertEqual(failed(gate.check(pr(paths, [ok], labels=["owner-approved"]), PATTERNS)), [])

    def test_ci_and_description(self):
        self.assertEqual(failed(gate.check(pr(["README.md"], checks=[]), PATTERNS)), ["ci"])
        pending = [{"name": "tests", "status": "IN_PROGRESS", "conclusion": ""}]
        self.assertEqual(failed(gate.check(pr(["README.md"], checks=pending), PATTERNS)), ["ci"])
        self.assertEqual(failed(gate.check(pr(["README.md"], body=BODY + "\nFixes #12"), PATTERNS)), ["description"])
        self.assertEqual(failed(gate.check(pr(["README.md"], body="Refs #1"), PATTERNS)), ["description"])


if __name__ == "__main__":
    unittest.main()
