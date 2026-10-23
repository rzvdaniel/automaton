#!/bin/bash
# Host-local capacity checks and narrowly scoped image maintenance. No volume pruning.
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

storage_clean_images() {
    storage_require_local_docker || return 1
    local allowlist="${AUTOMATON_STORAGE_IMAGE_ALLOWLIST:-/etc/automaton/storage-images.allow}"
    [[ -s "$allowlist" ]] || { echo "[ERROR] Missing image allow-list: $allowlist; no images removed" >&2; return 1; }
    local ref id repo tag container
    declare -A protected=() repositories=()
    # Validate the complete allow-list before deleting anything. Protect by image ID,
    # so aliases cannot cause deletion of an intentionally retained artifact.
    while IFS= read -r ref || [[ -n "$ref" ]]; do
        [[ -z "$ref" || "$ref" == \#* ]] && continue
        [[ "$ref" =~ ^[^[:space:]]+:[^/:[:space:]]+$ && "$ref" != *:latest ]] || {
            echo "[ERROR] Use explicit non-latest image tags in $allowlist" >&2; return 1;
        }
        id=$(docker image inspect "$ref" --format '{{.Id}}') || return 1
        protected["$id"]=1
        repositories["${ref%:*}"]=1
    done < "$allowlist"
    # Blue/green state explicitly names current and one rollback revision.
    # Parse only known keys; never execute the state file as shell.
    local state="${AUTOMATON_STORAGE_DEPLOYMENT_STATE:-$(dirname "${BASH_SOURCE[0]}")/../.prod-runtime/nginx/state/blue-green-state.env}"
    local component key revision
    if [[ -f "$state" ]]; then
        while IFS='=' read -r key revision; do
            [[ "$key" =~ ^(API|STUDIO|WEBSITE)_(CURRENT|PREVIOUS)_REVISION$ && -n "$revision" ]] || continue
            [[ "$revision" =~ ^[0-9a-f]{7,40}$ ]] || { echo '[ERROR] Invalid deployment revision' >&2; return 1; }
            component="${key%%_*}"
            repo="automaton-prod-${component,,}"
            id=$(docker image inspect "$repo:$revision" --format '{{.Id}}') || return 1
            protected["$id"]=1
        done < "$state"
    fi
    # Include stopped containers; none are removed by maintenance.
    local containers
    containers=$(docker ps -aq) || return 1
    while IFS= read -r container; do
        [[ -n "$container" ]] || continue
        id=$(docker inspect "$container" --format '{{.Image}}') || return 1
        protected["$id"]=1
    done <<< "$containers"
    local inventory
    inventory=$(docker image ls --no-trunc --format '{{.Repository}} {{.Tag}} {{.ID}}') || return 1
    while read -r repo tag id; do
        [[ -n "$repo" && -n "${repositories[$repo]:-}" && -z "${protected[$id]:-}" && "$tag" != '<none>' ]] || continue
        # Check again immediately before removal; Docker's non-force removal also
        # refuses images acquired by a container since the snapshot.
        local used=false
        local latest_image
        containers=$(docker ps -aq) || return 1
        while IFS= read -r container; do
            [[ -n "$container" ]] || continue
            latest_image=$(docker inspect "$container" --format '{{.Image}}') || return 1
            [[ "$latest_image" != "$id" ]] || used=true
        done <<< "$containers"
        [[ "$used" == false ]] || continue
        docker image rm "$repo:$tag" || return 1
    done <<< "$inventory"
}

storage_main() {
    case "${1:-check}" in
        check|preflight) storage_check "${1:-check}" ;;
        maintain)
            # The caller must serialize this with deployments. No automatic
            # database purge, journal deletion, volume prune or archive deletion.
            storage_clean_images
            ;;
        *) echo 'Usage: host-storage.sh [check|preflight|maintain]' >&2; return 1 ;;
    esac
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    storage_main "$@"
fi
