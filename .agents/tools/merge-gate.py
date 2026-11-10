#!/usr/bin/env python3
"""Check a pull request against the merge gate in .agents/skills/pr-review/SKILL.md.

Usage: python3 .agents/tools/merge-gate.py <pr-number> [--repo owner/name]

Read-only. Prints one line per check and exits 0 when the pull request may be merged, 1 when it may not.
Requires the gh CLI.
"""
import fnmatch
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OWNER_LABEL = "owner-approved"
# A change is low risk when every file is documentation that does not steer agents.
AGENT_DOCS = ("AGENTS.md", "CLAUDE.md", "REVIEW.md")
CLOSING = re.compile(r"\b(close[sd]?|fix(e[sd])?|resolve[sd]?)\s+#\d+", re.I)
GREEN = {"SUCCESS", "SKIPPED", "NEUTRAL"}


def owner_patterns(text: str) -> list[str]:
    lines = (line.split("#", 1)[0].split() for line in text.splitlines())
    return [parts[0] for parts in lines if parts]


def matches(path: str, pattern: str) -> bool:
    if pattern.startswith("/"):
        pattern = pattern[1:]
        return path.startswith(pattern) if pattern.endswith("/") else fnmatch.fnmatch(path, pattern)
    if pattern.endswith("/"):
        return f"/{pattern}" in f"/{path}"
    return fnmatch.fnmatch(path, pattern) or fnmatch.fnmatch(path.rsplit("/", 1)[-1], pattern)


def is_low(path: str) -> bool:
    if path in AGENT_DOCS or path.startswith(".agents/"):
        return False
    return path.endswith(".md") or path.startswith("docs/")


def path_tier(paths: list[str], patterns: list[str]) -> str:
    if any(matches(path, pattern) for path in paths for pattern in patterns):
        return "high"
    return "low" if paths and all(is_low(path) for path in paths) else "medium"


def read_review(review: dict) -> dict:
    """The verdict, the tier the reviewer chose and whether a security pass is recorded."""
    body = review.get("body") or ""
    first = next((line.strip().strip("*# ").upper() for line in body.splitlines() if line.strip()), "")
    state = review.get("state", "")
    if state == "CHANGES_REQUESTED" or first.startswith("CHANGES NEEDED"):
        verdict = "changes"
    elif state == "APPROVED" or first.startswith("APPROVE"):
        verdict = "approve"
    else:
        verdict = "none"
    tier = re.search(r"^\W*Tier\W+(low|medium|high)\b", body, re.I | re.M)
    return {
        "verdict": verdict,
        "tier": tier.group(1).lower() if tier else None,
        "security": bool(re.search(r"^\W*Security\W*:", body, re.I | re.M)),
        "commit": (review.get("commit") or {}).get("oid"),
    }


def check(pr: dict, patterns: list[str]) -> tuple[str, list[tuple[str, str, str]]]:
    """Return the tier and (result, name, detail) rows; result is PASS, FAIL or WARN."""
    rows = []
    head = pr["headRefOid"]
    paths = [item["path"] for item in pr.get("files", [])]
    labels = {item["name"] for item in pr.get("labels", [])}
    reviews = sorted(pr.get("reviews", []), key=lambda item: item.get("submittedAt") or "")
    review = read_review(reviews[-1]) if reviews else None

    tier = path_tier(paths, patterns)
    if review and review["tier"] == "high":
        tier = "high"
    elif review and review["tier"] == "medium" and tier == "low":
        tier = "medium"

    checks = pr.get("statusCheckRollup", [])
    not_green = [item.get("name") or item.get("context") or "?" for item in checks
                 if (item.get("conclusion") or item.get("state") or "PENDING") not in GREEN]
    if not checks:
        rows.append(("FAIL", "ci", "no checks on the final commit yet; an empty list is not a pass"))
    elif not_green:
        rows.append(("FAIL", "ci", "not green: " + ", ".join(not_green)))
    else:
        rows.append(("PASS", "ci", f"{len(checks)} checks green"))

    if tier == "low" and not review:
        rows.append(("PASS", "review", "not required for a low-risk change"))
    elif not review:
        rows.append(("FAIL", "review", "no formal review (gh pr review)"))
    elif review["commit"] != head:
        rows.append(("FAIL", "review", f"the last review covers {str(review['commit'])[:8]}, the final commit is {head[:8]}"))
    elif review["verdict"] != "approve":
        rows.append(("FAIL", "review", "the last review does not approve" if review["verdict"] == "changes"
                     else "the last review does not start with APPROVE or CHANGES NEEDED"))
    else:
        rows.append(("PASS", "review", f"approved at {head[:8]}"))

    if tier == "high":
        rows.append(("PASS", "security", "security pass recorded") if review and review["security"] and review["commit"] == head
                    else ("FAIL", "security", "a high-risk change needs a 'Security:' section in the review of the final commit"))
        rows.append(("PASS", "owner", f"label {OWNER_LABEL}") if OWNER_LABEL in labels
                    else ("FAIL", "owner", f"a high-risk change needs the owner's approval (label {OWNER_LABEL})"))

    body = pr.get("body") or ""
    missing = [part for part in ("How verified", "Risk", "How to revert") if part.lower() not in body.lower()]
    if CLOSING.search(body):
        rows.append(("FAIL", "description", "says Closes/Fixes #N; an issue closes only when it is live"))
    elif missing:
        rows.append(("FAIL", "description", "missing: " + ", ".join(missing)))
    else:
        rows.append(("PASS", "description", "how verified, risk and how to revert are there"))

    code = [path for path in paths if path.startswith("src/") and not path.endswith(".md")]
    tests = [path for path in code if "/UnitTests/" in path or re.search(r"\.(test|spec)\.[jt]s$", path)]
    if code and not tests:
        rows.append(("WARN", "tests", "code changed but no test file is in the diff; the review must say why"))

    pushed = max((item.get("committedDate") or "" for item in pr.get("commits", [])), default="")
    late = [item for item in pr.get("comments", []) if (item.get("createdAt") or "") > pushed]
    if late:
        rows.append(("WARN", "comments", f"{len(late)} comment(s) newer than the last commit; read them before merging"))
    return tier, rows


def main() -> int:
    args = sys.argv[1:]
    repo = []
    if "--repo" in args:
        at = args.index("--repo")
        repo, args = args[at:at + 2], args[:at] + args[at + 2:]
    if len(args) != 1:
        print(__doc__)
        return 2
    fields = "headRefOid,body,labels,files,reviews,statusCheckRollup,comments,commits"
    out = subprocess.run(["gh", "pr", "view", args[0], "--json", fields, *repo],
                         check=True, capture_output=True, text=True, encoding="utf-8").stdout
    codeowners = ROOT / ".github" / "CODEOWNERS"
    patterns = owner_patterns(codeowners.read_text(encoding="utf-8")) if codeowners.exists() else []
    tier, rows = check(json.loads(out), patterns)
    for result, name, detail in rows:
        print(f"{result}  {name}: {detail}")
    failed = any(result == "FAIL" for result, _, _ in rows)
    print(f"MERGE GATE: {'FAIL' if failed else 'PASS'} (tier {tier}, PR {args[0]})")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
