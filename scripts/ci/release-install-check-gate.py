#!/usr/bin/env python3
"""Verify that a server release commit is covered by a passing commit install check.

Usage:
    python3 scripts/ci/release-install-check-gate.py --sha <commit> [--skip-reason <reason>] [--repo <owner/repo>]

Exit 0 when the latest commit-build install check (mode all) on <commit>, or on the most recent
checked first-parent ancestor whose diff to <commit> touches no installer/server paths, passed;
or when a non-empty emergency --skip-reason (not a boolean placeholder) is supplied.
"""
import argparse
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request

COMMIT_ALL_TITLE = "Release install check (commit all)"
INVALID_SKIP_TOKENS = {"true", "false", "yes", "no", "1", "0"}
SERVER_EXACT_PATHS = {
    "scripts/install-automaton.sh",
    "scripts/update-automaton.sh",
    "scripts/uninstall-automaton.sh",
    ".github/workflows/server-release.yml",
    ".github/workflows/runner-image.yml",
    ".github/workflows/release-install-check.yml",
    "Automaton.Server.sln",
    "Automaton.Runner.sln",
}


def is_server_path(path: str) -> bool:
    """Return True when path can affect the server release bundle, images, installer, or CLI."""
    if path.startswith("src/"):
        return not path.startswith(("src/UnitTests/", "src/automaton-vue/"))
    if path.startswith("docker/"):
        return not path.endswith(".md")
    if path.startswith(("shared/", "system-apps/", "scripts/cli/", "scripts/ci/")):
        return True
    if path in SERVER_EXACT_PATHS:
        return True
    return path.startswith("Directory.")


def server_paths_changed(paths: list[str]) -> list[str]:
    """Filter changed file paths to those that affect a server release."""
    return [path for path in paths if is_server_path(path)]


def _is_commit_all_run(run: dict) -> bool:
    return (run.get("display_title") or run.get("name") or "") == COMMIT_ALL_TITLE


def evaluate_gate(
    target_sha: str,
    ancestors: list[str],
    runs: list[dict],
    diff_fn,
    skip_reason: str = "",
) -> tuple[bool, str]:
    """Evaluate whether target_sha may start a server release."""
    reason = skip_reason.strip()
    if reason:
        if reason.lower() in INVALID_SKIP_TOKENS:
            return False, "skip_install_check requires a non-empty emergency reason (not a boolean)."
        return True, f"Install check skipped by operator: {reason}"

    latest_by_sha: dict[str, dict] = {}
    for run in runs:
        if not _is_commit_all_run(run):
            continue
        sha = run.get("head_sha") or ""
        if not sha:
            continue
        prev = latest_by_sha.get(sha)
        key = (run.get("created_at") or "", int(run.get("id") or 0))
        prev_key = ((prev.get("created_at") or "", int(prev.get("id") or 0))) if prev else ("", -1)
        if key >= prev_key:
            latest_by_sha[sha] = run

    ordered = ancestors if ancestors and ancestors[0] == target_sha else [target_sha, *[a for a in ancestors if a != target_sha]]
    for anc in ordered:
        run = latest_by_sha.get(anc)
        if run is None:
            continue
        status = run.get("status") or ""
        conclusion = run.get("conclusion") or ""
        url = run.get("html_url") or str(run.get("id") or "")
        passed = status == "completed" and conclusion == "success"
        if anc == target_sha:
            if passed:
                return True, f"Install check passed on {target_sha[:12]} ({url})."
            return (
                False,
                f"Latest install check on {target_sha[:12]} did not pass "
                f"(status: {status or 'unknown'}, conclusion: {conclusion or 'pending'}, {url}). "
                "Run 'Release install check' (mode all) on this commit or pass skip_install_check with a reason.",
            )
        if not passed:
            return (
                False,
                f"Latest checked commit {anc[:12]} did not pass install check "
                f"(status: {status or 'unknown'}, conclusion: {conclusion or 'pending'}, {url}). "
                "Fix the check and run 'Release install check' (mode all), or pass skip_install_check with a reason.",
            )
        changed = server_paths_changed(diff_fn(anc, target_sha))
        if not changed:
            return True, f"Install check passed on {anc[:12]} ({url}); no installer/server paths changed since."
        sample = ", ".join(changed[:10]) + (" ..." if len(changed) > 10 else "")
        return (
            False,
            f"Latest passing install check was on {anc[:12]} ({url}), "
            f"but installer/server paths changed since: {sample}. "
            "Run 'Release install check' (mode all) on this commit or pass skip_install_check with a reason.",
        )

    return (
        False,
        f"No commit install check (mode all) found for {target_sha[:12]} or its recent ancestors. "
        "Run 'Release install check' (mode all) first, or pass skip_install_check with a reason for emergencies.",
    )


def _api_get(path: str) -> dict:
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN") or ""
    if token:
        req = urllib.request.Request(
            f"https://api.github.com/{path}",
            headers={"Authorization": f"Bearer {token}", "Accept": "application/vnd.github+json"},
        )
        try:
            with urllib.request.urlopen(req, timeout=30) as resp:
                return json.load(resp)
        except urllib.error.HTTPError as exc:
            raise RuntimeError(f"GitHub API {path} returned HTTP {exc.code}") from exc
    result = subprocess.run(["gh", "api", path], capture_output=True, text=True, check=True)
    return json.loads(result.stdout)


def fetch_runs(repo: str, max_pages: int = 3) -> list[dict]:
    """Fetch recent runs of release-install-check.yml from GitHub."""
    runs: list[dict] = []
    for page in range(1, max_pages + 1):
        data = _api_get(f"repos/{repo}/actions/workflows/release-install-check.yml/runs?per_page=100&page={page}")
        batch = data.get("workflow_runs") or []
        runs.extend(batch)
        if len(batch) < 100:
            break
    return runs


def git_ancestors(sha: str, limit: int = 100) -> list[str]:
    out = subprocess.check_output(
        ["git", "rev-list", "--first-parent", f"-n{limit}", sha], text=True
    )
    return [line.strip() for line in out.splitlines() if line.strip()]


def git_diff_names(base_sha: str, head_sha: str) -> list[str]:
    out = subprocess.check_output(["git", "diff", "--name-only", base_sha, head_sha], text=True)
    return [line.strip() for line in out.splitlines() if line.strip()]


def _write_summary(passed: bool, message: str) -> None:
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if not summary_path:
        return
    heading = "Install check gate passed" if passed else "Install check gate refused release"
    with open(summary_path, "a", encoding="utf-8") as fh:
        fh.write(f"### {heading}\n\n{message}\n")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sha", default=os.environ.get("GITHUB_SHA", ""), help="Commit SHA to check")
    parser.add_argument("--skip-reason", default="", help="Emergency bypass reason")
    parser.add_argument("--repo", default=os.environ.get("GITHUB_REPOSITORY", ""), help="owner/repo")
    args = parser.parse_args(argv)

    if not args.sha:
        print("::error::--sha or GITHUB_SHA is required.", file=sys.stderr)
        return 2
    if not args.skip_reason.strip() and not args.repo:
        print("::error::--repo or GITHUB_REPOSITORY is required.", file=sys.stderr)
        return 2

    ancestors = [] if args.skip_reason.strip() else git_ancestors(args.sha)
    runs = [] if args.skip_reason.strip() else fetch_runs(args.repo)
    passed, message = evaluate_gate(args.sha, ancestors, runs, git_diff_names, args.skip_reason)
    _write_summary(passed, message)
    if passed:
        print(message)
        return 0
    print(f"::error::{message}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
