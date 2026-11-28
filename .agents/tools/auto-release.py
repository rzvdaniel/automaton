#!/usr/bin/env python3
"""Cut a server release automatically when merged work waits in Verify and main is ready (issue #1889).

Usage:
  python3 .agents/tools/auto-release.py [--repo owner/name] [--board owner/num]
                                        [--max-per-day N] [--dry-run] [--summary-file PATH]

Exits 0 when a release is dispatched (or dry-run succeeds) and when cleanly skipped with a reason.
Exits non-zero only on invalid configuration or unexpected GitHub/command failures.
"""
import argparse
import json
import os
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

SEMVER_TAG = re.compile(r"^server-v(\d+)\.(\d+)\.(\d+)$")
ACTIVE_STATUSES = {"queued", "in_progress", "waiting", "requested", "pending"}
GREEN_CONCLUSIONS = {"success", "skipped", "neutral"}
CI_EVENTS = {"push", "dynamic"}
ISSUE_REF_KEYWORD = re.compile(r"\b(?:Refs|Part of|Closes|Fixes|Resolves)\s+", re.I)
TITLE_ISSUE_NUM = re.compile(r"\(#(\d+)\)")
PR_SUBJECT_NUM = re.compile(r"\(#(\d+)\)\s*$")
UPGRADE_WORKFLOW_HINT = re.compile(r"(upgrade.*test[-_]?server|test[-_]?server.*upgrade)", re.I)


def run_cmd(cmd: list[str]) -> str:
    """Run a command and return stdout, raising RuntimeError with concise stderr on failure."""
    proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8")
    if proc.returncode != 0:
        detail = (proc.stderr or proc.stdout or "").strip()[:300]
        raise RuntimeError(f"{' '.join(cmd[:4])} failed: {detail}")
    return proc.stdout


class GitHubClient:
    """Thin wrapper around the gh CLI so all GitHub calls can be stubbed in unit tests."""

    def __init__(self, gh_bin: str = "gh"):
        self.gh_bin = gh_bin

    def api(self, path: str) -> object:
        return json.loads(run_cmd([self.gh_bin, "api", path]) or "null")

    def project_items(self, owner: str, number: str) -> list[dict]:
        out = run_cmd([self.gh_bin, "project", "item-list", number, "--owner", owner, "--format", "json", "-L", "200"])
        return json.loads(out or "{}").get("items", [])

    def dispatch_release(self, repo: str, version: str) -> None:
        run_cmd([self.gh_bin, "workflow", "run", "server-release.yml", "-R", repo, "--ref", "main",
                 "-f", f"version={version}", "-f", "cli=auto"])


def parse_semver_tag(tag: str) -> tuple[int, int, int] | None:
    m = SEMVER_TAG.match(tag or "")
    return (int(m.group(1)), int(m.group(2)), int(m.group(3))) if m else None


def referenced_issues(pr: dict, repo: str) -> set[int]:
    """Extract issue numbers referenced by a PR's title (#N) or body (Refs/Part of/Closes/Fixes #N)."""
    found: set[int] = set()
    title = pr.get("title") or ""
    for m in TITLE_ISSUE_NUM.finditer(title):
        found.add(int(m.group(1)))
    body = pr.get("body") or ""
    escaped = re.escape(repo)
    item = rf"(?:{escaped})?#(\d+)"
    for run in re.finditer(rf"\b(?:Refs|Part of|Closes|Fixes|Resolves)\s+(?:{escaped})?#\d+(?:\s*(?:,\s*(?:and\s+|&\s*)?|and\s+|&\s*)(?:{escaped})?#\d+)*", body, re.I):
        for m in re.finditer(item, run.group(0), re.I):
            found.add(int(m.group(1)))
    return found


def local_upgrade_running(proc_root: Path = Path("/proc")) -> bool:
    """Return True if /opt/automaton-server/update.sh appears to be running locally."""
    if not proc_root.is_dir():
        return False
    my_pid = str(os.getpid())
    for entry in proc_root.iterdir():
        if not entry.name.isdigit() or entry.name == my_pid:
            continue
        try:
            cmdline = (entry / "cmdline").read_bytes().decode("utf-8", errors="replace").replace("\x00", " ")
        except OSError:
            continue
        if "/opt/automaton-server/update.sh" in cmdline or "/opt/automaton-server/update-from-request.sh" in cmdline:
            return True
    return False


