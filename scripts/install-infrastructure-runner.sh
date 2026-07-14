#!/usr/bin/env bash
set -euo pipefail

# Bootstrap a Hetzner/Debian-style server over SSH. This uses the normal
# Automaton Runner image; the server-side enrollment profile grants host
# administration capability. The enrollment token is short-lived and one-time;
# no permanent API key is passed through this installer.

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run this script as root (for example: sudo -E bash install-infrastructure-runner.sh)." >&2
  exit 1
fi

AUTOMATON_SERVER_URL="${AUTOMATON_SERVER_URL:-}"
AUTOMATON_ENROLLMENT_TOKEN="${AUTOMATON_ENROLLMENT_TOKEN:-}"
AUTOMATON_RUNNER_IMAGE="${AUTOMATON_RUNNER_IMAGE:-automaton-runner:latest}"
AUTOMATON_RUNNER_NAME="${AUTOMATON_RUNNER_NAME:-}"
AUTOMATON_CONTAINER_NAME="${AUTOMATON_CONTAINER_NAME:-automaton-infrastructure-runner}"
AUTOMATON_DATA_ROOT="${AUTOMATON_DATA_ROOT:-/var/lib/automaton/infrastructure-runner}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --server-url)
      AUTOMATON_SERVER_URL="${2:-}"
      shift 2
      ;;
    --enrollment-token)
      AUTOMATON_ENROLLMENT_TOKEN="${2:-}"
      shift 2
      ;;
    --runner-image)
      AUTOMATON_RUNNER_IMAGE="${2:-}"
      shift 2
      ;;
    --runner-name)
      AUTOMATON_RUNNER_NAME="${2:-}"
      shift 2
      ;;
    --container-name)
      AUTOMATON_CONTAINER_NAME="${2:-}"
      shift 2
      ;;
    --data-root)
      AUTOMATON_DATA_ROOT="${2:-}"
      shift 2
      ;;
    *)
      echo "Unknown installer argument: $1" >&2
      exit 1
      ;;
  esac
done

: "${AUTOMATON_SERVER_URL:?Set --server-url or AUTOMATON_SERVER_URL to the Automaton API URL}"
: "${AUTOMATON_ENROLLMENT_TOKEN:?Set --enrollment-token or AUTOMATON_ENROLLMENT_TOKEN to a one-time enrollment token}"
: "${AUTOMATON_RUNNER_IMAGE:?Set --runner-image or AUTOMATON_RUNNER_IMAGE to the published Runner image}"

if ! command -v docker >/dev/null 2>&1; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get update
  apt-get install -y docker.io
fi

systemctl enable --now docker
mkdir -p "$AUTOMATON_DATA_ROOT/config" "$AUTOMATON_DATA_ROOT/workspace" "$AUTOMATON_DATA_ROOT/logs"

docker pull "$AUTOMATON_RUNNER_IMAGE"
docker rm -f "$AUTOMATON_CONTAINER_NAME" >/dev/null 2>&1 || true

docker_args=(
  run -d
  --name "$AUTOMATON_CONTAINER_NAME"
  --restart unless-stopped
  --privileged
  --user 0
  --pid host
  --network host
  -v /var/run/docker.sock:/var/run/docker.sock
  -v /:/host
  -v "$AUTOMATON_DATA_ROOT/config:/app/config"
  -v "$AUTOMATON_DATA_ROOT/workspace:/automaton/workspace"
  -v "$AUTOMATON_DATA_ROOT/logs:/app/logs"
  -e "AUTOMATON_SERVER_URL=$AUTOMATON_SERVER_URL"
  -e "AUTOMATON_ENROLLMENT_TOKEN=$AUTOMATON_ENROLLMENT_TOKEN"
)

if [[ -n "$AUTOMATON_RUNNER_NAME" ]]; then
  docker_args+=( -e "AUTOMATON_RUNNER_NAME=$AUTOMATON_RUNNER_NAME" )
fi

docker_args+=( "$AUTOMATON_RUNNER_IMAGE" )
docker "${docker_args[@]}"

echo "Infrastructure Runner enrollment started: $AUTOMATON_CONTAINER_NAME"
echo "The Runner will exchange the one-time token and persist its durable credential in: $AUTOMATON_DATA_ROOT/config"

for attempt in $(seq 1 30); do
  if [[ "$(docker inspect --format '{{.State.Running}}' "$AUTOMATON_CONTAINER_NAME" 2>/dev/null || true)" != "true" ]]; then
    echo "Infrastructure Runner stopped before enrollment completed." >&2
    docker logs --tail 80 "$AUTOMATON_CONTAINER_NAME" >&2 || true
    exit 1
  fi

  if docker logs "$AUTOMATON_CONTAINER_NAME" 2>&1 | grep -q "Runner enrolled successfully"; then
    echo "Infrastructure Runner enrolled successfully and is starting its Runner connection."
    exit 0
  fi

  sleep 2
done

echo "Infrastructure Runner is still starting. Check logs with: docker logs -f $AUTOMATON_CONTAINER_NAME"
