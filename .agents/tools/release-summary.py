#!/usr/bin/env python3
"""Create/pin the "Release X.Y.Z" issue and post one verification summary comment (issue #1889).

Usage:
  python3 .agents/tools/release-summary.py --version <X.Y.Z|server-vX.Y.Z>
      [--repo owner/name] [--board owner/num] [--verify-outcome success|failure|...]
      [--run-url URL] [--dry-run] [--summary-file PATH]
"""
import argparse
import importlib.util
import json
import os
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
_spec = importlib.util.spec_from_file_location("auto_release", HERE / "auto-release.py")
ar = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ar)

VERSION_RE = re.compile(r"^\d+\.\d+\.\d+(?:[.-][A-Za-z0-9.-]+)?$")
MARKER_PREFIX = "<!-- release-summary:"
PINNED_QUERY = """query($o:String!,$r:String!){repository(owner:$o,name:$r){pinnedIssues(first:10){nodes{issue{number title state}}}}}"""


TRUSTED_LOGINS = {"rzvdaniel", "rzvdaniel-bot"}
TRUSTED_ASSOCIATIONS = {"OWNER", "MEMBER", "COLLABORATOR"}


def _is_trusted_author(login: str | None, association: str | None) -> bool:
    return (login or "") in TRUSTED_LOGINS or (association or "") in TRUSTED_ASSOCIATIONS


class SummaryClient(ar.GitHubClient):
    """GitHub CLI client extended with issue, comment, artifact, and pin operations."""

    def api_method(self, method: str, path: str, data: dict | None = None) -> object:
        cmd = [self.gh_bin, "api", "-X", method, path]
        if data is not None:
            proc = subprocess.run(
                [*cmd, "--input", "-"],
                input=json.dumps(data),
                capture_output=True,
                text=True,
                encoding="utf-8",
            )
        else:
            proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8")
        if proc.returncode != 0:
            detail = (proc.stderr or proc.stdout or "").strip()[:300]
            raise RuntimeError(f"gh api {method} {path} failed: {detail}")
        return json.loads(proc.stdout) if proc.stdout.strip() else None

    def graphql(self, query: str, **variables: str) -> dict:
        cmd = [self.gh_bin, "api", "graphql", "-f", f"query={query}"]
        for k, v in variables.items():
            cmd += ["-f", f"{k}={v}"]
        return json.loads(ar.run_cmd(cmd) or "{}")

    def search_merged_prs(self, repo: str, number: int) -> list[dict]:
        q = f'repo:{repo} is:pr is:merged "#{number}"'
        resp = self.api("search/issues?q=" + _url_quote(q) + "&per_page=50")
        out = []
        for item in (resp or {}).get("items", []):
            pr = self.api(f"repos/{repo}/pulls/{item['number']}")
            if pr:
                out.append(pr)
        return out

    def find_issue_by_title(self, repo: str, title: str) -> dict | None:
        out = ar.run_cmd([
            self.gh_bin, "issue", "list", "-R", repo, "--state", "all",
            "--search", f'"{title}" in:title', "--limit", "20",
            "--json", "number,title,state,url,author,authorAssociation",
        ])
        for item in json.loads(out or "[]"):
            if item.get("title") != title:
                continue
            author_login = (item.get("author") or {}).get("login")
            if _is_trusted_author(author_login, item.get("authorAssociation")):
                return item
        return None

    def create_issue(self, repo: str, title: str, body: str) -> dict:
        url = ar.run_cmd([self.gh_bin, "issue", "create", "-R", repo, "--title", title, "--body", body]).strip()
        number = int(url.rstrip("/").rsplit("/", 1)[-1])
        return {"number": number, "title": title, "state": "OPEN", "url": url}

    def reopen_issue(self, repo: str, number: int) -> None:
        ar.run_cmd([self.gh_bin, "issue", "reopen", str(number), "-R", repo])

    def pin_issue(self, repo: str, number: int) -> None:
        ar.run_cmd([self.gh_bin, "issue", "pin", str(number), "-R", repo])

    def unpin_issue(self, repo: str, number: int) -> None:
        ar.run_cmd([self.gh_bin, "issue", "unpin", str(number), "-R", repo])

    def list_comments(self, repo: str, number: int) -> list[dict]:
        return self.api(f"repos/{repo}/issues/{number}/comments?per_page=100") or []

    def post_comment(self, repo: str, number: int, body: str) -> dict:
        return self.api_method("POST", f"repos/{repo}/issues/{number}/comments", {"body": body})

    def update_comment(self, repo: str, comment_id: int, body: str) -> dict:
        return self.api_method("PATCH", f"repos/{repo}/issues/comments/{comment_id}", {"body": body})


