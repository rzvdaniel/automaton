#!/usr/bin/env bash
set -euo pipefail

# The single source of truth for an Infrastructure Runner container.  Install,
# repair, update and rollback all invoke the same builder so recovery never
# requires an administrator to reconstruct privileged docker arguments.
SPECIFICATION_VERSION=1
OPERATION=install
AUTOMATON_SERVER_URL="${AUTOMATON_SERVER_URL:-}"
AUTOMATON_API_KEY="${AUTOMATON_API_KEY:-}"
AUTOMATON_ENROLLMENT_TOKEN="${AUTOMATON_ENROLLMENT_TOKEN:-}"
AUTOMATON_RUNNER_IMAGE="${AUTOMATON_RUNNER_IMAGE:-automaton-runner:latest}"
AUTOMATON_RUNNER_NAME="${AUTOMATON_RUNNER_NAME:-}"
AUTOMATON_PUBLIC_IP="${AUTOMATON_PUBLIC_IP:-}"
AUTOMATON_PRIVATE_IP="${AUTOMATON_PRIVATE_IP:-}"
AUTOMATON_CONTAINER_NAME="${AUTOMATON_CONTAINER_NAME:-automaton-infrastructure-runner}"
AUTOMATON_ROOT="${AUTOMATON_ROOT:-/var/lib/automaton}"
AUTOMATON_DATA_ROOT="${AUTOMATON_DATA_ROOT:-$AUTOMATON_ROOT/infrastructure-runner}"
ROLLBACK_IMAGE=""
IMAGE_WAS_EXPLICIT=false
INSTALLATION_CONFIG_FILE=""
SERVER_URL_WAS_EXPLICIT=false
RUNNER_NAME_WAS_EXPLICIT=false
PUBLIC_IP_WAS_EXPLICIT=false
PRIVATE_IP_WAS_EXPLICIT=false

usage() {
  echo "Usage: $0 [install|update|repair|replace] [--version IMAGE] [installer options]" >&2
}

if [[ $# -gt 0 && "$1" != --* ]]; then OPERATION="$1"; shift; fi
case "$OPERATION" in install|update|repair|replace) ;; *) usage; exit 1 ;; esac
while [[ $# -gt 0 ]]; do
  case "$1" in
    --server-url) AUTOMATON_SERVER_URL="${2:-}"; SERVER_URL_WAS_EXPLICIT=true; shift 2 ;;
    --enrollment-token) AUTOMATON_ENROLLMENT_TOKEN="${2:-}"; shift 2 ;;
    --api-key|--enrollment-key) AUTOMATON_API_KEY="${2:-}"; shift 2 ;;
    --runner-image|--version) AUTOMATON_RUNNER_IMAGE="${2:-}"; IMAGE_WAS_EXPLICIT=true; shift 2 ;;
    --runner-name) AUTOMATON_RUNNER_NAME="${2:-}"; RUNNER_NAME_WAS_EXPLICIT=true; shift 2 ;;
    --public-ip) AUTOMATON_PUBLIC_IP="${2:-}"; PUBLIC_IP_WAS_EXPLICIT=true; shift 2 ;;
    --private-ip) AUTOMATON_PRIVATE_IP="${2:-}"; PRIVATE_IP_WAS_EXPLICIT=true; shift 2 ;;
    --container-name) AUTOMATON_CONTAINER_NAME="${2:-}"; shift 2 ;;
    --data-root) AUTOMATON_DATA_ROOT="${2:-}"; shift 2 ;;
    *) echo "Unknown installer argument: $1" >&2; exit 1 ;;
  esac
done

[[ "$(id -u)" -eq 0 ]] || { echo "Run this script as root." >&2; exit 1; }
if ! command -v docker >/dev/null 2>&1; then
  export DEBIAN_FRONTEND=noninteractive; apt-get update; apt-get install -y docker.io
fi
systemctl enable --now docker
mkdir -p "$AUTOMATON_DATA_ROOT"/{config,logs,workflows,artifacts} "$AUTOMATON_ROOT"/{hosted-runners,gateway,certificates}
INSTALLATION_CONFIG_FILE="$AUTOMATON_DATA_ROOT/config/container-specification.env"
exec 9>"$AUTOMATON_DATA_ROOT/update.lock"
flock -n 9 || { echo "Another Infrastructure Runner lifecycle operation is already running on this host." >&2; exit 75; }