def local_verify_pending(version_file: Path | None, state_file: Path | None, verify_count: int) -> str | None:
    """If the test server already holds an unverified version and Verify has items, return a skip reason."""
    if not version_file or not version_file.is_file() or verify_count <= 0:
        return None
    installed = version_file.read_text(encoding="utf-8").strip()
    if not installed:
        return None
    verified = state_file.read_text(encoding="utf-8").strip() if state_file and state_file.is_file() else ""
    if installed != verified:
        return f"test server version {installed} is waiting for Verify release to run"
    return None


def find_active_blocking_run(runs: list[dict]) -> tuple[str, str] | None:
    """Return (kind, url) when a release, Verify, or test-server upgrade workflow run is active."""
    for run in runs:
        if (run.get("status") or "").lower() not in ACTIVE_STATUSES:
            continue
        path = (run.get("path") or "").lower()
        name = (run.get("name") or "").lower()
        url = run.get("html_url") or str(run.get("id") or "")
        if path.endswith("/server-release.yml") or name == "publish automaton server":
            return ("release", url)
        if path.endswith("/verify-release.yml") or name == "verify release":
            return ("Verify run", url)
        if UPGRADE_WORKFLOW_HINT.search(path) or UPGRADE_WORKFLOW_HINT.search(name):
            return ("test-server upgrade", url)
    return None


def analyze_releases(releases: list[dict], today_utc: str) -> tuple[dict | None, int, set[str]]:
    """Return (latest_release, published_today_count, existing_tags) from GitHub releases."""
    published_today = 0
    existing_tags: set[str] = set()
    candidates: list[tuple[tuple[int, int, int], dict]] = []
    for rel in releases:
        tag = rel.get("tag_name") or ""
        if tag:
            existing_tags.add(tag)
        if rel.get("draft") or rel.get("prerelease"):
            continue
        ver = parse_semver_tag(tag)
        if not ver:
            continue
        ts = rel.get("published_at") or rel.get("created_at") or ""
        if ts[:10] == today_utc:
            published_today += 1
        candidates.append((ver, rel))
    latest = max(candidates, key=lambda pair: pair[0])[1] if candidates else None
    return latest, published_today, existing_tags


def next_patch_version(latest_tag: str, existing_tags: set[str]) -> str:
    """Compute the next unused X.Y.(Z+1) version string from latest_tag."""
    ver = parse_semver_tag(latest_tag)
    if not ver:
        raise ValueError(f"Unsupported release tag format: {latest_tag}")
    major, minor, patch = ver
    patch += 1
    while f"server-v{major}.{minor}.{patch}" in existing_tags:
        patch += 1
    return f"{major}.{minor}.{patch}"


def check_main_ci(runs: list[dict], main_sha: str) -> str | None:
    """Return a skip reason if main CI is missing, still running, or red; None when green."""
    ci_runs = [r for r in runs if (r.get("event") or "").lower() in CI_EVENTS]
    sha_runs = [r for r in ci_runs if r.get("head_sha") == main_sha]

    tests_runs = [r for r in sha_runs if (r.get("path") or "").endswith("/tests.yml") or r.get("name") == "Tests"]
    if not tests_runs:
        return f"main CI has no Tests run yet for {main_sha[:8]}"

    for run in sha_runs:
        name = run.get("name") or run.get("path") or "workflow"
        status = (run.get("status") or "").lower()
        conclusion = (run.get("conclusion") or "").lower()
        url = run.get("html_url") or str(run.get("id") or "")
        if status != "completed":
            return f"main CI still {status}: {name} ({url})"
        if conclusion not in GREEN_CONCLUSIONS:
            return f"main is red: {name} {conclusion} on {main_sha[:8]} ({url})"

    # Also verify the latest push run on main for each CI workflow (e.g., Build production images / Runner image).
    latest_by_workflow: dict[str, dict] = {}
    for run in ci_runs:
        key = run.get("path") or run.get("name") or str(run.get("workflow_id"))
        if key not in latest_by_workflow:
            latest_by_workflow[key] = run
    for run in latest_by_workflow.values():
        name = run.get("name") or run.get("path") or "workflow"
        status = (run.get("status") or "").lower()
        conclusion = (run.get("conclusion") or "").lower()
        url = run.get("html_url") or str(run.get("id") or "")
        if status != "completed":
            return f"main CI still {status}: {name} ({url})"
        if conclusion not in GREEN_CONCLUSIONS:
            return f"main is red: latest {name} {conclusion} ({url})"
    return None


