#!/usr/bin/env python3
"""Build every tracked C# project, collect analyzer SARIF, and gate debt rules."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
import tempfile
from collections import Counter
from pathlib import Path
from urllib.parse import unquote, urlparse


ROOT = Path(__file__).resolve().parents[2]
SOURCE_ROOT = ROOT / "src"
TARGET_RULES = {"CA1502", "CA1506", "S3776"}
BASELINE_PATH = ROOT / "scripts" / "quality" / "complexity-baseline.json"
SETTINGS_PATHS = [ROOT / ".editorconfig", ROOT / "src" / "CodeMetricsConfig.txt", ROOT / "src" / "Directory.Build.targets"]
METRIC_PATTERNS = {
    "CA1502": re.compile(r"complexity of ['\"](?P<score>\d+)['\"]", re.IGNORECASE),
    "CA1506": re.compile(r"coupled with ['\"](?P<score>\d+)['\"]", re.IGNORECASE),
    "S3776": re.compile(r"complexity from (?P<score>\d+) to", re.IGNORECASE),
}


class GateError(RuntimeError):
    pass


def tracked_projects() -> list[Path]:
    result = subprocess.run(
        ["git", "ls-files", "-z", "--", "src/**/*.csproj"],
        cwd=ROOT,
        check=True,
        capture_output=True,
    )
    projects = sorted(
        ROOT / item.decode("utf-8")
        for item in result.stdout.split(b"\0")
        if item
    )
    if not projects:
        raise GateError("No tracked C# projects were found under src/.")
    return projects


def create_quality_solution(solution: Path, projects: list[Path]) -> None:
    run_checked(
        [
            "dotnet",
            "new",
            "sln",
            "--name",
            solution.stem,
            "--output",
            str(solution.parent),
            "--format",
            "sln",
        ]
    )
    run_checked(["dotnet", "sln", str(solution), "add", *(str(path) for path in projects)])


def run_checked(command: list[str], cwd: Path = ROOT) -> None:
    result = subprocess.run(command, cwd=cwd, text=True, capture_output=True)
    if result.returncode == 0:
        return
    output = (result.stdout + result.stderr).splitlines()
    details = "\n".join(output[-100:])
    raise GateError(f"Command failed with exit code {result.returncode}: {' '.join(command)}\n{details}")


def installed_net9_sdk(required: str | None = None) -> str:
    result = subprocess.run(
        ["dotnet", "--list-sdks"], cwd=ROOT, check=True, capture_output=True, text=True
    )
    versions = [
        line.split()[0]
        for line in result.stdout.splitlines()
        if line.split() and re.fullmatch(r"9\.\d+\.\d+", line.split()[0])
    ]
    if not versions:
        raise GateError("The quality gate requires an installed stable .NET 9 SDK, as used by CI.")
    if required is not None:
        if required not in versions:
            raise GateError(f"The reviewed baseline requires .NET SDK {required}; install that exact SDK to run this gate.")
        return required
    return max(versions, key=lambda value: tuple(int(part) for part in value.split(".")))


def build_reports(solution: Path, report_dir: Path, configuration: str, sdk_version: str) -> str:
    command = [
        "dotnet",
        "build",
        str(solution),
        "--configuration",
        configuration,
        "--no-incremental",
        "--verbosity",
        "quiet",
        f"-p:AutomatonQualityReportDirectory={report_dir}",
    ]
    installed_net9_sdk(sdk_version)
    (solution.parent / "global.json").write_text(
        json.dumps({"sdk": {"version": sdk_version, "rollForward": "latestPatch"}}, indent=2) + "\n",
        encoding="utf-8",
    )
    run_checked(command, cwd=solution.parent)
    return sdk_version


def reviewed_sdk_version(create_baseline_requested: bool) -> str:
    if create_baseline_requested:
        if BASELINE_PATH.exists():
            raise GateError(f"Refusing to overwrite existing baseline {BASELINE_PATH.relative_to(ROOT)}.")
        return installed_net9_sdk()
    try:
        baseline = json.loads(BASELINE_PATH.read_text(encoding="utf-8"))
        version = baseline.get("dotnetSdkVersion")
    except (OSError, json.JSONDecodeError) as error:
        raise GateError(f"Could not read the reviewed baseline {BASELINE_PATH.relative_to(ROOT)}: {error}") from error
    if not isinstance(version, str) or not version:
        raise GateError("The reviewed baseline does not pin a .NET SDK version.")
    return installed_net9_sdk(version)


def sarif_uri_to_path(uri: str) -> str:
    parsed = urlparse(uri)
    if parsed.scheme == "file":
        path = unquote(parsed.path)
        if parsed.netloc:
            path = f"//{parsed.netloc}{path}"
        if re.match(r"^/[A-Za-z]:/", path):
            path = path[1:]
        return path.replace("\\", "/")
    return unquote(uri).replace("\\", "/")


def read_findings(report_paths: list[Path], repo_root: Path = ROOT) -> list[dict[str, object]]:
    findings: list[dict[str, object]] = []
    for report_path in report_paths:
        try:
            report = json.loads(report_path.read_text(encoding="utf-8-sig"))
        except (OSError, json.JSONDecodeError) as error:
            raise GateError(f"Could not read analyzer report {report_path}: {error}") from error

        if report.get("version") != "1.0.0" or not isinstance(report.get("runs"), list):
            raise GateError(f"Unsupported or malformed SARIF report: {report_path}")

        for run in report["runs"]:
            results = run.get("results")
            if not isinstance(results, list):
                raise GateError(f"Malformed results in SARIF report: {report_path}")
            for result in results:
                rule = result.get("ruleId")
                if rule not in TARGET_RULES:
                    continue
                locations = result.get("locations") or []
                physical = locations[0].get("resultFile", {}) if locations else {}
                uri = physical.get("uri", "<unknown>")
                region = physical.get("region", {})
                message = result.get("message", "")
                if isinstance(message, dict):
                    message = message.get("text", "")
                metric_match = METRIC_PATTERNS[rule].search(str(message))
                absolute_path = sarif_uri_to_path(uri)
                source_path = Path(absolute_path)
                try:
                    relative_path = source_path.resolve().relative_to(repo_root.resolve()).as_posix()
                except (OSError, ValueError):
                    raise GateError(f"Analyzer finding is outside the repository: {uri}")
                line = int(region.get("startLine", 0))
                start_column = int(region.get("startColumn", 0))
                end_column = int(region.get("endColumn", start_column))
                symbol, signature = get_symbol_identity(
                    source_path,
                    line,
                    start_column,
                    end_column,
                    str(message),
                )
                if metric_match is None:
                    raise GateError(f"Could not extract a numeric score for {rule} at {relative_path}:{line}.")
                findings.append(
                    {
                        "rule": rule,
                        "path": relative_path,
                        "line": line,
                        "symbol": symbol,
                        "signature": signature,
                        "identity": f"{rule}|{relative_path}|{signature}",
                        "score": int(metric_match.group("score")),
                        "message": str(message),
                        "report": str(report_path),
                    }
                )
    return findings


def read_compiler_versions(report_paths: list[Path]) -> list[str]:
    versions: set[str] = set()
    for report_path in report_paths:
        report = json.loads(report_path.read_text(encoding="utf-8-sig"))
        for run in report.get("runs", []):
            tool = run.get("tool", {})
            semantic_version = tool.get("semanticVersion") or tool.get("version")
            if not semantic_version:
                raise GateError(f"Compiler version is missing from analyzer report {report_path}.")
            versions.add(str(semantic_version))
    if len(versions) != 1:
        raise GateError("Analyzer reports were produced by inconsistent compiler versions: " + ", ".join(sorted(versions)))
    return sorted(versions)


def get_symbol_identity(
    source_path: Path,
    line: int,
    start_column: int,
    end_column: int,
    message: str,
) -> tuple[str, str]:
    try:
        lines = source_path.read_text(encoding="utf-8-sig").splitlines()
    except OSError as error:
        raise GateError(f"Could not read source for analyzer finding {source_path}:{line}: {error}") from error
    if line < 1 or line > len(lines):
        raise GateError(f"Analyzer finding has invalid source location {source_path}:{line}.")

    source_line = lines[line - 1]
    token = source_line[max(start_column - 1, 0) : max(end_column - 1, start_column)].strip()
    if not token:
        symbol_match = re.search(r"['\"](?P<symbol>[^'\"]+)['\"]", message)
        token = symbol_match.group("symbol") if symbol_match else "<unknown>"

    declaration = [source_line.strip()]
    for following_line in lines[line:line + 12]:
        if "{" in " ".join(declaration) or "=>" in " ".join(declaration):
            break
        declaration.append(following_line.strip())
    header = " ".join(" ".join(declaration).split())
    for terminator in ("{", "=>"):
        if terminator in header:
            header = header.split(terminator, 1)[0].strip()
    signature = f"{token}|{header}" if header else token
    return token, signature


def settings_digest() -> str:
    digest = hashlib.sha256()
    for path in SETTINGS_PATHS:
        if not path.is_file():
            raise GateError(f"Required analyzer configuration file is missing: {path.relative_to(ROOT)}")
        digest.update(path.relative_to(ROOT).as_posix().encode("utf-8"))
        digest.update(b"\0")
        digest.update(path.read_bytes())
        digest.update(b"\0")
    return digest.hexdigest()


def unique_findings(findings: list[dict[str, object]]) -> dict[str, dict[str, object]]:
    indexed: dict[str, dict[str, object]] = {}
    for finding in findings:
        identity = str(finding["identity"])
        if identity in indexed:
            raise GateError(
                f"Analyzer findings do not have a unique symbol identity: "
                f"{finding['path']}:{finding['line']} {finding['rule']} ({finding['signature']})."
            )
        indexed[identity] = finding
    return indexed


def create_baseline(
    findings: list[dict[str, object]], projects: list[Path], sdk_version: str, compiler_versions: list[str]
) -> None:
    if BASELINE_PATH.exists():
        raise GateError(f"Refusing to overwrite existing baseline {BASELINE_PATH.relative_to(ROOT)}.")
    baseline = {
        "schemaVersion": 1,
        "baselineCommit": subprocess.run(
            ["git", "rev-parse", "HEAD"], cwd=ROOT, check=True, capture_output=True, text=True
        ).stdout.strip(),
        "dotnetSdkVersion": sdk_version,
        "compilerVersions": compiler_versions,
        "settingsSha256": settings_digest(),
        "rules": sorted(TARGET_RULES),
        "projects": [path.relative_to(ROOT).as_posix() for path in projects],
        "findings": [
            {
                "identity": finding["identity"],
                "rule": finding["rule"],
                "path": finding["path"],
                "line": finding["line"],
                "symbol": finding["symbol"],
                "signature": finding["signature"],
                "score": finding["score"],
            }
            for finding in sorted(findings, key=lambda item: str(item["identity"]))
        ],
    }
    unique_findings(findings)
    BASELINE_PATH.parent.mkdir(parents=True, exist_ok=True)
    BASELINE_PATH.write_text(json.dumps(baseline, indent=2) + "\n", encoding="utf-8")


def compare_findings(
    baseline: dict[str, object], findings: list[dict[str, object]], sdk_version: str, compiler_versions: list[str]
) -> tuple[list[dict[str, object]], list[dict[str, object]], int]:
    if baseline.get("schemaVersion") != 1 or not isinstance(baseline.get("findings"), list):
        raise GateError("The complexity baseline has an unsupported schema.")
    if baseline.get("rules") != sorted(TARGET_RULES):
        raise GateError("The complexity baseline rule set differs from the configured gate.")
    if baseline.get("settingsSha256") != settings_digest():
        raise GateError("Analyzer settings changed; review and explicitly migrate the complexity baseline.")
    if baseline.get("dotnetSdkVersion") != sdk_version or baseline.get("compilerVersions") != compiler_versions:
        raise GateError(".NET SDK or compiler version changed; review and explicitly migrate the complexity baseline.")

    baseline_findings: dict[str, dict[str, object]] = {}
    for item in baseline["findings"]:
        identity = str(item["identity"])
        if identity in baseline_findings:
            raise GateError(f"Duplicate symbol identity in the complexity baseline: {identity}")
        baseline_findings[identity] = item
    current_findings = unique_findings(findings)
    new_findings: list[dict[str, object]] = []
    worsened_findings: list[dict[str, object]] = []
    for identity, current in current_findings.items():
        prior = baseline_findings.get(identity)
        if prior is None:
            new_findings.append(current)
        elif int(current["score"]) > int(prior["score"]):
            worsened_findings.append({**current, "previousScore": prior["score"]})
    resolved_count = len(set(baseline_findings) - set(current_findings))
    return new_findings, worsened_findings, resolved_count


def compare_to_baseline(
    findings: list[dict[str, object]], sdk_version: str, compiler_versions: list[str]
) -> tuple[list[dict[str, object]], list[dict[str, object]], int]:
    try:
        baseline = json.loads(BASELINE_PATH.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise GateError(f"Could not read the reviewed baseline {BASELINE_PATH.relative_to(ROOT)}: {error}") from error
    return compare_findings(baseline, findings, sdk_version, compiler_versions)


def verify_reports(report_dir: Path, projects: list[Path]) -> list[Path]:
    reports = sorted(report_dir.glob("*.sarif"))
    if not reports:
        raise GateError("The build completed without producing any analyzer SARIF reports.")

    missing = [
        project.relative_to(ROOT).as_posix()
        for project in projects
        if not any(path.name.startswith(f"{project.stem}-") for path in reports)
    ]
    if missing:
        raise GateError("No analyzer report was produced for: " + ", ".join(missing))
    return reports


def print_findings(findings: list[dict[str, object]], report_dir: Path) -> None:
    print(f"Analyzer reports: {report_dir}")
    counts = Counter(str(finding["rule"]) for finding in findings)
    print("Target findings: " + (", ".join(f"{rule}={counts[rule]}" for rule in sorted(TARGET_RULES)) or "none"))
    for finding in sorted(
        findings,
        key=lambda item: (str(item["path"]).casefold(), int(item["line"]), str(item["rule"])),
    ):
        print(f"{finding['rule']} {finding['path']}:{finding['line']} score={finding['score']}: {finding['message']}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", default="Debug", choices=("Debug", "Release"))
    parser.add_argument(
        "--report-only",
        action="store_true",
        help="List all target findings without applying the baseline gate.",
    )
    parser.add_argument(
        "--create-baseline",
        action="store_true",
        help="Create the initial reviewed baseline. Refuses to overwrite an existing baseline.",
    )
    parser.add_argument(
        "--require-zero",
        action="store_true",
        help="Fail while any target finding remains, even if it is in the historical baseline.",
    )
    args = parser.parse_args(argv)

    try:
        projects = tracked_projects()
        sdk_version = reviewed_sdk_version(args.create_baseline)
        report_dir = ROOT / ".artifacts" / "quality" / "complexity"
        report_dir.mkdir(parents=True, exist_ok=True)
        for old_report in report_dir.glob("*.sarif"):
            old_report.unlink()

        with tempfile.TemporaryDirectory(prefix="automaton-quality-") as temp_dir:
            temp_path = Path(temp_dir)
            solution = temp_path / "Automaton.Quality.sln"
            create_quality_solution(solution, projects)
            build_reports(solution, report_dir, args.configuration, sdk_version)
            reports = verify_reports(report_dir, projects)
            findings = read_findings(reports)
            compiler_versions = read_compiler_versions(reports)
        if args.create_baseline:
            create_baseline(findings, projects, sdk_version, compiler_versions)
            print(f"Created {BASELINE_PATH.relative_to(ROOT)} with {len(findings)} reviewed starting findings.")
            return 0
        if args.report_only:
            print_findings(findings, report_dir)
            print("REPORT ONLY: existing findings are not yet cleared.")
            return 0

        new_findings, worsened_findings, resolved_count = compare_to_baseline(
            findings, sdk_version, compiler_versions
        )
        print(f"Baseline findings resolved: {resolved_count}")
        print(f"New findings: {len(new_findings)}; worsened findings: {len(worsened_findings)}")
        for finding in new_findings:
            print(
                f"NEW {finding['rule']} {finding['path']}:{finding['line']} "
                f"score={finding['score']}: {finding['message']}"
            )
        for finding in worsened_findings:
            print(
                f"WORSE {finding['rule']} {finding['path']}:{finding['line']} "
                f"score={finding['score']} (baseline {finding['previousScore']}): {finding['message']}"
            )
        if new_findings or worsened_findings:
            print("FAIL: fix new or worsened complexity/coupling findings.", file=sys.stderr)
            return 1
        if args.require_zero and findings:
            print(
                f"FAIL: {len(findings)} historical complexity/coupling finding(s) remain.",
                file=sys.stderr,
            )
            return 1
        if findings:
            print(f"PASS: no regressions against baseline; {len(findings)} historical finding(s) remain to clean up.")
        else:
            print("PASS: all tracked C# projects are free of the targeted findings.")
        return 0
    except (GateError, subprocess.CalledProcessError) as error:
        print(f"QUALITY GATE ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
