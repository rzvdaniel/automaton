import importlib.util, unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("ic", Path(__file__).with_name("issue-check.py"))
ic = importlib.util.module_from_spec(spec); spec.loader.exec_module(ic)

FEATURE = "**Goal**\nx\n\n**Example**\ny\n\n**Suggestion**\n- z\n\n**Done when**\nchecked\n\n**Related**\n#1\n"
BUG = "### Problem\na\n\n### Steps\n1. b\n\n### Result\nc\n\n### Expected\nd\n\n### Done when\ne\n"


class Missing(unittest.TestCase):
    def test_complete_feature_and_bug(self):
        self.assertEqual(ic.missing(FEATURE, set()), [])
        self.assertEqual(ic.missing(BUG, set()), [])
        self.assertEqual(ic.missing(FEATURE.replace("\n", "\r\n"), set()), [])

    def test_colon_inside_bold(self):
        self.assertEqual(ic.missing(FEATURE.replace("**Goal**", "**Goal:**"), set()), [])

    def test_related_is_optional(self):
        self.assertEqual(ic.missing(FEATURE.split("**Related**")[0], set()), [])

    def test_kind_from_label_or_problem_heading(self):
        self.assertEqual(ic.missing(FEATURE, {"bug"}), ["Problem", "Steps", "Result", "Expected"])
        self.assertEqual(ic.kind(set(), ic.sections(BUG)), "bug")
        self.assertEqual(ic.kind(set(), ic.sections(FEATURE)), "feature")

    def test_missing_sections_listed(self):
        self.assertEqual(ic.missing("**Goal**\nx\n**Done when**\ny", set()), ["Example", "Suggestion"])
        self.assertEqual(ic.missing("", set()), ["Goal", "Example", "Suggestion", "Done when"])
        self.assertEqual(ic.missing(None, set()), ["Goal", "Example", "Suggestion", "Done when"])

    def test_empty_done_when(self):
        for empty in ("", "  \n", "_No response_"):
            body = FEATURE.replace("checked", empty)
            self.assertEqual(ic.missing(body, set()), ["Done when (empty)"], repr(empty))

    def test_done_when_ends_at_next_heading(self):
        body = "**Goal**\na\n**Example**\nb\n**Suggestion**\nc\n**Done when**\n**Related**\n#1"
        self.assertEqual(ic.missing(body, set()), ["Done when (empty)"])

    def test_words_in_prose_are_not_headings(self):
        body = FEATURE.replace("- z", "- the Goal is to Done when")
        self.assertEqual(ic.missing(body, set()), [])


class Run(unittest.TestCase):
    def setUp(self):
        self.calls, self.comments = [], []
        ic.api = self.api

    def api(self, method, path, data=None):
        self.calls.append((method, path.split("?")[0], data))
        if method == "GET":
            return self.comments

    def event(self, body, labels=()):
        return {"issue": {"number": 7, "body": body, "labels": [{"name": n} for n in labels]}}

    def test_flags_once_with_label_and_comment(self):
        self.assertTrue(ic.run(self.event("**Goal**\nx"), "o/r").startswith("flagged"))
        methods = [(m, p) for m, p, _ in self.calls]
        self.assertIn(("POST", "repos/o/r/issues/7/comments"), methods)
        self.assertIn(("POST", "repos/o/r/issues/7/labels"), methods)

    def test_question_and_foreign_comment(self):
        self.assertEqual(ic.run(self.event("x", ["question"]), "o/r"), "skipped")
        self.comments = [{"id": 6, "body": ic.MARKER, "user": {"login": "someone"}}]
        self.assertIsNone(ic.find_comment("o/r", 7))

    def test_edits_same_comment(self):
        self.comments = [{"id": 5, "body": ic.MARKER + "\nold", "user": {"login": "github-actions[bot]"}}]
        ic.run(self.event("**Goal**\nx", [ic.LABEL]), "o/r")
        self.assertEqual([(m, p) for m, p, _ in self.calls if m != "GET"], [("PATCH", "repos/o/r/issues/comments/5")])

    def test_fixed_removes_label_and_comment(self):
        self.comments = [{"id": 5, "body": ic.MARKER + "\nold", "user": {"login": "github-actions[bot]"}}]
        self.assertEqual(ic.run(self.event(FEATURE, [ic.LABEL]), "o/r"), "ok")
        self.assertEqual([(m, p) for m, p, _ in self.calls if m != "GET"],
                         [("DELETE", "repos/o/r/issues/7/labels/needs-shape"), ("DELETE", "repos/o/r/issues/comments/5")])

    def test_valid_issue_without_label_does_nothing(self):
        ic.run(self.event(FEATURE), "o/r")
        self.assertEqual([c for c in self.calls if c[0] != "GET"], [])

    def test_skip_labels_and_pull_requests(self):
        for l in ic.SKIP_LABELS:
            self.assertEqual(ic.run(self.event("", [l]), "o/r"), "skipped")
        e = self.event(""); e["issue"]["pull_request"] = {}
        self.assertEqual(ic.run(e, "o/r"), "skipped")
        self.assertEqual(self.calls, [])

    def test_comment_never_repeats_body(self):
        text = ic.comment_text("feature", ic.missing("**Goal**\n$(rm -rf /) @everyone", set()))
        self.assertNotIn("rm -rf", text); self.assertNotIn("@everyone", text)


if __name__ == "__main__": unittest.main()
