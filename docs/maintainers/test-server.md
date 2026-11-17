# Test server

The test server is where agents release and verify. It should have production's install and config shape (adopted install,
`compose.override.yaml`, `docker-compose.prod.yml`, settings), not its data or secrets. Differences hide upgrade paths
(#1210's migration only ran on production). Keep the list of differences below; each one says why it remains.

## Differences from production

Not yet listed: the agent user cannot read `/opt/automaton-server/.env`, `release.env` or the compose files (root only).
A root session must diff them against production's layout and fill this section in (#1225).

## Throwaway Runner for the agent user

The agent user may run only `update.sh` as root, so it could not install or fully delete a Runner (the installation folder
under `/opt/automaton/self-hosted-runners/` is root-owned). `scripts/test-server-runner.sh` closes that gap with two
verbs, both limited to one installation id (`sh_` + 32 hex):

- `prepare <id>`: creates `/opt/automaton/self-hosted-runners/<id>` owned by the agent user. Then run the install
  command Studio or `automaton runners install --print-command` prints, as the agent user (it is in the `docker` group).
- `remove <id>`: removes that folder as root, after the uninstall command has removed the containers.

The script refuses unless the root-owned marker file `/opt/automaton-server/.test-server` exists, accepts only
`SUDO_USER=agent`, and `prepare` refuses when the folder already exists. Only the setup below creates the marker;
**production never gets it** (never create it there). `/opt/automaton-server` and all its parents must be root-owned and
not writable by the agent user, otherwise the agent could replace the script or the marker.

Set it up once, as root:

```bash
install -m 0755 -o root -g root scripts/test-server-runner.sh /opt/automaton-server/test-server-runner.sh
install -m 0644 -o root -g root /dev/null /opt/automaton-server/.test-server   # test server only, never production
cat > /etc/sudoers.d/automaton-agent-runner <<'SUDO'
agent ALL=(root) NOPASSWD: /usr/bin/bash /opt/automaton-server/test-server-runner.sh prepare sh_*
agent ALL=(root) NOPASSWD: /usr/bin/bash /opt/automaton-server/test-server-runner.sh remove sh_*
SUDO
chmod 0440 /etc/sudoers.d/automaton-agent-runner && visudo -cf /etc/sudoers.d/automaton-agent-runner
```

Name the Runner `e2e-<issue>-...` and delete it when the check is done.
