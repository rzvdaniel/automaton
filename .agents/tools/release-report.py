#!/usr/bin/env python3
"""Render the release report for the pull requests merged between two refs.

Usage: python3 .agents/tools/release-report.py --from <previous-ref> --to <ref> [--title <text>]
           [--apps-repo owner/name] [--out FILE]

The main repository's pull requests come from the squash-merge commits in <from>..<to> ("... (#N)"). The apps
catalog repository, when given, contributes the pull requests merged between the commit dates of the two refs.
Parsing (reviews, tier, security pass) is merge-gate.py's, so the report and the gate always agree.
Requires git and the gh CLI. The output is Markdown for the GitHub Release body.
"""
import argparse
import importlib.util
import json
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
_spec = importlib.util.spec_from_file_location("merge_gate", HERE / "merge-gate.py")
gate = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(gate)

URL = re.compile(r"https?://[^\s)>\]]+")
DATA_CHANGE = re.compile(r"DropTable|DropColumn|DropIndex|DeleteData|\.Sql\(.*\b(DELETE|DROP|TRUNCATE|UPDATE)\b|AlterColumn", re.I)
INFRA = ("scripts/", "docker/", ".github/workflows/deploy-", "src/Automaton.Runner/")
AUTH = ("src/Automaton.Identity/", "src/Automaton.Api/Authentication/", "src/Automaton.Api/Authorization/")


def section(body: str, name: str) -> str:
    """The text after a 'Name:' label (bold or not) up to the next bold label or blank line."""
    match = re.search(r"^\W*" + re.escape(name) + r"\W*:\**\s*(.*?)(?=^\W*\w[\w ]*\W*:\*\*|^\s*$|\Z)", body or "", re.I | re.M | re.S)
    return match.group(1).strip() if match else ""


def what_changed(body: str) -> str:
    """The bullet list between the 'Refs' line and 'How verified'."""
    lines = []
    for line in (body or "").splitlines():
        if re.match(r"^\W*(How verified|Risk)\W*:", line, re.I):
            break
        if line.strip() and not re.match(r"^Refs #\d+", line.strip()):
            lines.append(line.rstrip())
    return "\n".join(lines).strip() or "(no description)"


def findings_count(review_body: str) -> int | None:
    """Bullets or numbered items under a 'Findings' heading; None when the review has no such list."""
    match = re.search(r"^\W*(?:findings|important|issues)\W*$(.*?)(?=^\W*(?:security|nits?|pre-existing|tier)\W*:?\W*$|\Z)",
                      review_body or "", re.I | re.M | re.S)
    if not match:
        return None
    return len(re.findall(r"^\s*(?:[-*]|\d+[.)])\s+\S", match.group(1), re.M))


def split_migration(text: str) -> tuple[str, str]:
    up = re.search(r"void Up\(.*?\)\s*\{(.*?)\n        \}", text, re.S)
    down = re.search(r"void Down\(.*?\)\s*\{(.*?)\n        \}", text, re.S)
    return (up.group(1) if up else "", down.group(1).strip() if down else "")


def migration_note(name: str, text: str) -> str:
    up, down = split_migration(text)
    drops = bool(DATA_CHANGE.search(up))
    if not down or "throw new" in down:
        restores = "Down does not undo it"
    elif drops:
        restores = "Down recreates the schema only; the dropped or rewritten data is NOT restored (restore from backup)"
    else:
        restores = "Down reverses it without losing data"
    return f"`{name}`: {'drops or rewrites data. ' if drops else ''}{restores}"


def risk_reasons(paths: list[str], tier: str, path_tier: str, notes: list[str], security: list[str], high: list[str]) -> list[str]:
    reasons = []
    if any("drops or rewrites data" in note for note in notes):
        reasons.append("data-dropping migration (rows are not restored by Down)")
    elif any("Down does not undo" in note for note in notes):
        reasons.append("irreversible migration")
    if any(path.startswith(".github/") for path in paths):
        reasons.append("workflow or CI change")
    if any(path.startswith(AUTH) or "Controllers/Account" in path or "TokenController" in path for path in paths):
        reasons.append("sign-in, access or authorization")
    if any(path.startswith(INFRA) for path in paths):
        reasons.append("infrastructure or deploy")
    if any(gate.matches(path, pattern) for path in paths for pattern in security + high):
        reasons.append("security-path change")
    if tier == "high" and path_tier != "high":
        reasons.append("reviewer raised the tier to high")
    return list(dict.fromkeys(reasons))


def pr_facts(pr: dict, files: list[str], migration_texts: dict[str, str], high: list[str], security: list[str]) -> dict:
    """Everything the report says about one pull request, from data already fetched."""
    tier, _ = gate.check(dict(pr, paths=files), high, security)
    trusted = sorted((r for r in pr.get("reviews", [])
                      if r.get("authorAssociation") in gate.TRUSTED and r.get("state") != "DISMISSED"),
                     key=lambda r: r.get("submittedAt") or "")
    last = gate.read_review(trusted[-1]) if trusted else None
    last_body = (trusted[-1].get("body") or "") if trusted else ""
    base_tier = gate.path_tier(files, high)
    notes = [migration_note(Path(p).stem, migration_texts[p]) for p in files if p in migration_texts]
    body = pr.get("body") or ""
    texts = [section(body, "How verified")] + [c.get("body") or "" for c in pr.get("comments", []) if "proof" in (c.get("body") or "").lower()]
    proofs = list(dict.fromkeys(url.rstrip(".,;") for text in texts for url in URL.findall(text)))
    return {
        "number": pr["number"], "title": pr["title"], "url": pr["url"],
        "what": what_changed(body), "tier": tier, "base_tier": base_tier,
        "tier_why": {"low": "documentation for people only", "medium": "ordinary code or guidance",
                     "high": "touches a protected path or the reviewer raised it"}[tier]
                    + (" (raised by the reviewer)" if tier != base_tier and tier == "high" else ""),
        "verdict": ("commented" if last["verdict"] == "none" else last["verdict"]) if last else "none", "findings": findings_count(last_body) if last else None,
        "security": bool(last and last["security"]), "proofs": proofs, "migrations": notes,
        "revert": section(body, "How to revert") or "revert this PR",
        "risks": risk_reasons(files, tier, base_tier, notes, security, high),
    }