def _url_quote(value: str) -> str:
    from urllib.parse import quote
    return quote(value, safe="")


def normalize_version(raw: str) -> tuple[str, str]:
    """Return (clean_version, release_tag) from 'X.Y.Z' or 'server-vX.Y.Z'."""
    clean = (raw or "").strip()
    if clean.startswith("server-v"):
        clean = clean[len("server-v"):]
    if not VERSION_RE.match(clean):
        raise ValueError(f"Unsupported version format: {raw!r}")
    return clean, f"server-v{clean}"


def resolve_tag_sha(client: SummaryClient, repo: str, tag: str) -> str | None:
    """Resolve tag to its commit SHA, dereferencing annotated tags when needed."""
    try:
        ref = client.api(f"repos/{repo}/git/ref/tags/{tag}")
    except RuntimeError:
        return None
    obj = (ref or {}).get("object") or {}
    sha = obj.get("sha")
    if obj.get("type") == "tag" and sha:
        tag_obj = client.api(f"repos/{repo}/git/tags/{sha}")
        return ((tag_obj or {}).get("object") or {}).get("sha")
    return sha


def is_contained(client: SummaryClient, repo: str, tag: str, sha: str, cache: dict[str, bool | None]) -> bool | None:
    """Return True if commit sha is an ancestor of (or equal to) tag."""
    if sha in cache:
        return cache[sha]
    try:
        status = (client.api(f"repos/{repo}/compare/{tag}...{sha}?per_page=1") or {}).get("status")
        res = status in ("behind", "identical")
    except RuntimeError:
        res = None
    cache[sha] = res
    return res


def has_proof_artifact(client: SummaryClient, repo: str, number: int) -> bool:
    """Return True if the issue has a `proof` artifact attached."""
    try:
        items = client.api(f"repos/{repo}/issues/{number}/artifacts?per_page=20") or []
    except RuntimeError:
        return False
    return any(isinstance(i, dict) and i.get("name") == "proof" for i in items)


def inspect_install_check(client: SummaryClient, repo: str, tag_sha: str | None) -> tuple[str, str] | None:
    """Return ('passed'|'failed'|'in progress', url) for release-install-check.yml on tag_sha if present."""
    if not tag_sha:
        return None
    runs = (client.api(f"repos/{repo}/actions/workflows/release-install-check.yml/runs?per_page=30") or {}).get("workflow_runs", [])
    matching = [r for r in runs if r.get("head_sha") == tag_sha]
    if not matching:
        return None
    run = matching[0]
    url = run.get("html_url") or ""
    status = (run.get("status") or "").lower()
    if status != "completed":
        return ("in progress", url)
    conclusion = (run.get("conclusion") or "").lower()
    if conclusion == "success":
        return ("passed", url)
    if conclusion == "skipped":
        return None
    return (conclusion or "failed", url)