def check_install_check(runs: list[dict], main_sha: str) -> str | None:
    """Return a skip reason if the release install check on main is running, missing, or failed."""
    for run in runs:
        if (run.get("status") or "").lower() in ACTIVE_STATUSES:
            url = run.get("html_url") or str(run.get("id") or "")
            return f"install check on main is in progress ({url})"
    completed = [r for r in runs if (r.get("status") or "").lower() == "completed"
                 and (r.get("conclusion") or "").lower() != "skipped"]
    for_head = [r for r in completed if r.get("head_sha") == main_sha]
    target = for_head[0] if for_head else (completed[0] if completed else None)
    if not target:
        return "no completed release install check found on main"
    conclusion = (target.get("conclusion") or "").lower()
    if conclusion != "success":
        url = target.get("html_url") or str(target.get("id") or "")
        return f"install check on main {conclusion} ({url})"
    return None


def evaluate(
    client: GitHubClient,
    repo: str,
    board: str,
    max_per_day: int,
    dry_run: bool = False,
    now_utc: datetime | None = None,
    check_local: bool = True,
    version_file: Path | None = None,
    state_file: Path | None = None,
) -> dict:
    """Evaluate readiness and optionally dispatch server-release.yml. Returns a structured result dict."""
    now = now_utc or datetime.now(timezone.utc)
    today_utc = now.strftime("%Y-%m-%d")

    if max_per_day < 0:
        raise ValueError(f"max-per-day must be >= 0, got {max_per_day}")
    if max_per_day == 0:
        return {"action": "skip", "reason": "release cadence is 0 (auto-release paused)"}

    # 1. Check in-progress runs (release, upgrade, Verify).
    active_runs: list[dict] = []
    for status in ("in_progress", "queued", "waiting"):
        resp = client.api(f"repos/{repo}/actions/runs?status={status}&per_page=100")
        active_runs.extend(resp.get("workflow_runs", []))
    blocking = find_active_blocking_run(active_runs)
    if blocking:
        kind, url = blocking
        return {"action": "skip", "reason": f"{kind} in progress ({url})"}
    if check_local and local_upgrade_running():
        return {"action": "skip", "reason": "local test-server upgrade is running"}

    # 2. Check cadence and determine latest / next patch version.
    releases = client.api(f"repos/{repo}/releases?per_page=50")
    latest, published_today, existing_tags = analyze_releases(releases, today_utc)
    if not latest:
        return {"action": "skip", "reason": "no published server-v* release found"}
    if published_today >= max_per_day:
        return {"action": "skip", "reason": f"release cadence reached ({published_today}/{max_per_day} today UTC)"}
    latest_tag = latest["tag_name"]
    major, minor, _ = parse_semver_tag(latest_tag)

    # Check git tags in this major.minor series so we never collide with an existing tag whose release was deleted.
    tags = client.api(f"repos/{repo}/git/matching-refs/tags/server-v{major}.{minor}.") or []
    for t in tags:
        ref = t.get("ref") or ""
        if ref.startswith("refs/tags/"):
            existing_tags.add(ref.removeprefix("refs/tags/"))
    next_version = next_patch_version(latest_tag, existing_tags)

    # 3. Check Verify cards on the board.
    owner, board_num = board.split("/", 1)
    items = client.project_items(owner, board_num)
    verify_issues = {
        item["content"]["number"]: item["content"].get("title", "")
        for item in items
        if (item.get("content") or {}).get("type") == "Issue"
        and item.get("status") == "Verify"
        and item["content"].get("repository") == repo
    }
    if not verify_issues:
        return {"action": "skip", "reason": f"no Verify cards in {repo}"}

    if check_local:
        pending_verify = local_verify_pending(version_file, state_file, len(verify_issues))
        if pending_verify:
            return {"action": "skip", "reason": pending_verify}

    # 4. Check commits on main since latest_tag and match Verify cards with unreleased merged PRs.
    main_commit = client.api(f"repos/{repo}/commits/main")
    main_sha = main_commit["sha"]
    compare = client.api(f"repos/{repo}/compare/{latest_tag}...{main_sha}")
    if compare.get("ahead_by", 0) <= 0:
        return {"action": "skip", "reason": f"no new commits on main since {latest_tag}"}

    ahead_commits = compare.get("commits", [])
    ahead_shas = {c["sha"] for c in ahead_commits}
    commit_pr_nums: set[int] = set()
    for c in ahead_commits:
        subj = ((c.get("commit") or {}).get("message") or "").splitlines()[0]
        m = PR_SUBJECT_NUM.search(subj)
        if m:
            commit_pr_nums.add(int(m.group(1)))

    closed_prs = client.api(f"repos/{repo}/pulls?state=closed&base=main&sort=updated&direction=desc&per_page=100")
    waiting_issues: dict[int, dict] = {}
    for pr in closed_prs:
        if not pr.get("merged_at"):
            continue
        if pr.get("merge_commit_sha") not in ahead_shas and pr.get("number") not in commit_pr_nums:
            continue
        for num in referenced_issues(pr, repo):
            if num in verify_issues and num not in waiting_issues:
                waiting_issues[num] = {"issue": num, "title": verify_issues[num], "pr": pr["number"]}

    if not waiting_issues:
        return {"action": "skip", "reason": f"no Verify cards have merged PRs outside {latest_tag}"}

    # 5. Check main CI is green.
    main_runs = client.api(f"repos/{repo}/actions/runs?branch=main&per_page=50").get("workflow_runs", [])
    ci_reason = check_main_ci(main_runs, main_sha)
    if ci_reason:
        return {"action": "skip", "reason": ci_reason}

    # 6. Check install check on main passed.
    ic_runs = client.api(
        f"repos/{repo}/actions/workflows/release-install-check.yml/runs?branch=main&per_page=20"
    ).get("workflow_runs", [])
    ic_reason = check_install_check(ic_runs, main_sha)
    if ic_reason:
        return {"action": "skip", "reason": ic_reason}

    waiting_list = [waiting_issues[k] for k in sorted(waiting_issues)]
    if dry_run:
        return {
            "action": "dry-run",
            "version": next_version,
            "previous": latest_tag,
            "main_sha": main_sha,
            "published_today": published_today,
            "max_per_day": max_per_day,
            "waiting": waiting_list,
        }

    client.dispatch_release(repo, next_version)
    return {
        "action": "released",
        "version": next_version,
        "previous": latest_tag,
        "main_sha": main_sha,
        "published_today": published_today,
        "max_per_day": max_per_day,
        "waiting": waiting_list,
    }


