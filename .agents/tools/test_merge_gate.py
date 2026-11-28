import importlib.util, os, unittest

spec = importlib.util.spec_from_file_location("gate", os.path.join(os.path.dirname(__file__), "merge-gate.py"))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)

OWNERS = "# comment\n/src/Automaton.Identity/ @o\n/scripts/update-automaton.sh @o  # why\n\n# --- security pass ---\n/.github/workflows/ @o\n"
PATTERNS = gate.owner_patterns(OWNERS)
SECURITY = gate.security_patterns(OWNERS)
BODY = "Refs #1.\n**How verified:** x\n```\n$ run\nok\n```\n**Risk:** medium\n**How to revert:** revert this PR\n**Docs:** none, because test"
GREEN = [{"name": "tests", "conclusion": "SUCCESS"}]


def review(body, commit="abc", state="COMMENTED", at="2026-01-01T00:00:00Z", who="main-reviewer"):
    return {"body": body, "state": state, "commit": {"oid": commit}, "submittedAt": at,
            "authorAssociation": "OWNER", "author": {"login": who}}


def pr(paths, reviews=(), labels=(), body=BODY, checks=GREEN):
    return {"headRefOid": "abc", "body": body, "paths": list(paths), "reviews": list(reviews),
            "labels": [{"name": n} for n in labels], "checks": checks, "comments": [], "commits": []}


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
        self.assertEqual(gate.path_tier(["docs/roadmap/README.md", "docs/maintainers/x.md"], PATTERNS), "low")

    def test_agent_guidance_and_code_are_medium(self):
        for path in ("AGENTS.md", ".agents/skills/pr-review/SKILL.md", ".claude/skills/a/SKILL.md", "docs/x.sh",
                     ".github/pull_request_template.md", "shared/ai-guidance/seed/cli/project-files/AGENTS.md",
                     "src/Automaton.Api/Program.cs", "scripts/host-storage.sh"):
            self.assertEqual(gate.path_tier(["README.md", path], PATTERNS), "medium", path)


REAL = gate.owner_patterns(open(os.path.join(os.path.dirname(__file__), "..", "..", ".github", "CODEOWNERS")).read())


def file(path, status="modified", deletions=0):
    return {"path": path, "status": status, "deletions": deletions}


class HardToUndo(unittest.TestCase):
    def test_migrations_and_runner_are_high(self):
        for path in ("src/Automaton.Infrastructure/Migrations/2026_X.cs", "src/Automaton.Runner/Program.cs",
                     "src/Automaton.RuntimeHost/a.cs", "src/Automaton.Messaging/a.cs"):
            self.assertEqual(gate.path_tier([path], REAL), "high", path)

    def test_studio_and_api_services_stay_medium(self):
        for path in ("src/automaton-studio-vue/src/a.vue", "src/Automaton.Api/Services/AppService.cs",
                     "src/Automaton.Infrastructure/Data/Db.cs"):
            self.assertEqual(gate.path_tier([path], REAL), "medium", path)

    def test_large_removal_is_high(self):
        big = [file("src/Automaton.Api/a.cs", deletions=501)]
        self.assertEqual(gate.check(dict(pr(["src/Automaton.Api/a.cs"]), files=big), REAL)[0], "high")
        small = [file("src/Automaton.Api/a.cs", deletions=500), file("docs/x.md", deletions=900)]
        self.assertEqual(gate.check(dict(pr(["src/Automaton.Api/a.cs"]), files=small), REAL)[0], "medium")

    def test_removed_project_is_high(self):
        gone = [file("src/Automaton.Old/Old.csproj", "removed", 20)]
        self.assertEqual(gate.check(dict(pr(["src/Automaton.Old/Old.csproj"]), files=gone), REAL)[0], "high")


