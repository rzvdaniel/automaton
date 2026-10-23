#!/bin/bash
# Run on Linux: a prior deployment holds the real shared flock while state changes.
set -euo pipefail
command -v flock >/dev/null || { echo 'Requires Linux flock' >&2; exit 77; }
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
fixture=$(mktemp -d)
waiter=""
trap '[[ -z "$waiter" ]] || kill "$waiter" 2>/dev/null || true; rm -rf -- "$fixture"' EXIT
cp "$REPO_ROOT/scripts/prod-redeploy-all.sh" "$fixture/"
cat > "$fixture/prod-blue-green-common.sh" <<'EOF'
source "$TEST_COMMON_SCRIPT"
record() { printf '%s\n' "$*" >> "$TEST_FIXTURE/events"; }
flock() {
    [[ "$*" != '-x 200' ]] || touch "$TEST_FIXTURE/waiting"
    command flock "$@"
}
validate_recovery_deployment_options() { :; }
get_live_color() { record 'read-state'; cat "$TEST_FIXTURE/color"; }
get_service_revision() { printf '%s-%s\n' "$(cat "$TEST_FIXTURE/revision")" "$2"; }
resolve_target_color() {
    if [[ "$(get_live_color "$1")" == blue ]]; then echo green; else echo blue; fi
}
preserve_current_service_image() { record "preserve:$1:$2:$(get_service_revision "$1" current)"; }
ensure_infrastructure() { acquire_deployment_lock; record infrastructure; }
deploy_color() { record "deploy:$1:$2"; }
switch_all_live_colors() { record "rollback-inputs:$5:$7:$9"; }
request_api_color_runner_tunnel_reconnect() { :; }
status_table() { :; }
maintain_deployment_images() {
    # The probe must not inherit the deployment's locked descriptor.
    if flock -n "$AUTOMATON_DEPLOYMENT_LOCK" -c true 200>&-; then
        echo 'Deployment released its lock before cleanup' >&2
        return 1
    fi
    record cleanup
}
EOF

printf 'blue\n' > "$fixture/color"
printf 'old\n' > "$fixture/revision"
exec 201>"$fixture/lock"
flock -x 201
env TEST_COMMON_SCRIPT="$REPO_ROOT/scripts/prod-blue-green-common.sh" \
    TEST_FIXTURE="$fixture" AUTOMATON_DEPLOYMENT_LOCK="$fixture/lock" \
    timeout 20s bash "$fixture/prod-redeploy-all.sh" --keep-previous > "$fixture/output" 2>&1 201>&- &
waiter=$!
for ((attempt=0; attempt<200; attempt++)); do
    [[ ! -e "$fixture/waiting" ]] || break
    sleep 0.025
done
[[ -e "$fixture/waiting" ]]
[[ ! -s "$fixture/events" ]] # No state reads or preservation while waiting.

# The first deployment finishes while the second is waiting.
printf 'green\n' > "$fixture/color"
printf 'new\n' > "$fixture/revision"
flock -u 201
wait "$waiter" || { cat "$fixture/output" >&2; exit 1; }
waiter=""
for component in api studio website; do
    grep -Fxq "preserve:$component:green:new-current" "$fixture/events"
    grep -Fxq "deploy:$component:blue" "$fixture/events"
done
grep -Fxq 'rollback-inputs:new-current:new-current:new-current' "$fixture/events"
grep -Fxq cleanup "$fixture/events"
echo 'waiting deployment uses fresh colors/revisions and holds lock through cleanup'