def format_summary(result: dict) -> str:
    """Render a concise Markdown summary for stdout and GITHUB_STEP_SUMMARY."""
    action = result["action"]
    if action == "skip":
        return f"### Auto release: skipped\n\n- **Reason:** {result['reason']}\n"
    verb = "Would dispatch (dry run)" if action == "dry-run" else "Dispatched"
    lines = [
        f"### Auto release: {verb} `server-v{result['version']}`",
        "",
        f"- **Previous release:** `{result['previous']}`",
        f"- **Main commit:** `{result['main_sha'][:8]}`",
        f"- **Cadence today:** {result['published_today']}/{result['max_per_day']} (UTC)",
        f"- **Waiting Verify issues ({len(result['waiting'])}):**",
    ]
    for w in result["waiting"]:
        lines.append(f"  - #{w['issue']} {w['title']} (PR #{w['pr']})")
    return "\n".join(lines) + "\n"


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--repo", default="rzvdaniel/automaton")
    ap.add_argument("--board", default="rzvdaniel/17")
    ap.add_argument("--max-per-day", type=int, default=None)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--summary-file", default=os.environ.get("GITHUB_STEP_SUMMARY", ""))
    ap.add_argument("--no-local-checks", action="store_true")
    args = ap.parse_args(argv)

    if args.max_per_day is not None:
        max_per_day = args.max_per_day
    else:
        raw = os.environ.get("RELEASE_CADENCE", "2").strip() or "2"
        try:
            max_per_day = int(raw)
        except ValueError:
            print(f"Invalid RELEASE_CADENCE value: {raw!r}", file=sys.stderr)
            return 2

    vf = Path(os.environ.get("VERIFY_VERSION_FILE", "/opt/automaton-server/version"))
    sf = Path(os.environ.get("VERIFY_STATE_FILE", str(Path.home() / ".local/state/automaton/verified-version")))
    client = GitHubClient(os.environ.get("AGENT_GH", "gh"))
    try:
        result = evaluate(
            client,
            repo=args.repo,
            board=args.board,
            max_per_day=max_per_day,
            dry_run=args.dry_run,
            check_local=not args.no_local_checks,
            version_file=vf,
            state_file=sf,
        )
    except Exception as exc:
        msg = f"### Auto release: error\n\n- **Error:** {exc}\n"
        print(msg, file=sys.stderr)
        if args.summary_file:
            with open(args.summary_file, "a", encoding="utf-8") as fh:
                fh.write(msg)
        return 1

    summary = format_summary(result)
    sys.stdout.write(summary)
    if args.summary_file:
        with open(args.summary_file, "a", encoding="utf-8") as fh:
            fh.write(summary)
    return 0


if __name__ == "__main__":
    sys.exit(main())
