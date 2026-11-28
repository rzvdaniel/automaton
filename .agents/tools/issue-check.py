#!/usr/bin/env python3
"""Flag issues that lack the sections of the write-issue skill (the issue forms in .github/ISSUE_TEMPLATE).

Run by .github/workflows/issue-check.yml with the event payload in GITHUB_EVENT_PATH. The issue body is untrusted:
it is only parsed here, never run or put in a shell command, and the comment only repeats fixed section names.
"""
import json, os, re, sys, urllib.error, urllib.request

LABEL = "needs-shape"
MARKER = "<!-- issue-check -->"
SKIP_LABELS = {"handover", "lessons", "dependencies", "dependency-advisory", "question"}
BUG = ["Problem", "Steps", "Result", "Expected", "Done when"]
FEATURE = ["Goal", "Example", "Suggestion", "Done when"]
OPTIONAL = ["Related", "Before work"]
HEADING = re.compile(r"^[ \t]*(?:#{1,6}[ \t]*)?(?:\*\*|__)?([A-Za-z][A-Za-z ]*?)[ \t]*:?(?:\*\*|__)?[ \t]*:?[ \t]*$")
EMPTY = {"", "_no response_", "no response", "n/a", "none", "tbd", "todo"}
UNTICKED_BOX = re.compile(r"^[ \t]*(?:[-*+]|\d+\.)[ \t]*\[[ \t]*\]", re.MULTILINE)
LIST_ITEM = re.compile(r"^[ \t]*(?:[-*+]|\d+\.)[ \t]+(?:\[[ xX]\][ \t]+)?(.*)")
CHECK_RE = re.compile(
    r"\b(?:"
    r"test[- ]server|install[- ]check|ci(?:\s+run)?|workflow|github\s+actions|"
    r"throwaway|runner-02|local(?:ly)?|"
    r"unit\s+test(?:s)?|integration\s+test(?:s)?|smoke\s+test(?:s)?|"
    r"dotnet\s+test|pytest|unittest|npm\s+run|vue-tsc|"
    r"issue-check|merge[- ]gate|curl|playwright|browser|"
    r"studio|cli|api|mcp|runner|website|store"
    r")\b",
    re.IGNORECASE,
)


def sections(body):
    """Map known section name (lowercase) to its text; a section ends at the next known heading."""
    known = {n.lower() for n in BUG + FEATURE + OPTIONAL}
    out, cur = {}, None
    for line in (body or "").replace("\r\n", "\n").split("\n"):
        m = HEADING.match(line)
        if m and m.group(1).strip().lower() in known and (m.group(0).lstrip().startswith(("#", "*", "_")) or m.group(0).rstrip().endswith(":")):
            cur = m.group(1).strip().lower()
            out.setdefault(cur, [])
        elif cur is not None:
            out[cur].append(line)
    return {k: "\n".join(v).strip() for k, v in out.items()}


def kind(labels, secs):
    return "bug" if "bug" in labels or "problem" in secs else "feature"


def done_when_items(text):
    """Split Done when into logical items (bullet/numbered items with continuation lines, or one block)."""
    items = []
    for line in text.split("\n"):
        m = LIST_ITEM.match(line)
        if m:
            items.append(m.group(1).strip())
        elif line.strip():
            if items:
                items[-1] += " " + line.strip()
            else:
                items.append(line.strip())
    return [i for i in items if i]


def missing(body, labels):
    """Names of required sections that are absent, plus Done when and Before work readiness checks."""
    secs = sections(body)
    required = BUG if kind(labels, secs) == "bug" else FEATURE
    out = [n for n in required if n.lower() not in secs]
    if "Done when" not in out:
        dw = secs["done when"].strip()
        if dw.lower() in EMPTY:
            out.append("Done when (empty)")
        elif any(not CHECK_RE.search(item) for item in done_when_items(dw)):
            out.append("Done when (each item must say how and where an agent checks it, e.g. test server, install check, CI run)")
    if "before work" in secs and UNTICKED_BOX.search(secs["before work"]):
        out.append("Before work (unticked prerequisite)")
    return out


def comment_text(kind_, names):
    items = "\n".join(f"- {n}" for n in names)
    return (f"{MARKER}\nThis {kind_} issue needs attention before an agent can pick it up:\n\n{items}\n\n"
            "Fix these as the issue form and the write-issue skill describe (bold or `###` headings, a check and "
            "where to run it per Done when item, and ticked Before work boxes). "
            "This comment and the `needs-shape` label go away once the issue is ready.")


def api(method, path, data=None):
    req = urllib.request.Request("https://api.github.com/" + path, method=method,
                                 data=None if data is None else json.dumps(data).encode(),
                                 headers={"Authorization": "Bearer " + os.environ["GITHUB_TOKEN"],
                                          "Accept": "application/vnd.github+json", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req) as r:
            raw = r.read()
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        if e.code == 404 and method == "DELETE":
            return None
        raise


def find_comment(repo, number):
    page = 1
    while True:
        items = api("GET", f"repos/{repo}/issues/{number}/comments?per_page=100&page={page}")
        for c in items:
            if c["body"].startswith(MARKER) and c.get("user", {}).get("login") == "github-actions[bot]":
                return c
        if len(items) < 100:
            return None
        page += 1


def run(event, repo):
    issue = event["issue"]
    labels = {l["name"] for l in issue.get("labels", [])}
    if SKIP_LABELS & labels or "pull_request" in issue:
        return "skipped"
    number, names = issue["number"], missing(issue.get("body"), labels)
    existing = find_comment(repo, number)
    if not names:
        if LABEL in labels:
            api("DELETE", f"repos/{repo}/issues/{number}/labels/{LABEL}")
        if existing:
            api("DELETE", f"repos/{repo}/issues/comments/{existing['id']}")
        return "ok"
    text = comment_text(kind(labels, sections(issue.get("body"))), names)
    if existing:
        if existing["body"] != text:
            api("PATCH", f"repos/{repo}/issues/comments/{existing['id']}", {"body": text})
    else:
        api("POST", f"repos/{repo}/issues/{number}/comments", {"body": text})
    if LABEL not in labels:
        api("POST", f"repos/{repo}/issues/{number}/labels", {"labels": [LABEL]})
    return "flagged: " + ", ".join(names)


if __name__ == "__main__":
    with open(os.environ["GITHUB_EVENT_PATH"], encoding="utf-8") as f:
        print(run(json.load(f), os.environ["GITHUB_REPOSITORY"]))
