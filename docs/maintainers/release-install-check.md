# Release install check

Every published server release is installed on a throwaway Hetzner server and checked by script, so a broken install, Runner connect or upgrade shows before production.

- Workflow: `.github/workflows/release-install-check.yml` (starts after `Publish Automaton Server` succeeds; run it by hand with an optional version). Script: `scripts/ci/release-install-check.sh`.
- Secrets (owner creates both): `HCLOUD_TOKEN` for the separate Hetzner project `automaton-ci` only, and `CLOUDFLARE_DNS_TOKEN` with Zone.DNS edit on `automatontest.fyi` only.
- Each install uses its own sub-domain of `automatontest.fyi` (Let's Encrypt allows five certificates a week per set of names).
- Flow: create a server with the first-boot snippet from the install page (`--unattended --domain automatontest.fyi`), set the A records through Cloudflare (proxy off, TTL 60), wait for health, then check health, Studio and API (200), the admin link and password, CLI login, the `main` Runner connected, and a hello app that is created, deployed, published and answers 200. A second run installs the previous release, runs `automaton server update --version <new> --wait` and repeats the checks.
- Server types are tried in order (`cx23`, then `cpx12`), across `nbg1`, `fsn1`, `hel1`: both are workflow inputs. The summary names the type and location that ran; a failure on `cpx12` that looks like memory is reported as such.
- Cleanup: the script deletes the server, SSH key and DNS records on exit, the workflow deletes leftovers of its run in an `if: always()` step, and `release-install-sweeper.yml` deletes `automaton-ci` servers older than 3 hours every hour.
- A failing check fails this run, not the release run that already published.
