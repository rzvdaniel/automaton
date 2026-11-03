# Security policy

## Report a vulnerability

Please do not open a public issue, discussion or pull request for a security problem.

Report it privately through GitHub: open the repository's **Security** tab and choose **Report a vulnerability** ([direct link](https://github.com/rzvdaniel/automaton/security/advisories/new)). Include what is affected (server, Runner, CLI, Studio or website, and the version), how to reproduce it, and what an attacker could do.

Automaton is maintained by a small team. We aim to:

- acknowledge a report within 5 working days;
- tell you whether we confirm it, and our plan, within 14 days;
- release a fix in a new version and publish a GitHub security advisory, crediting you unless you prefer not to be named.

These are goals, not guarantees. Please give us reasonable time to release a fix before disclosing details.

## Supported versions

Automaton is in early access. Only the latest release receives security fixes:

| Component | Supported |
| --- | --- |
| Automaton Server (API, Worker, Studio, app store, Runner) | latest [`server-v*` release](https://github.com/rzvdaniel/automaton/releases) |
| CLI (`Automaton.Cli` on NuGet) | latest version |
| Older releases | not supported; update with `sudo bash /opt/automaton-server/update.sh` |

## Scope

The [security overview](docs/security/overview.md) describes the isolation model, how secrets and app permissions work, and what is out of scope (for example, mutually untrusted tenants on one machine). Reports about out-of-scope behaviour are still welcome but may be treated as hardening rather than vulnerabilities.
