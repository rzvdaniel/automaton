import importlib.util, unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("cap", Path(__file__).with_name("check-action-pins.py"))
cap = importlib.util.module_from_spec(spec); spec.loader.exec_module(cap)
S = "a" * 40


class FindPins(unittest.TestCase):
    def pins(self, text): return list(cap.find_pins(text))

    def test_pinned_with_comment_and_path(self):
        t = f"- uses: o/r@{S} # v1\n- uses: o/r/sub/dir@{S}\n  uses: \"o/q@{S}\"\n"
        self.assertEqual([p[1:] for p in self.pins(t)], [("o", "r", S), ("o", "r", S), ("o", "q", S)])

    def test_skips_tags_local_docker_short_sha(self):
        t = f"- uses: actions/checkout@v4\n- uses: ./.github/actions/x\n- uses: docker://img@sha256:{'b'*64}\n- uses: o/r@{'a'*39}\n"
        self.assertEqual(self.pins(t), [])

    def test_line_numbers(self):
        self.assertEqual(self.pins(f"a: 1\n- uses: o/r@{S}\n")[0][0], 2)


class Check(unittest.TestCase):
    def run_check(self, responses):
        cap.api = lambda p: responses.get(p.split("/")[3], (404, None))
        return cap.check("o", "r", S)

    def test_commit_ok(self): self.assertIsNone(self.run_check({"commits": (200, {})}))

    def test_tag_object_names_commit(self):
        msg = self.run_check({"git": (200, {"object": {"type": "commit", "sha": "c" * 40}})})
        self.assertIn("c" * 40, msg); self.assertIn("tag object", msg)

    def test_unknown(self): self.assertIn("not a commit", self.run_check({}))


if __name__ == "__main__": unittest.main()
