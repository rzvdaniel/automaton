#!/usr/bin/env python3
"""Build the deploy notice: version, what shipped, check results.

Usage: deploy-notice.py VERSION RELEASE_NOTES_FILE --deploy RESULT [--report RESULT] [--run-url URL]
RESULT is a GitHub job result (success, failure, cancelled, skipped). Prints Markdown; the first line is the title.
"""
import argparse
import re
import sys


def shipped(notes):
    """The 'N pull request(s) merged' line and the list of pull requests from the release notes."""
    summary = re.search(r"^\d+ pull request\(s\) merged[^\n]*", notes, re.M)
    prs = re.findall(r"^<details><summary>(#\d+ .+?)</summary>", notes, re.M)
    return (summary.group(0) if summary else "No pull request list in the release notes."), prs


def section_value(notes, heading, key):
    m = re.search(rf"^## {re.escape(heading)}\s*\n+(?:.*\n)*?{key}: (\w+)", notes, re.M)
    return m.group(1) if m else "unavailable"


def build(version, notes, deploy, report, run_url):
    outcome = {"success": "deployed, public routes answered 200",
               "failure": "FAILED (not rolled back automatically; check the run)",
               "cancelled": "cancelled",
               "skipped": "not run (blocked before deploy, for example by the docs walk or approval)"}.get(deploy, deploy)
    summary, prs = shipped(notes)
    lines = [f"Production deploy {version}: {'OK' if deploy == 'success' else 'needs attention'}", "",
             f"**Version:** {version}", f"**Result:** {outcome}",
             f"**AI check:** {section_value(notes, 'AI check', 'Verdict')}",
             f"**Docs walk:** {section_value(notes, 'Docs walk', 'Result')}",
             f"**Release report job:** {report or 'unknown'}", "", f"**Shipped:** {summary}"]
    lines += [f"- {p}" for p in prs]
    if run_url:
        lines += ["", f"[Workflow run]({run_url})"]
    return "\n".join(lines) + "\n"


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("version")
    ap.add_argument("notes_file")
    ap.add_argument("--deploy", required=True)
    ap.add_argument("--report", default="")
    ap.add_argument("--run-url", default="")
    a = ap.parse_args(argv)
    try:
        notes = open(a.notes_file, encoding="utf-8").read()
    except OSError:
        notes = ""
    sys.stdout.write(build(a.version, notes, a.deploy, a.report, a.run_url))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
