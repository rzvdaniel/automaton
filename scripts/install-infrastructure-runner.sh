#!/usr/bin/env bash
set -euo pipefail

# Bootstrap a Hetzner/Debian-style server over SSH. This uses the normal
# Automaton Runner image; the installation profile grants host administration
# capability. Keep the API key in the shell environment or a protected secret
# store and never commit it to this repository.

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run this script as root (for example: sudo -E bash install-infrastructure-runner.sh)." >&2
  exit 1
fi

: "${AUTOMATON_SERVER_URL:?Set AUTOMATON_SERVER_URL to the Automaton API URL}"
: "${AUTOMATON_API_KEY:?Set AUTOMATON_API_KEY to a protected runner key}"
: "${AUTOMATON_RUNNER_IMAGE:?Set AUTOMATON_RUNNER_IMAGE to the published Runner image}"
: "${AUTOMATON_RUNNER_NAME:?Set AUTOMATON_RUNNER_NAME to the Runner name}"
: "${AUTOMATON_RUNNER_HOST_ID:?Set AUTOMATON_RUNNER_HOST_ID to the Runner Host record ID}"

AUTOMATON_CONTAINER_NAME="${AUTOMATON_CONTAINER_NAME:-automaton-infrastructure-runner}"
AUTOMATON_DATA_ROOT="${AUTOMATON_DATA_ROOT:-/var/lib/automaton/infrastructure-runner}"
AUTOMATON_RUNNER_CAPABILITIES="${AUTOMATON_RUNNER_CAPABILITIES:-ApplicationHosting,WorkflowExecution,DockerManagement,HostExecution,HostedRunnerProvisioning,RouteManagement,CertificateManagement}"

if ! command -v docker >/dev/null 2>&1; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get update
  apt-get install -y docker.io
fi

systemctl enable --now docker
mkdir -p "$AUTOMATON_DATA_ROOT/config" "$AUTOMATON_DATA_ROOT/workspace" "$AUTOMATON_DATA_ROOT/logs"

docker pull "$AUTOMATON_RUNNER_IMAGE"
docker rm -f "$AUTOMATON_CONTAINER_NAME" >/dev/null 2>&1 || true

docker run -d \
  --name "$AUTOMATON_CONTAINER_NAME" \
  --restart unless-stopped \
  --privileged \
  --user 0 \
  --pid host \
  --network host \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v /:/host \
  -v "$AUTOMATON_DATA_ROOT/config:/home/automaton/.config/AutomatonRunner" \
  -v "$AUTOMATON_DATA_ROOT/workspace:/automaton/workspace" \
  -v "$AUTOMATON_DATA_ROOT/logs:/app/logs" \
  -e "AUTOMATON_SERVER_URL=$AUTOMATON_SERVER_URL" \
  -e "AUTOMATON_API_KEY=$AUTOMATON_API_KEY" \
  -e "AUTOMATON_RUNNER_NAME=$AUTOMATON_RUNNER_NAME" \
  -e "AUTOMATON_RUNNER_PURPOSE=Infrastructure" \
  -e "AUTOMATON_RUNNER_CAPABILITIES=$AUTOMATON_RUNNER_CAPABILITIES" \
  -e "AUTOMATON_RUNNER_HOST_ID=$AUTOMATON_RUNNER_HOST_ID" \
  "$AUTOMATON_RUNNER_IMAGE"

echo "Infrastructure Runner started: $AUTOMATON_CONTAINER_NAME"
