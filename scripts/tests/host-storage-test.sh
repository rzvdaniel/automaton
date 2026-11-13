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
        'inspect container --format {{range .Mounts}}{{println .Source}}{{end}}') echo /database/postgres ;;
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

DOCKER_HOST=tcp://remote.invalid:2376
if storage_require_local_docker 2>/dev/null; then exit 1; fi
unset DOCKER_HOST
echo 'host storage capacity regressions passed'
