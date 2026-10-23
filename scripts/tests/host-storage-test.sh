#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../host-storage.sh"
fixture=$(mktemp -d)
trap 'rm -rf -- "$fixture"' EXIT

# Discover relevant split mounts without blocking deployment on a small /boot.
docker() {
    case "$*" in
        'context inspect --format {{.Endpoints.docker.Host}}') echo unix:///var/run/docker.sock ;;
        'info --format {{.DockerRootDir}}') echo /workloads/docker ;;
        'ps -aq') echo container ;;
        'inspect container --format {{range .Mounts}}{{println .Source}}{{end}}') echo /database/mysql ;;
        *) return 1 ;;
    esac
}
findmnt() { printf '/\n/boot\n/workloads\n/var/lib/containerd\n/database\n'; }
[[ "$(storage_filesystems)" == $'/\n/workloads\n/var/lib/containerd\n/database' ]]
unset -f findmnt

storage_filesystems() { printf '/\n/workloads\n'; }
df() {
    echo 'Size Avail'
    if [[ "${*: -1}" == /workloads ]]; then
        printf '%s %s\n' "$((40*1024*1024*1024))" "$((workload_free*1024*1024*1024))"
    else
        printf '%s %s\n' "$((40*1024*1024*1024))" "$((20*1024*1024*1024))"
    fi
}
workload_free=12
storage_check preflight > "$fixture/check"
grep -q '/workloads' "$fixture/check"
workload_free=9
if storage_check preflight > "$fixture/check" 2>&1; then exit 1; fi
grep -q 'Refusing build/pull/deployment' "$fixture/check"
workload_free=4
if storage_check check > "$fixture/check"; then exit 1; fi
grep -q WARNING "$fixture/check"
workload_free=1
if storage_check check > "$fixture/check"; then exit 1; fi
grep -q CRITICAL "$fixture/check"
# Percentage thresholds also apply on larger filesystems.
df() { printf 'Size Avail\n1073741824000 10737418240\n'; }
if storage_check check > "$fixture/check"; then exit 1; fi
grep -q CRITICAL "$fixture/check"

AUTOMATON_STORAGE_IMAGE_ALLOWLIST="$fixture/allow"
AUTOMATON_STORAGE_DEPLOYMENT_STATE="$fixture/state"
printf 'owned:rollback\n' > "$fixture/allow"
printf 'API_PREVIOUS_REVISION=1234567\n' > "$fixture/state"
: > "$fixture/removed"
docker() {
    case "$*" in
        'context inspect --format {{.Endpoints.docker.Host}}') echo unix:///var/run/docker.sock ;;
        'image inspect owned:rollback --format {{.Id}}') echo protected ;;
        'image inspect automaton-prod-api:1234567 --format {{.Id}}') echo state-protected ;;
        'ps -aq') printf 'running\nstopped\n' ;;
        'inspect running --format {{.Image}}') echo active ;;
        'inspect stopped --format {{.Image}}') echo stopped ;;
        'image ls --no-trunc --format {{.Repository}} {{.Tag}} {{.ID}}')
            printf '%s\n' 'owned rollback protected' 'owned alias protected' 'owned current active' 'owned old-container stopped' 'owned history unused' 'owned previous state-protected' 'unowned old other' ;;
        'image rm owned:history') echo "$*" >> "$fixture/removed" ;;
        *) echo "Unexpected Docker operation: $*" >&2; return 1 ;;
    esac
}
storage_clean_images
[[ "$(cat "$fixture/removed")" == 'image rm owned:history' ]]
# A missing protected reference aborts before any removals.
printf 'owned:missing\n' >> "$fixture/allow"
: > "$fixture/removed"
if storage_clean_images 2>/dev/null; then exit 1; fi
[[ ! -s "$fixture/removed" ]]
DOCKER_HOST=tcp://remote.invalid:2376
if storage_require_local_docker 2>/dev/null; then exit 1; fi
unset DOCKER_HOST
# Admission failure precedes infrastructure changes and build-context creation.
source "$SCRIPT_DIR/../prod-blue-green-common.sh"
AUTOMATON_DEPLOYMENT_LOCK="$fixture/lock"
flock() { :; }
storage_check() { return 1; }
ensure_blue_green_bootstrap() { echo mutation >> "$fixture/mutations"; }
create_production_build_context() { echo mutation >> "$fixture/mutations"; }
if ensure_infrastructure; then exit 1; fi
if run_source_build_service api-blue 1234567; then exit 1; fi
[[ ! -e "$fixture/mutations" ]]
echo 'host storage capacity and image retention regressions passed'
