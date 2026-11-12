#!/usr/bin/env python3
"""Check a pull request against the merge gate in .agents/skills/pr-review/SKILL.md.

Usage: python3 .agents/tools/merge-gate.py <pr-number> [--repo owner/name] [--merge]

Prints one line per check and exits 0 when the pull request may be merged, 1 when it may not.
With --merge it also squash-merges the checked commit, and only when every check passes. Agents merge only
this way, never with gh pr merge directly.
Requires the gh CLI.
"""
import fnmatch
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SECURITY_MARKER = "# --- security pass ---"
# A change is low risk when every file is documentation for people: Markdown in these places and nothing else.
# Skills, AGENTS.md, templates and the guidance shipped to users' agents steer agents, so they are never low.
PEOPLE_DOCS = ("docs/", "src/Automaton.Api/Documentation/Admin/")
TRUSTED = {"OWNER", "MEMBER", "COLLABORATOR"}
CLOSING = re.compile(r"\b(close[sd]?|fix(e[sd])?|resolve[sd]?)\s+#\d+", re.I)
GREEN = {"SUCCESS", "SKIPPED", "NEUTRAL"}


def _patterns(lines) -> list[str]:
    parts = (line.split("#", 1)[0].split() for line in lines)
    return [items[0] for items in parts if items]


def owner_patterns(text: str) -> list[str]:
    """High-tier paths: everything above the security-pass marker."""
    return _patterns(text.split(SECURITY_MARKER, 1)[0].splitlines())


def security_patterns(text: str) -> list[str]:
    """Medium-tier paths whose review must include a security pass: everything below the marker."""
    parts = text.split(SECURITY_MARKER, 1)
    return _patterns(parts[1].splitlines()) if len(parts) == 2 else []


def matches(path: str, pattern: str) -> bool:
    if pattern.startswith("/"):
        pattern = pattern[1:]
        return path.startswith(pattern) if pattern.endswith("/") else fnmatch.fnmatch(path, pattern)
    if pattern.endswith("/"):
        return f"/{pattern}" in f"/{path}"
    return fnmatch.fnmatch(path, pattern) or fnmatch.fnmatch(path.rsplit("/", 1)[-1], pattern)


def is_low(path: str) -> bool:
    return path.endswith(".md") and (path == "README.md" or path.startswith(PEOPLE_DOCS))


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


def check(pr: dict, patterns: list[str], security: list[str] = ()) -> tuple[str, list[tuple[str, str, str]]]:
    """Return the tier and (result, name, detail) rows; result is PASS, FAIL or WARN."""
    rows = []
    head = pr["headRefOid"]
    paths = pr["paths"]
    labels = {item["name"] for item in pr.get("labels", [])}
    # Only people with write access review; on a public repository anyone can post a review.
    reviews = sorted((item for item in pr.get("reviews", [])
                      if item.get("authorAssociation") in TRUSTED and item.get("state") != "DISMISSED"),
                     key=lambda item: item.get("submittedAt") or "")
    review = read_review(reviews[-1]) if reviews else None

    # A tier a reviewer raised stays raised: a later, shorter review cannot lower it.
    tier = path_tier(paths, patterns)
    raised = {read_review(item)["tier"] for item in reviews}
    if "high" in raised:
        tier = "high"
    elif "medium" in raised and tier == "low":
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
    elif review["verdict"] == "changes":
        rows.append(("FAIL", "review", "the last review asks for changes"))
    else:
        # Running the gate with --merge is the approval; the review records what was checked.
        rows.append(("PASS", "review", f"reviewed at {head[:8]}"))

    needs_security = tier == "high" or any(matches(path, pattern) for path in paths for pattern in security)
    if needs_security:
        rows.append(("PASS", "security", "security pass recorded") if review and review["security"] and review["commit"] == head
                    else ("FAIL", "security", "this change needs a 'Security:' section in the review of the final commit"))

    body = pr.get("body") or ""
    missing = [part for part in ("How verified", "Risk", "How to revert")
               if not re.search(r"^\W*" + part + r"\W*:", body, re.I | re.M)]
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


def gh(*args: str) -> str:
    return subprocess.run(["gh", *args], check=True, capture_output=True, text=True, encoding="utf-8").stdout


def main() -> int:
    args = sys.argv[1:]
    repo = []
    merge = "--merge" in args
    args = [arg for arg in args if arg != "--merge"]
    if "--repo" in args:
        at = args.index("--repo")
        repo, args = args[at:at + 2], args[:at] + args[at + 2:]
    if len(args) != 1:
        print(__doc__)
        return 2
    fields = "number,headRefOid,baseRefName,body,labels,reviews,statusCheckRollup,comments,commits"
    pr = json.loads(gh("pr", "view", args[0], "--json", fields, *repo))
    slug = repo[1] if repo else "{owner}/{repo}"
    # Every changed file, with the old path of a rename: a file moved out of a protected folder still counts.
    files = gh("api", "--paginate", f"repos/{slug}/pulls/{pr['number']}/files?per_page=100",
               "-q", ".[] | .filename, (.previous_filename // empty)")
    pr["paths"] = files.splitlines()
    # The owner's paths come from the base branch, so a pull request cannot shorten its own list.
    try:
        owners = gh("api", f"repos/{slug}/contents/.github/CODEOWNERS?ref={pr['baseRefName']}",
                    "-H", "Accept: application/vnd.github.raw")
    except subprocess.CalledProcessError:
        # Fail closed: without the base branch's list every path would look medium.
        print(f"FAIL  tier: could not read .github/CODEOWNERS from {pr['baseRefName']}")
        print(f"MERGE GATE: FAIL (tier unknown, PR {args[0]})")
        return 1
    tier, rows = check(pr, owner_patterns(owners), security_patterns(owners))
    for result, name, detail in rows:
        print(f"{result}  {name}: {detail}")
    failed = any(result == "FAIL" for result, _, _ in rows)
    print(f"MERGE GATE: {'FAIL' if failed else 'PASS'} (tier {tier}, PR {args[0]})")
    if failed:
        return 1
    if merge:
        # Merge exactly the commit the gate checked, so a push after the check cannot slip in.
        subprocess.run(["gh", "pr", "merge", args[0], "--squash", "--delete-branch",
                        "--match-head-commit", pr["headRefOid"], *repo], check=True)
        print(f"MERGED PR {args[0]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
