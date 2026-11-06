import importlib.util, os, unittest

spec = importlib.util.spec_from_file_location("cri", os.path.join(os.path.dirname(__file__), "close-released-issues.py"))
cri = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cri)

CTX = {"accept": set(), "reason": "", "published": set(), "website_sha": None}


def pr(kind, **in_release):
    return {"number": 1, "kind": kind, "in_release": in_release}


class Decide(unittest.TestCase):
    def test_server_in_release_closes(self):
        self.assertEqual(cri.decide(5, {"verified"}, [pr("server", server=True)], CTX)[0], "close")

    def test_server_not_in_release_kept(self):
        self.assertEqual(cri.decide(5, {"verified"}, [pr("server", server=False)], CTX)[0], "skip")

    def test_unknown_compare_is_not_closed(self):
        self.assertEqual(cri.decide(5, {"verified"}, [pr("server", server=None)], CTX)[0], "skip")

    def test_one_pr_missing_blocks(self):
        prs = [pr("server", server=True), pr("server", server=False)]
        self.assertEqual(cri.decide(5, {"verified"}, prs, CTX)[0], "skip")

    def test_no_pr_needs_human(self):
        self.assertEqual(cri.decide(5, {"verified"}, [], CTX)[0], "human")

    def test_website_needs_sha(self):
        self.assertEqual(cri.decide(5, {"verified"}, [pr("website")], CTX)[0], "human")
        ctx = dict(CTX, website_sha="abc")
        self.assertEqual(cri.decide(5, {"verified"}, [pr("website", website=True)], ctx)[0], "close")
        self.assertEqual(cri.decide(5, {"verified"}, [pr("website", website=False)], ctx)[0], "skip")

    def test_apps_need_published(self):
        self.assertEqual(cri.decide(5, {"verified"}, [pr("apps")], CTX)[0], "human")
        self.assertEqual(cri.decide(5, {"verified"}, [pr("apps")], dict(CTX, published={5}))[0], "close")

    def test_partial_and_failed_left_alone(self):
        self.assertEqual(cri.decide(5, {"verified-partial"}, [pr("server", server=True)], CTX)[0], "skip")
        self.assertEqual(cri.decide(5, {"verification-failed"}, [pr("server", server=True)], CTX)[0], "skip")

    def test_accept_needs_reason(self):
        ctx = dict(CTX, accept={5})
        self.assertEqual(cri.decide(5, {"verified-partial"}, [pr("server", server=True)], ctx)[0], "human")
        ctx["reason"] = "ok by owner"
        self.assertEqual(cri.decide(5, {"verified-partial"}, [pr("server", server=True)], ctx)[0], "close")


class Helpers(unittest.TestCase):
    def test_classify(self):
        self.assertEqual(cri.classify_files(["docs/a.md", "src/automaton-vue/x.vue"]), "website")
        self.assertEqual(cri.classify_files(["docs/a.md", "src/Server/x.cs"]), "server")
        self.assertEqual(cri.classify_files(["a"], repo_is_apps=True), "apps")
        self.assertIsNone(cri.classify_files([]))

    def test_references(self):
        self.assertTrue(cri.references("Refs #12", 12))
        self.assertTrue(cri.references("part of rzvdaniel/automaton#12.", 12))
        self.assertFalse(cri.references("Refs #123", 12))
        self.assertFalse(cri.references("mentions #12", 12))


if __name__ == "__main__":
    unittest.main()
