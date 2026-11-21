import importlib.util, pathlib, unittest

spec = importlib.util.spec_from_file_location("dn", pathlib.Path(__file__).parent / "deploy-notice.py")
dn = importlib.util.module_from_spec(spec); spec.loader.exec_module(dn)

NOTES = """## AI check

Verdict: Yellow
- x

## Automaton Server 1.2.3

2 pull request(s) merged since the previous release: #1, #2.

<details><summary>#1 First thing (tier low)</summary>
body
</details>
<details><summary>#2 Second (tier high)</summary>
</details>
"""

class T(unittest.TestCase):
    def test_success(self):
        out = dn.build("1.2.3", NOTES, "success", "success", "http://run")
        self.assertTrue(out.startswith("Production deploy 1.2.3: OK"))
        for s in ("Yellow", "- #1 First thing", "- #2 Second", "2 pull request(s)", "http://run"):
            self.assertIn(s, out)

    def test_failure_and_missing_notes(self):
        out = dn.build("1.2.3", "", "failure", "", "")
        self.assertIn("needs attention", out)
        self.assertIn("FAILED", out)
        self.assertIn("unavailable", out)

if __name__ == "__main__":
    unittest.main()
