#!/usr/bin/env python3
"""Close the open `verified` issues that a production release contains. Needs Python 3 and an authenticated `gh`.

Usage:
  python .agents/tools/close-released-issues.py                       # dry run (default): print what it would do
  python .agents/tools/close-released-issues.py --apply               # label `live`, comment, close, board Status Done
  python .agents/tools/close-released-issues.py --version server-v0.2.40 --website-sha <sha> --published 12,15
  python .agents/tools/close-released-issues.py --accept 40 --reason "<the owner's words>"   # verified-partial gap

Rules (anything not decidable is "needs a human", left open, and makes the exit code 1):
- Issues come from `--repo` (default rzvdaniel/automaton). Merged PRs that say `Refs #N` or `Part of #N`
  are searched in that repo and in `--apps-repo` (default rzvdaniel/automaton-apps).
- Server and CLI changes are live when the merge commit is an ancestor of (or equal to) the release tag.
- Changes only under src/automaton-vue/ or docs/ (website, docs) are live only with `--website-sha`.
- automaton-apps PRs are live only for issues listed in `--published`.
- `verified-partial` / `verification-failed` are never closed, except `verified-partial` named by `--accept N`
  together with `--reason`, which is written into the closing comment.
- Idempotent: closed issues are not listed, and the `live` label / board Status are only set when missing.
The production version defaults to https://api.automaton.monster/health (read only).
"""
import argparse, json, re, subprocess, sys, urllib.request

WEBSITE_PREFIXES = ("src/automaton-vue/", "docs/")
BOARDS = (1, 2, 3)


def gh(*args, stdin=None):
    done = subprocess.run(["gh", *args], capture_output=True, text=True, encoding="utf-8", input=stdin)
    if done.returncode != 0:
        raise RuntimeError(f"gh {' '.join(args[:3])} failed: {done.stderr.strip()[:200]}")
    return done.stdout


def api(path, *extra):
    return json.loads(gh("api", path, *extra) or "null")


def classify_files(paths, repo_is_apps=False):
    """Which release carries a PR: 'apps', 'website' (only website/docs files), 'server' (anything else), or None."""
    if repo_is_apps:
        return "apps"
    if not paths:
        return None
    return "website" if all(p.startswith(WEBSITE_PREFIXES) for p in paths) else "server"


def references(body, number, main_repo=None, qualified_only=False):
    """True when body says Refs/Part of #N. In another repo (qualified_only) it must name main_repo#N."""
    repo = re.escape(main_repo) if main_repo else r"[\w.-]+/[\w.-]+"
    prefix = rf"(?:{repo})" if qualified_only else rf"(?:{repo})?"
    item = rf"{prefix}#\d+"
    for run in re.finditer(rf"\b(?:Refs|Part of)\s+{item}(?:\s*(?:,\s*(?:and\s+|&\s*)?|and\s+|&\s*){item})*", body or "", re.I):
        if re.search(rf"{prefix}#{number}\b", run.group(0), re.I):
            return True
    return False


def decide(number, labels, prs, ctx):
    """Return (action, reason). action: close | skip | human.
    prs: list of {"number", "kind", "in_release": {"server": bool|None, "website": bool|None}}.
    ctx: {"accept": set of ints, "reason": str, "published": set of ints, "website_sha": str|None}."""
    if "verification-failed" in labels:
        return "skip", "verification-failed"
    if "verified" not in labels:
        if "verified-partial" in labels:
            if number not in ctx["accept"]:
                return "skip", "verified-partial: needs the owner's --accept"
            if not ctx["reason"]:
                return "human", "--accept needs --reason"
        else:
            return "skip", "not verified"
    if not prs:
        return "human", "no merged PR with Refs/Part of found"
    for pr in prs:
        kind = pr["kind"]
        if kind == "apps":
            if number not in ctx["published"]:
                return "human", f"apps PR #{pr['number']}: not listed in --published"
        elif kind == "website":
            if not ctx["website_sha"]:
                return "human", f"PR #{pr['number']} is website/docs only: give --website-sha"
            if pr["in_release"].get("website") is None:
                return "human", f"PR #{pr['number']}: compare failed"
            if pr["in_release"].get("website") is not True:
                return "skip", f"PR #{pr['number']} not in the deployed website commit"
        elif kind == "server":
            if pr["in_release"].get("server") is None:
                return "human", f"PR #{pr['number']}: compare failed"
            if pr["in_release"].get("server") is not True:
                return "skip", f"PR #{pr['number']} not in the release"
        else:
            return "human", f"PR #{pr['number']}: cannot classify"
    return "close", "all merged PRs are live"


def contained(repo, release, sha):
    """True when sha is an ancestor of (or equal to) release (tag or commit)."""
    try:
        status = api(f"repos/{repo}/compare/{release}...{sha}?per_page=1")["status"]
    except RuntimeError:
        return None
    return status in ("behind", "identical")


def production_version():
    with urllib.request.urlopen("https://api.automaton.monster/health", timeout=30) as response:
        return json.load(response)["version"]


def find_prs(number, repos, main_repo):
    found = {}
    for repo in repos:
        result = api("search/issues", "-X", "GET", "-f", f"q=repo:{repo} is:pr is:merged \"#{number}\"", "-f", "per_page=50")
        for item in result["items"]:
            if references(item.get("body"), number, main_repo, repo != main_repo):
                found[(repo, item["number"])] = item
    return found


