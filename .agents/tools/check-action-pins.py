#!/usr/bin/env python3
"""Fail when a workflow pins an action to a 40-hex sha that is not a commit.

`git ls-remote` shows the annotated tag object for a tag; the commit is the `^{}` line.
Usage: check-action-pins.py [root]   (needs GITHUB_TOKEN, or `gh` logged in)
"""
import json, os, re, subprocess, sys, urllib.error, urllib.request
from pathlib import Path

USES = re.compile(r"^\s*-?\s*uses:\s*['\"]?([^'\"\s#]+)")
PIN = re.compile(r"^([\w.-]+)/([\w.-]+)(?:/[^@]*)?@([0-9a-f]{40})$")


def find_pins(text):
    """Yield (line, owner, repo, sha) for each remote action pinned to a full sha."""
    for n, line in enumerate(text.splitlines(), 1):
        m = USES.match(line)
        if not m or m.group(1).startswith(("./", "docker://")):
            continue
        p = PIN.match(m.group(1))
        if p:
            yield n, p.group(1), p.group(2), p.group(3)


def api(path):
    token = os.environ.get("GITHUB_TOKEN")
    if token:
        req = urllib.request.Request("https://api.github.com/" + path,
            headers={"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json"})
        try:
            with urllib.request.urlopen(req) as r:
                return r.status, json.load(r)
        except urllib.error.HTTPError as e:
            return e.code, None
    r = subprocess.run(["gh", "api", path], capture_output=True, text=True)
    return (200, json.loads(r.stdout)) if r.returncode == 0 else (404, None)


def check(owner, repo, sha):
    """Return None when sha is a commit, else an error message."""
    status, _ = api(f"repos/{owner}/{repo}/commits/{sha}")
    if status == 200:
        return None
    status, tag = api(f"repos/{owner}/{repo}/git/tags/{sha}")
    if status == 200 and tag:
        obj = tag["object"]
        if obj["type"] == "commit":
            return f"{sha} is a tag object, not a commit; pin the commit {obj['sha']}"
        return f"{sha} is a tag object pointing to a {obj['type']} ({obj['sha']}); pin a commit"
    return f"{sha} is not a commit of {owner}/{repo}"


def main(root):
    root = Path(root)
    files = sorted([*root.glob(".github/workflows/*.y*ml"), *root.glob(".github/actions/**/*.y*ml")])
    bad = 0
    for f in files:
        for n, owner, repo, sha in find_pins(f.read_text()):
            err = check(owner, repo, sha)
            if err:
                bad += 1
                print(f"{f.relative_to(root)}:{n}: {owner}/{repo}: {err}")
    print("FAIL" if bad else "OK: all pinned action shas are commits")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "."))