existing_env() { docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$AUTOMATON_CONTAINER_NAME" 2>/dev/null | sed -n "s/^$1=//p" | head -n 1; }
load_installation_configuration() {
  [[ -r "$INSTALLATION_CONFIG_FILE" ]] || return 0
  # This file is written by this installer with shell-escaped values and contains no credentials.
  # shellcheck disable=SC1090
  source "$INSTALLATION_CONFIG_FILE"
}
save_installation_configuration() {
  umask 077
  {
    printf 'AUTOMATON_SERVER_URL=%q\n' "$AUTOMATON_SERVER_URL"
    printf 'AUTOMATON_RUNNER_NAME=%q\n' "$AUTOMATON_RUNNER_NAME"
    printf 'AUTOMATON_PUBLIC_IP=%q\n' "$AUTOMATON_PUBLIC_IP"
    printf 'AUTOMATON_PRIVATE_IP=%q\n' "$AUTOMATON_PRIVATE_IP"
    printf 'AUTOMATON_CONTAINER_NAME=%q\n' "$AUTOMATON_CONTAINER_NAME"
    printf 'AUTOMATON_DATA_ROOT=%q\n' "$AUTOMATON_DATA_ROOT"
    printf 'AUTOMATON_ROOT=%q\n' "$AUTOMATON_ROOT"
    printf 'AUTOMATON_RUNNER_IMAGE=%q\n' "$AUTOMATON_RUNNER_IMAGE"
    printf 'SPECIFICATION_VERSION=%q\n' "$SPECIFICATION_VERSION"
  } >"$INSTALLATION_CONFIG_FILE"
}
CLI_SERVER_URL="$AUTOMATON_SERVER_URL"; CLI_RUNNER_NAME="$AUTOMATON_RUNNER_NAME"; CLI_PUBLIC_IP="$AUTOMATON_PUBLIC_IP"; CLI_PRIVATE_IP="$AUTOMATON_PRIVATE_IP"; CLI_IMAGE="$AUTOMATON_RUNNER_IMAGE"
load_installation_configuration
[[ "$SERVER_URL_WAS_EXPLICIT" == true ]] && AUTOMATON_SERVER_URL="$CLI_SERVER_URL"
[[ "$RUNNER_NAME_WAS_EXPLICIT" == true ]] && AUTOMATON_RUNNER_NAME="$CLI_RUNNER_NAME"
[[ "$PUBLIC_IP_WAS_EXPLICIT" == true ]] && AUTOMATON_PUBLIC_IP="$CLI_PUBLIC_IP"
[[ "$PRIVATE_IP_WAS_EXPLICIT" == true ]] && AUTOMATON_PRIVATE_IP="$CLI_PRIVATE_IP"
[[ "$IMAGE_WAS_EXPLICIT" == true ]] && AUTOMATON_RUNNER_IMAGE="$CLI_IMAGE"
if [[ -z "$AUTOMATON_SERVER_URL" ]]; then AUTOMATON_SERVER_URL="$(existing_env AUTOMATON_SERVER_URL || true)"; fi
if [[ -z "$AUTOMATON_RUNNER_NAME" ]]; then AUTOMATON_RUNNER_NAME="$(existing_env AUTOMATON_RUNNER_NAME || true)"; fi
if [[ -z "$AUTOMATON_PUBLIC_IP" ]]; then AUTOMATON_PUBLIC_IP="$(existing_env AUTOMATON_PUBLIC_IP || true)"; fi
if [[ -z "$AUTOMATON_PRIVATE_IP" ]]; then AUTOMATON_PRIVATE_IP="$(existing_env AUTOMATON_PRIVATE_IP || true)"; fi
if [[ "$OPERATION" == repair && "$IMAGE_WAS_EXPLICIT" == false ]]; then
  AUTOMATON_RUNNER_IMAGE="$(docker inspect --format '{{.Config.Image}}' "$AUTOMATON_CONTAINER_NAME" 2>/dev/null || true)"
  [[ -n "$AUTOMATON_RUNNER_IMAGE" ]] || { echo "repair requires --version when no existing Infrastructure Runner container is available." >&2; exit 1; }
fi
[[ -n "$AUTOMATON_SERVER_URL" ]] || { echo "Set --server-url for the initial installation." >&2; exit 1; }
if [[ "$OPERATION" == install && -z "$AUTOMATON_API_KEY" && -z "$AUTOMATON_ENROLLMENT_TOKEN" ]]; then
  echo "Set --api-key or --enrollment-token for installation." >&2; exit 1
fi
if [[ "$OPERATION" == replace && -z "$AUTOMATON_API_KEY" && -z "$AUTOMATON_ENROLLMENT_TOKEN" ]]; then
  echo "replace requires a fresh enrollment key or token." >&2; exit 1
fi

resolve_image() {
  docker pull "$AUTOMATON_RUNNER_IMAGE" >&2
  local digest
  digest="$(docker image inspect --format '{{index .RepoDigests 0}}' "$AUTOMATON_RUNNER_IMAGE" 2>/dev/null || true)"
  [[ "$digest" == *@sha256:* ]] || { echo "Unable to resolve an immutable digest for $AUTOMATON_RUNNER_IMAGE." >&2; exit 1; }
  AUTOMATON_RUNNER_IMAGE="$digest"
}

installation_id() {
  local profile="$AUTOMATON_DATA_ROOT/config/installation-profile.json"
  [[ -f "$profile" ]] && sed -n 's/.*"InstallationId"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$profile" | head -n 1 || true
}

run_canonical_container() {
  local image="$1" digest="$2" id
  id="$(installation_id)"
  local args=(run -d --name "$AUTOMATON_CONTAINER_NAME" --restart unless-stopped --privileged --user 0:0 --pid host --network host
    --label "automaton.installation-id=$id" --label "automaton.runner-version=${image#*:}" --label "automaton.image-digest=$digest" --label "automaton.specification-version=$SPECIFICATION_VERSION"
    -v /var/run/docker.sock:/var/run/docker.sock -v /:/host
    -v "$AUTOMATON_DATA_ROOT/config:/app/config" -v "$AUTOMATON_DATA_ROOT/workflows:/automaton/workspace"
    -v "$AUTOMATON_DATA_ROOT/artifacts:/automaton/artifacts" -v "$AUTOMATON_DATA_ROOT/logs:/app/logs"
    -e "AUTOMATON_SERVER_URL=$AUTOMATON_SERVER_URL" -e "AUTOMATON_RUNNER_PURPOSE=Infrastructure" -e "AUTOMATON_RUNNER_IMAGE=$image"
    -e "AUTOMATON_RUNNER_IMAGE_DIGEST=$digest" -e "AUTOMATON_CONTAINER_SPECIFICATION_VERSION=$SPECIFICATION_VERSION")
  [[ -n "$AUTOMATON_RUNNER_NAME" ]] && args+=(-e "AUTOMATON_RUNNER_NAME=$AUTOMATON_RUNNER_NAME")
  [[ -n "$AUTOMATON_PUBLIC_IP" ]] && args+=(-e "AUTOMATON_PUBLIC_IP=$AUTOMATON_PUBLIC_IP")
  [[ -n "$AUTOMATON_PRIVATE_IP" ]] && args+=(-e "AUTOMATON_PRIVATE_IP=$AUTOMATON_PRIVATE_IP")
  [[ -n "$AUTOMATON_API_KEY" ]] && args+=(-e "AUTOMATON_API_KEY=$AUTOMATON_API_KEY")
  [[ -n "$AUTOMATON_ENROLLMENT_TOKEN" ]] && args+=(-e "AUTOMATON_ENROLLMENT_TOKEN=$AUTOMATON_ENROLLMENT_TOKEN")
  args+=("$image"); docker "${args[@]}"
}

wait_for_healthy() {
  for _ in $(seq 1 30); do
    [[ "$(docker inspect --format '{{.State.Running}}' "$AUTOMATON_CONTAINER_NAME" 2>/dev/null || true)" == true ]] || return 1
    docker exec "$AUTOMATON_CONTAINER_NAME" curl -fsS http://127.0.0.1:8001/health >/dev/null 2>&1 \
      && docker logs "$AUTOMATON_CONTAINER_NAME" 2>&1 | grep -Fq 'Successfully connected to server' \
      && return 0
    sleep 2
  done
  return 1
}

ROLLBACK_IMAGE="$(docker inspect --format '{{.Config.Image}}' "$AUTOMATON_CONTAINER_NAME" 2>/dev/null || true)"
resolve_image
TARGET_DIGEST="$AUTOMATON_RUNNER_IMAGE"
if [[ "$OPERATION" == replace ]]; then
  # Do this only after the replacement artifact has been pulled and resolved.
  rm -f "$AUTOMATON_DATA_ROOT/config/api-key" "$AUTOMATON_DATA_ROOT/config/runner.json" "$AUTOMATON_DATA_ROOT/config/installation-profile.json" "$AUTOMATON_DATA_ROOT/config/installation-id"
fi
docker rm -f "$AUTOMATON_CONTAINER_NAME" >/dev/null 2>&1 || true
run_canonical_container "$AUTOMATON_RUNNER_IMAGE" "$TARGET_DIGEST"
if ! wait_for_healthy; then
  docker logs --tail 120 "$AUTOMATON_CONTAINER_NAME" >&2 || true
  docker rm -f "$AUTOMATON_CONTAINER_NAME" >/dev/null 2>&1 || true
  if [[ -n "$ROLLBACK_IMAGE" && "$OPERATION" == update ]]; then
    echo "Update failed; restoring the previous image." >&2
    PREVIOUS_DIGEST="$(docker image inspect --format '{{index .RepoDigests 0}}' "$ROLLBACK_IMAGE" 2>/dev/null || echo "$ROLLBACK_IMAGE")"
    run_canonical_container "$ROLLBACK_IMAGE" "$PREVIOUS_DIGEST"
    if wait_for_healthy; then
      echo "Update failed; rollback succeeded." >&2
    else
      echo "Update failed; rollback also failed. Manual recovery is required." >&2
    fi
  fi
  exit 1
fi
echo "Infrastructure Runner $OPERATION succeeded using immutable image $TARGET_DIGEST (specification v$SPECIFICATION_VERSION)."
save_installation_configuration