def collect_release_status(
    client: SummaryClient,
    repo: str,
    board: str,
    version: str,
    verify_outcome: str = "success",
    run_url: str = "",
) -> dict:
    """Gather verification outcomes for all Verify cards belonging to this release."""
    clean, tag = normalize_version(version)
    tag_sha = resolve_tag_sha(client, repo, tag)
    owner, board_num = board.split("/", 1)
    items = client.project_items(owner, board_num)
    verify_items = [
        item for item in items
        if (item.get("content") or {}).get("type") == "Issue" and item.get("status") == "Verify"
    ]

    recent_prs = client.api(f"repos/{repo}/pulls?state=closed&base=main&sort=updated&direction=desc&per_page=100") or []
    prs_by_issue: dict[int, list[dict]] = {}
    for pr in recent_prs:
        if not pr.get("merged_at"):
            continue
        for num in ar.referenced_issues(pr, repo):
            prs_by_issue.setdefault(num, []).append(pr)

    verified: list[dict] = []
    verified_partial: list[dict] = []
    failed: list[dict] = []
    skipped: list[dict] = []
    contain_cache: dict[str, bool | None] = {}

    for item in verify_items:
        content = item["content"]
        item_repo = content.get("repository", "")
        num = content["number"]
        title = content.get("title", "")
        if item_repo != repo:
            skipped.append({"repo": item_repo, "number": num, "title": title, "reason": f"other repository ({item_repo})"})
            continue

        issue = client.api(f"repos/{repo}/issues/{num}") or {}
        labels = {lbl["name"] for lbl in issue.get("labels", [])}
        issue_url = issue.get("html_url") or f"https://github.com/{repo}/issues/{num}"

        candidate_prs = prs_by_issue.get(num)
        if candidate_prs is None:
            candidate_prs = [pr for pr in client.search_merged_prs(repo, num) if num in ar.referenced_issues(pr, repo)]

        if not candidate_prs:
            skipped.append({"repo": item_repo, "number": num, "title": title, "url": issue_url, "reason": "no merged PR"})
            continue

        contained_results = [
            is_contained(client, repo, tag, pr["merge_commit_sha"], contain_cache)
            for pr in candidate_prs if pr.get("merge_commit_sha")
        ]
        if not any(r is True for r in contained_results):
            reason = "compare failed" if any(r is None for r in contained_results) else f"merged after {tag}"
            skipped.append({"repo": item_repo, "number": num, "title": title, "url": issue_url, "reason": reason})
            continue

        has_proof = has_proof_artifact(client, repo, num)
        entry = {"number": num, "title": title, "url": issue_url, "has_proof": has_proof}
        if "verification-failed" in labels:
            entry["reason"] = "verification-failed"
            failed.append(entry)
        elif "verified-partial" in labels:
            verified_partial.append(entry)
        elif "verified" in labels:
            verified.append(entry)
        else:
            entry["reason"] = "unverified (no verification label)"
            failed.append(entry)

    install_check = inspect_install_check(client, repo, tag_sha)
    blockers: list[str] = []
    if verify_outcome and verify_outcome != "success":
        blockers.append(f"verification run {verify_outcome}")
    if failed:
        blockers.append(f"{len(failed)} failed")
    if verified_partial:
        blockers.append(f"{len(verified_partial)} verified-partial")
    if install_check and install_check[0] not in ("passed", "in progress"):
        blockers.append(f"install check {install_check[0]}")

    ready = not blockers
    return {
        "version": clean,
        "tag": tag,
        "ready": ready,
        "ready_reason": "" if ready else "; ".join(blockers),
        "verified": verified,
        "verified_partial": verified_partial,
        "failed": failed,
        "skipped": skipped,
        "install_check": install_check,
        "verify_outcome": verify_outcome,
        "run_url": run_url,
    }


def format_issue_line(entry: dict, include_reason: bool = False) -> str:
    proof = f" — [proof]({entry['url']})" if entry.get("has_proof") else ""
    reason = f" ({entry['reason']})" if include_reason and entry.get("reason") else ""
    return f"- [#{entry['number']}]({entry['url']}) {entry['title']}{reason}{proof}"


def render_summary_comment(summary: dict, repo: str) -> str:
    """Render the Markdown summary comment posted on the pinned Release X.Y.Z issue."""
    ver = summary["version"]
    marker = f"{MARKER_PREFIX} {ver} -->"
    deploy_line = "ready to deploy: yes" if summary["ready"] else f"ready to deploy: no ({summary['ready_reason']})"
    lines = [
        marker,
        f"## Release {ver} verification summary",
        "",
        f"**{deploy_line}**",
        "",
        f"### verified ({len(summary['verified'])})",
    ]
    lines += [format_issue_line(e) for e in summary["verified"]] or ["- none"]
    lines += ["", f"### verified-partial ({len(summary['verified_partial'])})"]
    lines += [format_issue_line(e) for e in summary["verified_partial"]] or ["- none"]
    lines += ["", f"### failed ({len(summary['failed'])})"]
    lines += [format_issue_line(e, include_reason=True) for e in summary["failed"]] or ["- none"]
    if summary["skipped"]:
        lines += ["", f"### skipped ({len(summary['skipped'])})"]
        for s in summary["skipped"]:
            prefix = f"#{s['number']}" if s["repo"] == repo else f"{s['repo']}#{s['number']}"
            lines.append(f"- {prefix} {s['title']} ({s['reason']})")
    lines += [
        "",
        f"- **Release:** [`{summary['tag']}`](https://github.com/{repo}/releases/tag/{summary['tag']})",
    ]
    if summary["install_check"]:
        ic_status, ic_url = summary["install_check"]
        ic_text = f"[{ic_status}]({ic_url})" if ic_url else ic_status
        lines.append(f"- **Install check:** {ic_text}")
    if summary["run_url"]:
        lines.append(f"- **Verify run:** [{summary['verify_outcome']}]({summary['run_url']})")
    return "\n".join(lines) + "\n"


