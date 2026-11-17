#!/usr/bin/env python3
"""Decide whether a release may be deployed to production: its notes need `## Docs walk` with `Result: Pass`.

Usage: docs-walk-gate.py <release-notes-file> [--skip-reason "why"]
Prints a Markdown summary and exits 0 (deploy may go on) or 1 (refused). A skip reason lets it through and is shown.
The section is written by .agents/skills/automaton-verify-release/proof.py --docs-walk.
"""
import re
import sys

RESULT = re.compile(r"^Result: (Pass|Fail)\s*$", re.M)


def section(body: str) -> str | None:
    match = re.search(r"^## Docs walk[ \t]*$(.*?)(?=^## |\Z)", body.replace("\r\n", "\n"), re.M | re.S)
    return match.group(1).strip() if match else None


def decide(body: str, skip_reason: str = "") -> tuple[bool, str]:
    text = section(body)
    found = RESULT.search(text) if text is not None else None
    result = found.group(1) if found else "none"
    skip = " ".join(skip_reason.split())
    if result == "Pass":
        return True, "Docs walk: Pass" + (f" (skip reason ignored: {skip})" if skip else "")
    if skip:
        return True, f"Docs walk: {result}. **Skipped by the owner, reason: {skip}**"
    why = "no `## Docs walk` section" if text is None else f"Result is {result}"
    return False, (f"Docs walk: {result}. Refused: {why}. Run the docs walk (automaton-verify-release skill), "
                   "or start the run with skip_docs_walk and a reason.")


def main() -> int:
    args = sys.argv[1:]
    if not args:
        print(__doc__, file=sys.stderr)
        return 2
    reason = args[args.index("--skip-reason") + 1] if "--skip-reason" in args else ""
    try:
        body = open(args[0], encoding="utf-8").read()
    except OSError:
        body = ""
    ok, message = decide(body, reason)
    print(message)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