class Gate(unittest.TestCase):
    def test_low_passes_without_review(self):
        self.assertEqual(failed(gate.check(pr(["README.md"]), PATTERNS)), [])

    def test_medium_needs_review_of_final_commit(self):
        code = ["src/Automaton.Api/Program.cs"]
        self.assertEqual(failed(gate.check(pr(code), PATTERNS)), ["review"])
        self.assertEqual(failed(gate.check(pr(code, [review("APPROVE", commit="old")]), PATTERNS)), ["review"])
        self.assertEqual(failed(gate.check(pr(code, [review("CHANGES NEEDED\n...")]), PATTERNS)), ["review"])
        self.assertEqual(failed(gate.check(pr(code, [review("**APPROVE**\n\nTier: medium")]), PATTERNS)), [])
        self.assertEqual(failed(gate.check(pr(code, [review("Checked the route change; no test, it only loads a script.")]), PATTERNS)), [])

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
        tier, rows = gate.check(pr(["src/a.cs"], [first, later]), PATTERNS)
        self.assertEqual(tier, "high")
        self.assertEqual(failed((tier, rows)), ["security"])

    def test_dismissed_review_does_not_count(self):
        gone = dict(review("APPROVE"), state="DISMISSED")
        self.assertEqual(failed(gate.check(pr(["src/a.cs"], [gone]), PATTERNS)), ["review"])

    def test_reviewer_can_raise_the_tier(self):
        tier, rows = gate.check(pr(["src/a.cs"], [review("APPROVE\nTier: high - drops a column")]), PATTERNS)
        self.assertEqual(tier, "high")
        self.assertEqual(failed((tier, rows)), ["security"])

    def test_security_path_needs_security(self):
        paths = [".github/workflows/tests.yml"]
        self.assertEqual(failed(gate.check(pr(paths, [review("APPROVE")]), PATTERNS, SECURITY)), ["security"])
        ok = review("APPROVE\nTier: medium\nSecurity: checked the token permissions")
        self.assertEqual(gate.check(pr(paths, [ok]), PATTERNS, SECURITY)[0], "medium")
        self.assertEqual(failed(gate.check(pr(paths, [ok]), PATTERNS, SECURITY)), [])

    def test_high_needs_security_but_no_owner_label(self):
        paths = ["src/Automaton.Identity/a.cs"]
        ok = review("APPROVE\nTier: high\n\nSecurity: checked the token permissions")
        # One reviewer cannot be both the main review and the security pass on a high-tier change.
        self.assertEqual(failed(gate.check(pr(paths, [ok]), PATTERNS)), ["security"])
        main = review("APPROVE\nTier: high", at="2026-01-01T00:00:00Z")
        second = review("APPROVE\nSecurity: checked the token permissions", at="2026-01-02T00:00:00Z", who="security-reviewer")
        self.assertEqual(failed(gate.check(pr(paths, [main, second]), PATTERNS)), [])
        same = review("APPROVE\nSecurity: ok", at="2026-01-02T00:00:00Z")
        self.assertEqual(failed(gate.check(pr(paths, [main, same]), PATTERNS)), ["security"])
        stale = review("APPROVE\nSecurity: ok", commit="old", at="2026-01-02T00:00:00Z", who="security-reviewer")
        self.assertIn("security", failed(gate.check(pr(paths, [main, stale]), PATTERNS)))
        bare = review("APPROVE\nTier: high")
        self.assertEqual(failed(gate.check(pr(paths, [bare]), PATTERNS)), ["security"])

    def test_skip_ci_ignores_checks(self):
        self.assertEqual(failed(gate.check(pr(["README.md"], checks=[]), PATTERNS, skip_ci=True)), [])

    def test_own_merge_gate_check_does_not_count(self):
        own = GREEN + [{"name": "merge-gate", "status": "COMPLETED", "conclusion": "FAILURE"}]
        self.assertEqual(failed(gate.check(pr(["README.md"], checks=own), PATTERNS)), [])
        self.assertEqual(failed(gate.check(pr(["README.md"], checks=[own[1]]), PATTERNS)), ["ci"])

    def test_latest_result_per_check_is_what_counts(self):
        # The gate gets GitHub's filter=latest list: the older failed run of "tests" is not in it.
        self.assertEqual(failed(gate.check(pr(["README.md"], checks=GREEN), PATTERNS)), [])

    def test_description_needs_docs_line(self):
        body = BODY.replace("\n**Docs:** none, because test", "")
        self.assertEqual(failed(gate.check(pr(["README.md"], body=body), PATTERNS)), ["description"])

    def test_ci_and_description(self):
        self.assertEqual(failed(gate.check(pr(["README.md"], checks=[]), PATTERNS)), ["ci"])
        pending = [{"name": "tests", "status": "IN_PROGRESS", "conclusion": ""}]
        self.assertEqual(failed(gate.check(pr(["README.md"], checks=pending), PATTERNS)), ["ci"])
        self.assertEqual(failed(gate.check(pr(["README.md"], body=BODY + "\nFixes #12"), PATTERNS)), ["description"])
        self.assertEqual(failed(gate.check(pr(["README.md"], body="Refs #1"), PATTERNS)), ["description"])


FENCED = "\n```text\n$ curl -i x\nHTTP/1.1 200\n```"
IMG = "![shot](https://github.com/user-attachments/assets/1)"


