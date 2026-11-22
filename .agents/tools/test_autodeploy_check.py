import importlib.util
import os
import unittest

HERE = os.path.dirname(__file__)
spec = importlib.util.spec_from_file_location("autodeploy_check", os.path.join(HERE, "autodeploy-check.py"))
ad = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ad)


def fact(paths=("src/a.cs",), tier="medium", risks=(), migrations=()):
    return dict(number=5, tier=tier, risks=list(risks), migrations=list(migrations), paths=list(paths))


class Blockers(unittest.TestCase):
    def test_off_by_default(self):
        self.assertEqual(ad.blockers([fact()], False), ["autonomous deploys are switched off"])

    def test_plain_release_is_eligible(self):
        self.assertEqual(ad.blockers([fact(), fact(tier="low")], True), [])

    def test_empty_release_is_not(self):
        self.assertTrue(ad.blockers([], True))

    def test_owner_cases(self):
        self.assertIn("#5: high tier", ad.blockers([fact(tier="high")], True))
        self.assertIn("#5: migration", ad.blockers([fact(paths=["src/X/Migrations/1_A.cs"])], True))
        for path in ("src/Automaton.Runner/a.cs", "src/Automaton.Messaging/a.cs", "scripts/update-automaton.sh"):
            self.assertIn("#5: Runner, broker, installer or updater change", ad.blockers([fact(paths=[path])], True))
        self.assertIn("#5: workflow or CI change", ad.blockers([fact(risks=["workflow or CI change"])], True))


if __name__ == "__main__":
    unittest.main()
