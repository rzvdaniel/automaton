import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("check_image_sizes", Path(__file__).resolve().parent.parent / "check-image-sizes.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
BASELINE = {"marginPercent": 10, "images": {"api": 100_000_000}}


class CheckImageSizes(unittest.TestCase):
    def test_within_margin_passes(self):
        self.assertTrue(module.check(BASELINE, "api", 110_000_000)[0])

    def test_shrink_passes(self):
        self.assertTrue(module.check(BASELINE, "api", 50_000_000)[0])

    def test_growth_beyond_margin_fails(self):
        self.assertFalse(module.check(BASELINE, "api", 110_000_001)[0])

    def test_unknown_component_fails(self):
        self.assertFalse(module.check(BASELINE, "other", 1)[0])


if __name__ == "__main__":
    unittest.main()