def build_prs(number, repo, apps_repo, version, website_sha):
    prs = []
    for (pr_repo, pr_number), _ in find_prs(number, [repo, apps_repo], repo).items():
        pr = api(f"repos/{pr_repo}/pulls/{pr_number}")
        files = [f["filename"] for f in api(f"repos/{pr_repo}/pulls/{pr_number}/files?per_page=100")]
        kind = classify_files(files, pr_repo == apps_repo)
        sha = pr.get("merge_commit_sha")
        in_release = {}
        if kind == "server":
            in_release["server"] = contained(pr_repo, version, sha)
        elif kind == "website" and website_sha:
            in_release["website"] = contained(pr_repo, website_sha, sha)
        prs.append({"number": pr_number, "kind": kind, "in_release": in_release})
    return prs


def board_items(repo, number):
    owner, name = repo.split("/")
    query = """query($o:String!,$r:String!,$n:Int!){repository(owner:$o,name:$r){issue(number:$n){projectItems(first:10){nodes{id project{id number}}}}}}"""
    data = api("graphql", "-f", f"query={query}", "-f", f"o={owner}", "-f", f"r={name}", "-F", f"n={number}")
    return [i for i in data["data"]["repository"]["issue"]["projectItems"]["nodes"] if i["project"]["number"] in BOARDS]


def set_done(item):
    query = """query($p:ID!){node(id:$p){... on ProjectV2{field(name:"Status"){... on ProjectV2SingleSelectField{id options{id name}}}}}}"""
    field = api("graphql", "-f", f"query={query}", "-f", f"p={item['project']['id']}")["data"]["node"]["field"]
    option = next((o["id"] for o in (field or {}).get("options", []) if o["name"] == "Done"), None)
    if not option:
        return False
    mutation = """mutation($p:ID!,$i:ID!,$f:ID!,$o:String!){updateProjectV2ItemFieldValue(input:{projectId:$p,itemId:$i,fieldId:$f,value:{singleSelectOptionId:$o}}){projectV2Item{id}}}"""
    api("graphql", "-f", f"query={mutation}", "-f", f"p={item['project']['id']}", "-f", f"i={item['id']}",
        "-f", f"f={field['id']}", "-f", f"o={option}")
    return True


def apply_close(repo, number, version, labels, comment_extra):
    if "live" not in labels:
        gh("issue", "edit", str(number), "--repo", repo, "--add-label", "live")
    text = f"Live on production in {version}" + (f". {comment_extra}" if comment_extra else "")
    gh("issue", "close", str(number), "--repo", repo, "--comment", text)
    for item in board_items(repo, number):
        set_done(item)


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--apply", action="store_true", help="change things (default is a dry run)")
    p.add_argument("--repo", default="rzvdaniel/automaton")
    p.add_argument("--apps-repo", default="rzvdaniel/automaton-apps")
    p.add_argument("--version", help="release tag, e.g. server-v0.2.40 (default: production /health)")
    p.add_argument("--website-sha", help="deployed website commit")
    p.add_argument("--published", default="", help="comma-separated issue numbers of published apps")
    p.add_argument("--accept", default="", help="comma-separated verified-partial issue numbers the owner accepted")
    p.add_argument("--reason", default="", help="the owner's words for --accept")
    p.add_argument("--only", default="", help="comma-separated issue numbers to consider (default: all)")
    a = p.parse_args(argv)

    nums = lambda s: {int(x) for x in s.split(",") if x.strip()}
    ctx = {"accept": nums(a.accept), "reason": a.reason.strip(), "published": nums(a.published), "website_sha": a.website_sha}
    version = a.version or production_version()
    only = nums(a.only)

    issues = {}
    for label in ("verified", "verified-partial"):
        for i in json.loads(gh("issue", "list", "--repo", a.repo, "--state", "open", "--label", label, "--limit", "200",
                               "--json", "number,title,labels")):
            issues[i["number"]] = i
    print(f"Release: {version}   mode: {'APPLY' if a.apply else 'dry run'}")
    needs_human = False
    for number in sorted(issues):
        if only and number not in only:
            continue
        labels = {l["name"] for l in issues[number]["labels"]}
        if "verified" not in labels and number not in ctx["accept"]:
            action, reason = "skip", "verified-partial: needs the owner's --accept"
        else:
            try:
                prs = build_prs(number, a.repo, a.apps_repo, version, a.website_sha)
                action, reason = decide(number, labels, prs, ctx)
            except RuntimeError as error:
                action, reason = "human", str(error)
        if action == "human":
            needs_human = True
        done = ""
        if action == "close" and a.apply:
            extra = f"Owner accepted the gap: {ctx['reason']}" if "verified" not in labels else ""
            apply_close(a.repo, number, version, labels, extra)
            done = " [closed]"
        label = {"close": "CLOSE", "skip": "keep", "human": "needs a human"}[action]
        print(f"#{number:<5} {label:<14} {reason}{done}  | {issues[number]['title'][:50]}")
    return 1 if needs_human else 0


if __name__ == "__main__":
    sys.exit(main())