def render(facts: list[dict], title: str) -> str:
    out = [f"## {title}", "", f"{len(facts)} pull request(s) merged since the previous release: "
           + (", ".join(f"#{f['number']}" for f in facts) or "none") + ".", ""]
    tiers = {t: sum(1 for f in facts if f["tier"] == t) for t in ("high", "medium", "low")}
    out += [f"Tiers: {tiers['high']} high, {tiers['medium']} medium, {tiers['low']} low. "
            f"Security pass recorded on {sum(1 for f in facts if f['security'])}.", "", "### Risky", ""]
    risky = [f for f in facts if f["risks"]]
    out += [f"- [#{f['number']}]({f['url']}) {f['title']}: " + "; ".join(f["risks"]) for f in risky] \
        or ["- Nothing flagged."]
    out += ["", "### Pull requests", ""]
    for f in facts:
        findings = "n/a" if f["findings"] is None else str(f["findings"])
        out += [f"<details><summary>#{f['number']} {f['title']} (tier {f['tier']})</summary>", "",
                f"{f['url']}", "", "**What changed**", "", f["what"], "",
                f"**Tier:** {f['tier']}, {f['tier_why']}.",
                f"**Review:** {f['verdict']}, findings: {findings}. **Security pass:** {'yes' if f['security'] else 'no'}.",
                "**How verified / proof:** " + (", ".join(f["proofs"]) if f["proofs"] else "no links in the PR"),
                "**Migrations:** " + ("; ".join(f["migrations"]) if f["migrations"] else "none"),
                f"**How to revert:** {f['revert']}", "", "</details>", ""]
    return "\n".join(out)


def run(*cmd: str) -> str:
    return subprocess.run(cmd, check=True, capture_output=True, text=True, encoding="utf-8").stdout


def fetch_pr(number: int, repo: str | None) -> dict:
    flag = ["--repo", repo] if repo else []
    pr = json.loads(run("gh", "pr", "view", str(number), "--json",
                        "number,title,url,body,labels,reviews,statusCheckRollup,comments,commits,headRefOid,baseRefName", *flag))
    return pr


def changed_files(number: int, repo: str | None) -> list[str]:
    slug = repo or "{owner}/{repo}"
    return run("gh", "api", "--paginate", f"repos/{slug}/pulls/{number}/files?per_page=100",
               "-q", ".[] | .filename, (.previous_filename // empty)").splitlines()


def codeowners(repo: str | None, ref: str) -> str:
    if repo is None:
        return run("git", "show", f"{ref}:.github/CODEOWNERS")
    return run("gh", "api", f"repos/{repo}/contents/.github/CODEOWNERS", "-H", "Accept: application/vnd.github.raw")


def collect(numbers: list[int], repo: str | None, ref: str) -> list[dict]:
    owners = codeowners(repo, ref)
    high, security = gate.owner_patterns(owners), gate.security_patterns(owners)
    facts = []
    for number in numbers:
        pr = fetch_pr(number, repo)
        files = changed_files(number, repo)
        texts = {}
        if repo is None:
            for path in files:
                if "/Migrations/" in path and path.endswith(".cs") and not path.endswith((".Designer.cs", "ModelSnapshot.cs")):
                    try:
                        texts[path] = run("git", "show", f"{ref}:{path}")
                    except subprocess.CalledProcessError:
                        pass  # the file was deleted in this release
        facts.append(pr_facts(pr, files, texts, high, security))
    return facts


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--from", dest="start", required=True)
    ap.add_argument("--to", required=True)
    ap.add_argument("--title", default="Release report")
    ap.add_argument("--apps-repo")
    ap.add_argument("--out")
    args = ap.parse_args()
    subjects = run("git", "log", "--format=%s", f"{args.start}..{args.to}").splitlines()
    numbers = sorted({int(m.group(1)) for s in subjects if (m := re.search(r"\(#(\d+)\)\s*$", s))})
    facts = collect(numbers, None, args.to)
    text = render(facts, args.title)
    if args.apps_repo:
        since, until = (run("git", "log", "-1", "--format=%cI", ref).strip() for ref in (args.start, args.to))
        listing = json.loads(run("gh", "pr", "list", "--repo", args.apps_repo, "--state", "merged", "--limit", "100",
                                 "--search", f"merged:{since}..{until}", "--json", "number"))
        apps = collect(sorted(item["number"] for item in listing), args.apps_repo, args.to)
        text += "\n" + render(apps, f"App catalog changes ({args.apps_repo})")
    if args.out:
        Path(args.out).write_text(text, encoding="utf-8")
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