class Proof(unittest.TestCase):
    def verdict(self, paths, verified, author=""):
        body = "**How verified:** " + verified + "\n**Risk:** low\n**How to revert:** r\n**Docs:** none, because t"
        item = dict(pr(paths, body=body), author={"login": author})
        return [row for row in gate.check(item, PATTERNS)[1] if row[1] == "proof"]

    def test_ui_needs_an_image(self):
        ui = ["src/automaton-studio-vue/src/a.vue"]
        self.assertEqual(self.verdict(ui, "ran it")[0][0], "FAIL")
        self.assertEqual(self.verdict(ui, FENCED)[0][0], "FAIL")
        self.assertEqual(self.verdict(ui, IMG)[0][0], "PASS")
        self.assertEqual(self.verdict(ui, '<img src="a.png">')[0][0], "PASS")

    def test_api_cli_and_scripts_need_a_code_block(self):
        for path in ("src/Automaton.Api/A.cs", "src/Automaton.Cli/B.cs", "scripts/x.sh", "docker/install/backup.sh", "docker/Dockerfile.server", "scripts/x.ps1", "docker-compose.prod.yml"):
            self.assertEqual(self.verdict([path], "ran it")[0][0], "FAIL", path)
            self.assertEqual(self.verdict([path], FENCED)[0][0], "PASS", path)

    def test_mixed_change_needs_both(self):
        paths = ["src/automaton-vue/a.vue", "src/Automaton.Api/A.cs"]
        self.assertEqual(self.verdict(paths, IMG)[0][0], "FAIL")
        self.assertEqual(self.verdict(paths, IMG + FENCED)[0][0], "PASS")

    def test_no_proof_for_docs_tests_ci_and_dependabot(self):
        for paths in (["docs/a.md"], ["src/UnitTests/ATests.cs"], [".github/workflows/t.yml"],
                      ["src/automaton-studio-vue/src/a.test.ts"], ["src/automaton-vue/docs/.vitepress/config.ts"],
                      ["docker/README.md"], ["src/automaton-vue/src/__tests__/a.ts"]):
            self.assertEqual(self.verdict(paths, "ran it"), [], paths)
        self.assertEqual(self.verdict(["src/automaton-studio-vue/package.json", "src/A/B.cs"], "x", "dependabot[bot]"), [])

    def test_excuse_turns_fail_into_warn(self):
        paths = ["src/Automaton.Api/A.cs"]
        self.assertEqual(self.verdict(paths, "x\nProof: not possible, because it needs a fresh server")[0][0], "WARN")
        self.assertEqual(self.verdict(paths, "x\nProof: not possible, because")[0][0], "FAIL")

    def test_bold_line_inside_a_code_block_does_not_end_the_section(self):
        out = "\n```\n**Result:** ok\n$ run\n```"
        self.assertEqual(self.verdict(["src/A/B.cs"], out)[0][0], "PASS")

    def test_proof_after_how_verified_does_not_count(self):
        body = "**How verified:** none\n**Risk:** low\n" + FENCED + "\n**How to revert:** r\n**Docs:** none, because t"
        item = dict(pr(["src/A/B.cs"], body=body), author=None)
        self.assertIn("proof", failed(gate.check(item, PATTERNS)))


class InstallCheck(unittest.TestCase):
    RUN = {"id": "9", "path": ".github/workflows/release-install-check.yml", "head_sha": "abc", "conclusion": "success"}

    def item(self, path, runs):
        item = pr([path])
        item["install_runs"] = runs
        return item

    def test_ci_script_without_a_branch_run_fails(self):
        self.assertIn("install-check", failed(gate.check(self.item("scripts/ci/x.sh", []), PATTERNS)))

    def test_passing_run_on_the_final_commit_passes(self):
        self.assertNotIn("install-check", failed(gate.check(self.item("scripts/ci/x.sh", [self.RUN]), PATTERNS)))

    def test_workflow_change_needs_it_too(self):
        path = ".github/workflows/release-install-check.yml"
        self.assertIn("install-check", failed(gate.check(self.item(path, []), PATTERNS)))

    def test_stale_or_failed_run_fails(self):
        for bad in ({**self.RUN, "head_sha": "old"}, {**self.RUN, "conclusion": "failure"}, {**self.RUN, "path": ".github/workflows/tests.yml"}):
            self.assertIn("install-check", failed(gate.check(self.item("scripts/ci/x.sh", [bad]), PATTERNS)))

    def test_other_paths_need_no_run(self):
        self.assertNotIn("install-check", [n for _, n, _ in gate.check(pr(["src/a.cs"]), PATTERNS)[1]])

    def test_run_ids_come_from_how_verified(self):
        body = "**How verified:** https://github.com/o/r/actions/runs/123\n**Risk:** low"
        self.assertEqual(gate.install_run_ids(body), ["123"])


if __name__ == "__main__":
    unittest.main()
