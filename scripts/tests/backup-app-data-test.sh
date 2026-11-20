#!/usr/bin/env bash
# Round trip for the app-data part of backup.sh and restore.sh against real Docker volumes (needs a Docker daemon;
# runs as any user in the docker group). Creates volumes named e2e-test-*, never touches other volumes, removes its own.
#   scripts/tests/backup-app-data-test.sh
set -euo pipefail
repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
work=$(mktemp -d)
tag=e2e-test-$$
web_volume=$tag-workspace
compose_volume=${tag}_uploads
other_volume=${tag}-other-workspace
app=11111111-2222-3333-4444-555555555555
other_app=66666666-7777-8888-9999-aaaaaaaaaaaa
helper=alpine
cleanup() {
    docker rm -f "$tag-web" "$tag-compose" >/dev/null 2>&1 || true
    docker volume rm -f "$web_volume" "$compose_volume" "$other_volume" >/dev/null 2>&1 || true
    rm -rf -- "$work"
}
trap cleanup EXIT
fail() { echo "FAIL: $*" >&2; exit 1; }

# Only the fixtures: wrap docker so volume listings are limited to this test's volumes.
mkdir "$work/bin"
cat > "$work/bin/docker" <<WRAP
#!/usr/bin/env bash
if [[ "\$1 \$2" == "volume ls" ]]; then exec $(command -v docker) volume ls --filter name=$tag "\${@:3}"; fi
exec $(command -v docker) "\$@"
WRAP
chmod +x "$work/bin/docker"
export PATH="$work/bin:$PATH"

extract() { sed -n '/^# app-data:begin/,/^# app-data:end/p' "$1"; }
source <(extract "$repo/docker/install/backup.sh")
source <(extract "$repo/docker/install/restore.sh")

docker volume create --label automaton.resource=machine-workspace-volume "$web_volume" >/dev/null
docker volume create --label automaton.resource=machine-workspace-volume "$other_volume" >/dev/null
docker volume create --label com.docker.compose.project="$tag" --label com.docker.compose.volume=uploads "$compose_volume" >/dev/null
docker run --rm -v "$web_volume:/v" "$helper" sh -c "mkdir -p /v/workspace/data/$app/data/sub /v/workspace/data/$other_app/data /v/workspace/data/not-a-guid && echo web-one > /v/workspace/data/$app/data/a.txt && echo web-nested > /v/workspace/data/$app/data/sub/b.txt && echo other > /v/workspace/data/$other_app/data/o.txt"
docker run --rm -v "$compose_volume:/v" "$helper" sh -c 'echo compose-file > /v/upload.pdf'
docker run -d --name "$tag-web" --label "com.automaton.app-id=$app" "$helper" sleep 600 >/dev/null
docker run -d --name "$tag-compose" --label "com.docker.compose.project=$tag" "$helper" sleep 600 >/dev/null

mkdir "$work/backup"
backup_app_data "$work/backup" "$helper" automaton-server-test
manifest="$work/backup/app-data/manifest.tsv"
[[ $(wc -l < "$manifest") == 3 ]] || fail "expected 3 archives (2 web apps, 1 compose volume), got: $(cat "$manifest")"
grep -q "^web.$app." "$manifest" || fail 'web app missing from manifest'
grep -q "^compose.$tag." "$manifest" || fail 'compose volume missing from manifest'
grep -q 'not-a-guid' "$manifest" && fail 'a non-app directory was backed up as an app'
[[ $(docker inspect -f '{{.State.Paused}}' "$tag-web") == false && $(docker inspect -f '{{.State.Paused}}' "$tag-compose") == false ]] || fail 'containers were left paused'

# Wipe everything, then a full restore (the compose volume is deleted entirely).
docker run --rm -v "$web_volume:/v" "$helper" sh -c 'rm -rf /v/workspace/data/*'
docker rm -f "$tag-compose" >/dev/null
docker volume rm "$compose_volume" >/dev/null
restore_app_data '' "$helper" "$work/backup" >/dev/null
check() { [[ $(docker run --rm -v "$1:/v:ro" "$helper" cat "/v/$2") == "$3" ]] || fail "$2 in $1 is not '$3'"; }
check "$web_volume" "workspace/data/$app/data/a.txt" web-one
check "$web_volume" "workspace/data/$app/data/sub/b.txt" web-nested
check "$web_volume" "workspace/data/$other_app/data/o.txt" other
check "$compose_volume" upload.pdf compose-file
[[ $(docker volume inspect -f '{{index .Labels "com.docker.compose.volume"}}' "$compose_volume") == uploads ]] || fail 'compose volume lost its label'
[[ $(docker inspect -f '{{.State.Running}}' "$tag-web") == true ]] || fail 'the app container was not started again'

# Single-app restore: changes only the chosen app.
docker run --rm -v "$web_volume:/v" "$helper" sh -c "rm -rf /v/workspace/data/$app/data/* && echo changed > /v/workspace/data/$other_app/data/o.txt"
restore_app_data "$app" "$helper" "$work/backup" >/dev/null
check "$web_volume" "workspace/data/$app/data/a.txt" web-one
check "$web_volume" "workspace/data/$other_app/data/o.txt" changed
restore_app_data nobody "$helper" "$work/backup" >/dev/null 2>&1 && fail 'an unknown app was accepted'
echo PASS
