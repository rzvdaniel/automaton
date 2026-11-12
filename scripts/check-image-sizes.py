#!/usr/bin/env python3
"""Fail when a built image is larger than its recorded baseline plus the agreed margin.

Usage: check-image-sizes.py <component> <image-ref>
Baselines live in docker/image-size-baseline.json (uncompressed bytes as `docker image inspect` reports them).
To accept a deliberate increase or record a shrink, edit the baseline in the same pull request and say why.
"""
import json
import subprocess
import sys
from pathlib import Path

BASELINE = Path(__file__).resolve().parent.parent / "docker" / "image-size-baseline.json"


def check(baseline: dict, component: str, size: int) -> tuple[bool, str]:
    entry = baseline["images"].get(component)
    if entry is None:
        return False, f"{component}: no baseline recorded in {BASELINE.name}; add one"
    limit = int(entry * (1 + baseline["marginPercent"] / 100))
    mb = lambda value: f"{value / 1e6:.1f} MB"
    message = f"{component}: {mb(size)} (baseline {mb(entry)}, limit {mb(limit)} = +{baseline['marginPercent']}%)"
    return size <= limit, message


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    component, image = sys.argv[1], sys.argv[2]
    size = int(subprocess.run(["docker", "image", "inspect", "--format", "{{.Size}}", image],
                              check=True, capture_output=True, text=True).stdout.strip())
    ok, message = check(json.loads(BASELINE.read_text()), component, size)
    print(("OK   " if ok else "FAIL ") + message)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