def ensure_pinned_release_issue(client: SummaryClient, repo: str, version: str, dry_run: bool = False) -> dict:
    """Find or create 'Release X.Y.Z', reopen if closed, and pin it (unpinning older Release pins)."""
    title = f"Release {version}"
    existing = client.find_issue_by_title(repo, title)
    if existing:
        if not dry_run and existing.get("state") == "CLOSED":
            client.reopen_issue(repo, existing["number"])
        issue = existing
    else:
        if dry_run:
            return {"number": 0, "title": title, "state": "OPEN", "url": ""}
        body = (
            f"Verification status for [`server-v{version}`]"
            f"(https://github.com/{repo}/releases/tag/server-v{version}).\n\n"
            "The latest verification summary is posted below so one place says whether this release is ready to deploy.\n"
        )
        issue = client.create_issue(repo, title, body)

    if dry_run:
        return issue

    owner, name = repo.split("/", 1)
    try:
        pinned_data = client.graphql(PINNED_QUERY, o=owner, r=name)
        pinned_nodes = (
            pinned_data.get("data", {})
            .get("repository", {})
            .get("pinnedIssues", {})
            .get("nodes", [])
        )
        pinned_nums = set()
        for node in pinned_nodes:
            pi = (node or {}).get("issue") or {}
            pnum = pi.get("number")
            ptitle = pi.get("title") or ""
            if not pnum:
                continue
            pinned_nums.add(pnum)
            if pnum != issue["number"] and ptitle.startswith("Release "):
                client.unpin_issue(repo, pnum)
        if issue["number"] not in pinned_nums:
            client.pin_issue(repo, issue["number"])
    except RuntimeError as exc:
        print(f"Warning: could not update pinned issues for #{issue['number']}: {exc}", file=sys.stderr)
    return issue


def publish_summary(
    client: SummaryClient,
    repo: str,
    board: str,
    version: str,
    verify_outcome: str = "success",
    run_url: str = "",
    dry_run: bool = False,
) -> tuple[dict, dict, str]:
    """Compute summary, ensure pinned 'Release X.Y.Z' issue, and post/update its one summary comment."""
    summary = collect_release_status(client, repo, board, version, verify_outcome=verify_outcome, run_url=run_url)
    comment_body = render_summary_comment(summary, repo)
    issue = ensure_pinned_release_issue(client, repo, summary["version"], dry_run=dry_run)
    if not dry_run:
        marker = f"{MARKER_PREFIX} {summary['version']} -->"
        comments = client.list_comments(repo, issue["number"])
        existing_comment = next(
            (
                c
                for c in comments
                if (c.get("body") or "").startswith(marker)
                and _is_trusted_author((c.get("user") or {}).get("login"), c.get("author_association"))
            ),
            None,
        )
        if existing_comment:
            client.update_comment(repo, existing_comment["id"], comment_body)
        else:
            client.post_comment(repo, issue["number"], comment_body)
    return summary, issue, comment_body


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--version", required=True)
    ap.add_argument("--repo", default="rzvdaniel/automaton")
    ap.add_argument("--board", default="rzvdaniel/17")
    ap.add_argument("--verify-outcome", default="success")
    ap.add_argument("--run-url", default="")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--summary-file", default=os.environ.get("GITHUB_STEP_SUMMARY", ""))
    args = ap.parse_args(argv)

    client = SummaryClient(os.environ.get("AGENT_GH", "gh"))
    _, issue, comment_body = publish_summary(
        client,
        repo=args.repo,
        board=args.board,
        version=args.version,
        verify_outcome=args.verify_outcome,
        run_url=args.run_url,
        dry_run=args.dry_run,
    )
    header = f"Summary issue: {issue.get('url') or issue.get('title')}\n\n"
    sys.stdout.write(header + comment_body)
    if args.summary_file:
        with open(args.summary_file, "a", encoding="utf-8") as fh:
            fh.write(header + comment_body)
    return 0


if __name__ == "__main__":
    sys.exit(main())
