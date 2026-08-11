#!/usr/bin/env bash
set -euo pipefail

[[ "${EUID}" -eq 0 ]] || { echo "Run as root." >&2; exit 2; }
[[ $# -ge 2 ]] || { echo "Usage: $0 <archive.tar.gz> <version> [--activate]" >&2; exit 2; }
ARCHIVE="$(readlink -f "$1")"
VERSION="$2"
ACTIVATE="${3:-}"
[[ "$VERSION" =~ ^[0-9A-Za-z][0-9A-Za-z._-]*$ ]] || { echo "Invalid version." >&2; exit 2; }
for command in dotnet git codex systemctl python3 tar sha256sum setsid; do command -v "$command" >/dev/null || { echo "Missing prerequisite: $command" >&2; exit 3; }; done
test -f "$ARCHIVE.sha256" || { echo "Missing checksum sidecar." >&2; exit 3; }
(cd "$(dirname "$ARCHIVE")" && sha256sum -c "$(basename "$ARCHIVE").sha256")
if tar -tzf "$ARCHIVE" | grep -Eq '(^/|(^|/)\.\.(/|$))'; then echo "Archive contains unsafe paths." >&2; exit 4; fi

id automaton-agent >/dev/null 2>&1 || useradd --system --home-dir /var/lib/automaton-agent-orchestrator/home --shell /usr/sbin/nologin automaton-agent
install -d -o root -g root -m 0755 /opt/automaton-agent-orchestrator/releases
install -d -o root -g automaton-agent -m 0750 /etc/automaton-agent-orchestrator
install -d -o automaton-agent -g automaton-agent -m 0750 /var/lib/automaton-agent-orchestrator/{workspaces,repository,home,codex}
RELEASE="/opt/automaton-agent-orchestrator/releases/$VERSION"
test ! -e "$RELEASE" || { echo "Release already exists; refusing overwrite: $RELEASE" >&2; exit 5; }
install -d -o root -g root -m 0755 "$RELEASE"
tar -xzf "$ARCHIVE" -C "$RELEASE"
test -x "$RELEASE/Automaton.AgentOrchestrator" || { echo "Release executable is missing." >&2; exit 5; }
install -o root -g root -m 0644 "$(dirname "$0")/../deploy/agent-orchestrator/automaton-agent-orchestrator.service" /etc/systemd/system/automaton-agent-orchestrator.service
test -f /etc/automaton-agent-orchestrator/WORKFLOW.md || { echo "Install trusted WORKFLOW.md before activation." >&2; exit 6; }
test -f /etc/automaton-agent-orchestrator/secrets.env || { echo "Install secrets.env before activation." >&2; exit 6; }
chown automaton-agent:automaton-agent /etc/automaton-agent-orchestrator/secrets.env
chmod 0600 /etc/automaton-agent-orchestrator/secrets.env
ln -sfn "$RELEASE" /opt/automaton-agent-orchestrator/.current-new
mv -Tf /opt/automaton-agent-orchestrator/.current-new /opt/automaton-agent-orchestrator/current
systemctl daemon-reload
if [[ "$ACTIVATE" == "--activate" ]]; then
  systemctl enable --now automaton-agent-orchestrator.service
  "$(dirname "$0")/agent-orchestrator-health.sh"
else
  echo "Installed $VERSION without starting it. Use systemctl start after validation."
fi
