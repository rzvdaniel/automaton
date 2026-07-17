#!/usr/bin/env bash
set -euo pipefail

# Bootstrap a Docker-capable server. This uses the normal Automaton Runner
# image with Infrastructure capabilities. New installations use a short-lived,
# single-use Infrastructure Runner enrollment API key; the Runner exchanges it
# for its permanent credential and stores that credential in the config volume.

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run this script as root (for example: sudo -E bash install-infrastructure-runner.sh)." >&2
  exit 1
fi

AUTOMATON_SERVER_URL="${AUTOMATON_SERVER_URL:-}"
AUTOMATON_API_KEY="${AUTOMATON_API_KEY:-}"
AUTOMATON_ENROLLMENT_TOKEN="${AUTOMATON_ENROLLMENT_TOKEN:-}"
AUTOMATON_RUNNER_IMAGE="${AUTOMATON_RUNNER_IMAGE:-automaton-runner:latest}"
AUTOMATON_RUNNER_NAME="${AUTOMATON_RUNNER_NAME:-}"
AUTOMATON_CONTAINER_NAME="${AUTOMATON_CONTAINER_NAME:-automaton-infrastructure-runner}"
AUTOMATON_ROOT="${AUTOMATON_ROOT:-/var/lib/automaton}"
AUTOMATON_DATA_ROOT="${AUTOMATON_DATA_ROOT:-$AUTOMATON_ROOT/infrastructure-runner}"
AUTOMATON_REPLACE=false

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
    --api-key)
      AUTOMATON_API_KEY="${2:-}"
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
    --replace)
      AUTOMATON_REPLACE=true
      shift
      ;;
    *)
      echo "Unknown installer argument: $1" >&2
      exit 1
      ;;
  esac
done

: "${AUTOMATON_SERVER_URL:?Set --server-url or AUTOMATON_SERVER_URL to the Automaton API URL}"
: "${AUTOMATON_RUNNER_IMAGE:?Set --runner-image or AUTOMATON_RUNNER_IMAGE to the published Runner image}"

if [[ -z "$AUTOMATON_API_KEY" && -z "$AUTOMATON_ENROLLMENT_TOKEN" ]]; then
  echo "Set --api-key to a one-time Infrastructure Runner enrollment key." >&2
  exit 1
fi

if [[ "$AUTOMATON_REPLACE" == true && -z "$AUTOMATON_API_KEY" ]]; then
  echo "--replace requires a fresh one-time Infrastructure Runner enrollment key via --api-key." >&2
  exit 1
fi

if ! command -v docker >/dev/null 2>&1; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get update
  apt-get install -y docker.io
fi

systemctl enable --now docker
mkdir -p \
  "$AUTOMATON_DATA_ROOT/config" \
  "$AUTOMATON_DATA_ROOT/logs" \
  "$AUTOMATON_DATA_ROOT/workflows" \
  "$AUTOMATON_DATA_ROOT/artifacts" \
  "$AUTOMATON_ROOT/hosted-runners" \
  "$AUTOMATON_ROOT/gateway" \
  "$AUTOMATON_ROOT/certificates"

docker pull "$AUTOMATON_RUNNER_IMAGE"
docker rm -f "$AUTOMATON_CONTAINER_NAME" >/dev/null 2>&1 || true

if [[ "$AUTOMATON_REPLACE" == true ]]; then
  # Keep installation-id so the API reconnects the replacement to the same
  # Runner/RunnerHost, while forcing a fresh permanent credential exchange.
  rm -f \
    "$AUTOMATON_DATA_ROOT/config/api-key" \
    "$AUTOMATON_DATA_ROOT/config/runner.json" \
    "$AUTOMATON_DATA_ROOT/config/installation-profile.json"
  echo "Replacing the existing Infrastructure Runner while preserving its installation identity."
fi

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
  -v "$AUTOMATON_DATA_ROOT/workflows:/automaton/workspace"
  -v "$AUTOMATON_DATA_ROOT/artifacts:/automaton/artifacts"
  -v "$AUTOMATON_DATA_ROOT/logs:/app/logs"
  -e "AUTOMATON_SERVER_URL=$AUTOMATON_SERVER_URL"
)

if [[ -n "$AUTOMATON_API_KEY" ]]; then
  docker_args+=(
    -e "AUTOMATON_API_KEY=$AUTOMATON_API_KEY"
    -e "AUTOMATON_RUNNER_PURPOSE=Infrastructure"
  )
else
  docker_args+=( -e "AUTOMATON_ENROLLMENT_TOKEN=$AUTOMATON_ENROLLMENT_TOKEN" )
fi

if [[ -n "$AUTOMATON_RUNNER_NAME" ]]; then
  docker_args+=( -e "AUTOMATON_RUNNER_NAME=$AUTOMATON_RUNNER_NAME" )
fi

docker_args+=( "$AUTOMATON_RUNNER_IMAGE" )
docker "${docker_args[@]}"

echo "Infrastructure Runner registration started: $AUTOMATON_CONTAINER_NAME"
echo "The Runner will exchange the one-time credential and persist its durable credential in: $AUTOMATON_DATA_ROOT/config"

for attempt in $(seq 1 30); do
  if [[ "$(docker inspect --format '{{.State.Running}}' "$AUTOMATON_CONTAINER_NAME" 2>/dev/null || true)" != "true" ]]; then
    echo "Infrastructure Runner stopped before enrollment completed." >&2
    docker logs --tail 80 "$AUTOMATON_CONTAINER_NAME" >&2 || true
    exit 1
  fi

  if docker logs "$AUTOMATON_CONTAINER_NAME" 2>&1 | grep -Eq "self-registration completed|Runner enrolled successfully|self-registered"; then
    echo "Infrastructure Runner registered successfully and is starting its Runner connection."
    exit 0
  fi

  sleep 2
done

echo "Infrastructure Runner is still starting. Check logs with: docker logs -f $AUTOMATON_CONTAINER_NAME"
