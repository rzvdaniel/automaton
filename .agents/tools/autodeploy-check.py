#!/usr/bin/env python3
"""Say whether a release may deploy to production on its own (issue #1222).

Usage: python3 .agents/tools/autodeploy-check.py --from <previous-ref> --to <ref> [--enabled]

Autonomous deploys are OFF unless --enabled is passed (the orchestrator passes it only when its configuration
switches the feature on). Prints "AUTODEPLOY: yes" or "AUTODEPLOY: owner" with the reasons, one per line, and exits
0 for yes and 1 for owner. Eligible: every pull request is low or medium tier, has no flagged risk, no migration and
no change to the Runner, the broker (Messaging), the installer or the updater. Anything else stays the owner's
"deploy". Facts come from release-report.py, so the report and this check always agree.
"""
import argparse
import importlib.util
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
_spec = importlib.util.spec_from_file_location("release_report", HERE / "release-report.py")
rr = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(rr)

# Paths that always need the owner, whatever the tier.
OWNER_PATHS = ("src/Automaton.Runner/", "src/Automaton.Messaging/", "scripts/install-automaton.sh",
               "scripts/update-automaton.sh", "scripts/setup-service.sh", "scripts/setup-service.ps1")


def blockers(facts: list[dict], enabled: bool) -> list[str]:
    if not enabled:
        return ["autonomous deploys are switched off"]
    if not facts:
        return ["no pull requests in this release"]
    out = []
    for f in facts:
        tag = f"#{f['number']}"
        if f["tier"] == "high":
            out.append(f"{tag}: high tier")
        if f["migrations"] or any("/Migrations/" in p for p in f["paths"]):
            out.append(f"{tag}: migration")
        if any(p.startswith(OWNER_PATHS) for p in f["paths"]):
            out.append(f"{tag}: Runner, broker, installer or updater change")
        out += [f"{tag}: {r}" for r in f["risks"]]
    return list(dict.fromkeys(out))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--from", dest="start", required=True)
    ap.add_argument("--to", required=True)
    ap.add_argument("--enabled", action="store_true")
    args = ap.parse_args()
    subjects = rr.run("git", "log", "--format=%s", f"{args.start}..{args.to}").splitlines()
    numbers = sorted({int(m.group(1)) for s in subjects if (m := re.search(r"\(#(\d+)\)\s*$", s))})
    reasons = blockers(rr.collect(numbers, None, args.to) if args.enabled else [], args.enabled)
    print("AUTODEPLOY: " + ("owner" if reasons else "yes"))
    for reason in reasons:
        print(f"- {reason}")
    return 1 if reasons else 0


if __name__ == "__main__":
    sys.exit(main())
