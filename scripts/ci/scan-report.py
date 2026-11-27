#!/usr/bin/env python3
"""Turns ZAP baseline JSON reports and an nmap grepable report into a Markdown summary and an exit code.

  scan-report.py --allowlist FILE [--zap NAME=report.json ...] [--nmap nmap.gnmap] [--summary out.md]

Exit 1 when a finding is not allowlisted and is high, a missing security header, or an unexpected open port.
Allowlist lines: `<zap plugin id | port/tcp> <url substring or *> <reason>`; blank lines and `#` lines are ignored,
and a line without a reason is an error.
"""
import argparse
import json
import re
import sys

HEADER_RULES = {"10038", "10020", "10035", "10021", "10037"}  # CSP, clickjacking, HSTS, nosniff, server version leak
EXPECTED_PORTS = {22, 80, 443, 5671}  # 5671: Runner broker (amqps), documented as required
API_FAIL_RULES = {"90022"}  # Application Error Disclosure (stack trace): fails in the signed-in API scan (a report named api-*)


def load_allowlist(path):
    rules = []
    for n, line in enumerate(open(path, encoding="utf-8"), 1):
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.split(None, 2)
        if len(parts) < 3:
            sys.exit(f"{path}:{n}: needs '<id> <url substring or *> <reason>'")
        rules.append((parts[0], parts[1]))
    return rules


def allowed(rules, key, where):
    return any(k == key and (u == "*" or u in where) for k, u in rules)


def zap_findings(name, path):
    findings = []
    data = json.load(open(path, encoding="utf-8"))
    for site in data.get("site", []):
        for a in site.get("alerts", []):
            risk = int(a.get("riskcode", 0))
            pid = str(a.get("pluginid"))
            api_fail = name.startswith("api-") and pid in API_FAIL_RULES
            if risk < 2 and pid not in HEADER_RULES and not api_fail:
                continue
            uris = [i.get("uri", "") for i in a.get("instances", [])] or [site.get("@name", name)]
            level = "high" if risk >= 3 else "fail" if pid in HEADER_RULES or api_fail else "medium"
            for uri in uris[:5]:
                findings.append((level, pid, uri, a.get("alert", pid), name))
    return findings


def nmap_findings(path):
    findings = []
    for line in open(path, encoding="utf-8"):
        if not line.startswith("Host:") or "Ports:" not in line:
            continue
        for m in re.finditer(r"(\d+)/open/tcp//([^/]*)", line):
            port = int(m.group(1))
            if port not in EXPECTED_PORTS:
                findings.append(("high", f"{port}/tcp", "host", f"Unexpected open port {port} ({m.group(2) or 'unknown'})", "nmap"))
    return findings


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--allowlist", required=True)
    p.add_argument("--zap", action="append", default=[])
    p.add_argument("--nmap")
    p.add_argument("--summary")
    p.add_argument("--failing-json", help="write the failing findings (not allowlisted) as a JSON list of {level, id, where, title, source}")
    a = p.parse_args()
    rules = load_allowlist(a.allowlist)
    findings = []
    for z in a.zap:
        name, path = z.split("=", 1)
        findings += zap_findings(name, path)
    if a.nmap:
        findings += nmap_findings(a.nmap)
    out = ["### External scan", ""]
    failing = 0
    failed = []
    seen = set()
    for level, pid, where, title, src in findings:
        if (pid, where) in seen:
            continue
        seen.add((pid, where))
        if allowed(rules, pid, where):
            out.append(f"- allowed: {title} on {where} ({src})")
            continue
        out.append(f"- **{level}**: {title} [{pid}] on {where} ({src})")
        if level in ("high", "fail"):
            failing += 1
            failed.append({"level": level, "id": pid, "where": where, "title": title, "source": src})
    if not findings:
        out.append("No findings.")
    out.append("")
    out.append(f"{failing} finding(s) fail the check.")
    text = "\n".join(out)
    print(text)
    if a.summary:
        open(a.summary, "a", encoding="utf-8").write(text + "\n")
    if a.failing_json:
        json.dump(failed, open(a.failing_json, "w", encoding="utf-8"))
    return 1 if failing else 0


sys.exit(main())
