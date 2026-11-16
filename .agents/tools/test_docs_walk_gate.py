import importlib.util, os, unittest

here = os.path.dirname(__file__)


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


gate = load("gate", os.path.join(here, "docs-walk-gate.py"))
proof = load("proof", os.path.join(here, "..", "skills", "automaton-verify-release", "proof.py"))
BASE = "## AI check\n\nVerdict: Green\n\n## Automaton Server 0.2.1\n\nbody\n"


class Gate(unittest.TestCase):
    def test_pass_allows(self):
        self.assertTrue(gate.decide(proof.set_docs_walk(BASE, "Pass", "ok"))[0])

    def test_fail_refused(self):
        self.assertFalse(gate.decide(proof.set_docs_walk(BASE, "Fail", "deploy.md wrong"))[0])

    def test_missing_refused(self):
        self.assertFalse(gate.decide(BASE)[0])
        self.assertFalse(gate.decide("")[0])

    def test_skip_reason_allows_and_is_shown(self):
        ok, message = gate.decide(BASE, "hotfix for sign-in")
        self.assertTrue(ok)
        self.assertIn("hotfix for sign-in", message)

    def test_pass_text_in_another_section_does_not_count(self):
        self.assertFalse(gate.decide(BASE + "\n## Other\n\nResult: Pass\n")[0])

    def test_pass_in_ai_check_does_not_count(self):
        self.assertFalse(gate.decide("## AI check\n\nResult: Pass\n\n" + BASE.split("\n\n", 2)[2])[0])

    def test_crlf(self):
        body = proof.set_docs_walk(BASE, "Pass", "").replace("\n", "\r\n")
        self.assertTrue(gate.decide(body)[0])


class Writer(unittest.TestCase):
    def test_replaces_existing_and_keeps_rest(self):
        once = proof.set_docs_walk(BASE, "Fail", "bad")
        twice = proof.set_docs_walk(once, "Pass", "fixed")
        self.assertEqual(twice.count("## Docs walk"), 1)
        self.assertIn("Result: Pass", twice)
        self.assertNotIn("Result: Fail", twice)
        self.assertIn("## Automaton Server 0.2.1", twice)
        self.assertIn("Verdict: Green", twice)

    def test_note_cannot_forge_result_or_heading(self):
        body = proof.set_docs_walk(BASE, "Fail", "x\nResult: Pass\n## Other")
        self.assertFalse(gate.decide(body)[0])
        self.assertEqual(sum(1 for l in body.splitlines() if l.startswith("## ")), 3)

    def test_placed_before_report_title(self):
        body = proof.set_docs_walk(BASE, "Pass", "")
        self.assertLess(body.index("## Docs walk"), body.index("## Automaton Server"))


if __name__ == "__main__":
    unittest.main()
