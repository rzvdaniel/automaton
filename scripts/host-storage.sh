#!/bin/bash
# Host-local capacity checks. Image cleanup is the Runner's nightly cleanup.
set -euo pipefail

storage_require_local_docker() {
    local endpoint="${DOCKER_HOST:-}"
    if [[ -z "$endpoint" ]]; then
        endpoint=$(docker context inspect --format '{{.Endpoints.docker.Host}}') || return 1
    fi
    [[ "$endpoint" == unix://* ]] || {
        echo '[ERROR] Storage checks require host-local Docker; run on the Docker host.' >&2; return 1;
    }
}

storage_filesystems() {
    storage_require_local_docker || return 1
    local docker_root mounts sources containers mount path relevant
    docker_root=$(docker info --format '{{.DockerRootDir}}') || return 1
    mounts=$(findmnt -rn --raw -t ext4,xfs,btrfs,zfs -o TARGET) || return 1
    containers=$(docker ps -aq) || return 1
    sources=""
    if [[ -n "$containers" ]]; then
        # Sources only: no credentials, container environments or private payloads.
        sources=$(docker inspect $containers --format '{{range .Mounts}}{{println .Source}}{{end}}') || return 1
    fi
    local paths=(/ "$docker_root" /var/lib/containerd "${TMPDIR:-/tmp}" "${REPO_ROOT:-$PWD}")
    while IFS= read -r path; do
        [[ -z "$path" ]] || paths+=("$path")
    done <<< "$sources
${AUTOMATON_STORAGE_PATHS:-}"
    while IFS= read -r mount; do
        mount=$(printf '%b' "$mount") # findmnt --raw escapes spaces in mount names.
        relevant=false
        for path in "${paths[@]}"; do
            [[ "$mount" == / || "$path" == "$mount" || "$path" == "$mount/"* ]] && relevant=true
        done
        [[ "$relevant" == false ]] || printf '%s\n' "$mount"
    done <<< "$mounts"
}

storage_check() {
    local mode="${1:-check}" path total free percent level result=0
    local minimum="${AUTOMATON_STORAGE_MIN_FREE_GIB:-10}" filesystems
    [[ "$minimum" =~ ^[1-9][0-9]*$ ]] || { echo 'Invalid minimum free GiB' >&2; return 1; }
    filesystems=$(storage_filesystems) || return 1
    [[ -n "$filesystems" ]] || { echo '[ERROR] No storage filesystems found' >&2; return 1; }
    while IFS= read -r path; do
        read -r total free < <(df -B1 --output=size,avail -- "$path" | tail -1) || return 1
        [[ "$total" =~ ^[1-9][0-9]*$ && "$free" =~ ^[0-9]+$ ]] || return 1
        percent=$((free * 100 / total))
        level=OK
        if (( free < 2*1024*1024*1024 || percent < 5 )); then
            level=CRITICAL; result=1
        elif (( free < 5*1024*1024*1024 || percent < 15 )); then
            level=WARNING; result=1
        fi
        printf '[%s] %s: %s bytes free (%s%%)\n' "$level" "$path" "$free" "$percent"
        if [[ "$mode" == preflight ]] && (( free < minimum*1024*1024*1024 || percent < 15 )); then
            echo "[ERROR] Refusing build/pull/deployment: $path needs at least $minimum GiB and 15% free for replacement alongside running/rollback images. Reclaim verified disposable data or expand the disk." >&2
            return 1
        fi
    done <<< "$filesystems"
    [[ "$mode" == preflight ]] || return "$result"
}


storage_main() {
    case "${1:-check}" in
        check|preflight) storage_check "${1:-check}" ;;
        *) echo 'Usage: host-storage.sh [check|preflight]' >&2; return 1 ;;
    esac
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    storage_main "$@"
fi
