# Release install check

Every published server release is installed on a throwaway Hetzner server and checked by script, so a broken install, Runner connect or upgrade shows before production.

- Workflow: `.github/workflows/release-install-check.yml` (starts after `Publish Automaton Server` succeeds; run it by hand with an optional version). Script: `scripts/ci/release-install-check.sh`.
- Secrets (owner creates both): `HCLOUD_TOKEN` for the separate Hetzner project `automaton-ci` only, and `CLOUDFLARE_DNS_TOKEN` with Zone.DNS edit on `automatontest.fyi` only.
- Each install uses its own sub-domain of `automatontest.fyi` (Let's Encrypt allows five certificates a week per set of names).
- Flow: create a server with the first-boot snippet from the install page (`--unattended --domain automatontest.fyi`), set the A records through Cloudflare (proxy off, TTL 60), wait for health, then check health, Studio and API (200), the admin link and password, CLI login, the `main` Runner connected, and a hello app that is created, deployed, published and answers 200. A second run installs the previous release, runs `automaton server update --version <new> --wait` and repeats the checks.
- Server types are tried in order (`cx23`, then `cpx12`), across `nbg1`, `fsn1`, `hel1`: both are workflow inputs. The summary names the type and location that ran; a failure on `cpx12` that looks like memory is reported as such.
- Cleanup: the script deletes the server, SSH key and DNS records on exit, the workflow deletes leftovers of its run in an `if: always()` step. Check Hetzner for leftover `automaton-ci` servers after a cancelled run.
- A failing check fails this run, not the release run that already published.
- External scan: at the end of each mode, while the server still runs, `scripts/ci/release-scan.sh` runs the OWASP ZAP baseline (passive, unauthenticated) from the runner against Studio, the API health URL, the store and the published hello app, and nmap against all ports. `scripts/ci/scan-report.py` fails the run on high findings, missing security headers (CSP, clickjacking, HSTS, nosniff) and open ports other than 22, 80 and 443; other medium findings are listed in the run summary. Accepted findings go in `scripts/ci/scan-allowlist.txt` with a reason each. Authenticated deep scans are out of scope.
