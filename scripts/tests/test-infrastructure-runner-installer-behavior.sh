#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
INSTALLER="$ROOT/scripts/install-infrastructure-runner.sh"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
mkdir -p "$TMP/bin" "$TMP/data/config"
LOG="$TMP/docker.log"; export LOG
CURL_LOG="$TMP/curl.log"; export CURL_LOG
OLD_DIGEST="$(printf '%064d' 0 | tr '0' '1')"
NEW_DIGEST="$(printf '%064d' 0 | tr '0' '2')"
TEST_DIGEST="$(printf '%064d' 0 | tr '0' '3')"
export OLD_DIGEST NEW_DIGEST

cat >"$TMP/bin/id" <<'EOF'
#!/usr/bin/env bash
[[ "$1" == -u ]] && echo 0
EOF
cat >"$TMP/bin/systemctl" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
cat >"$TMP/bin/sleep" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
cat >"$TMP/bin/docker" <<'EOF'
#!/usr/bin/env bash
set -e
printf '%s\n' "$*" >>"$LOG"
if [[ "$1" == pull ]]; then [[ "${MOCK_PULL_FAIL:-}" != 1 ]] || exit 1; exit 0; fi
if [[ "$1" == image && "$2" == inspect ]]; then
  [[ "${@: -1}" == "example/runner@sha256:$OLD_DIGEST" ]] && echo "example/runner@sha256:$OLD_DIGEST" || echo "${MOCK_REPO_DIGEST:-example/runner@sha256:$NEW_DIGEST}"
  exit 0
fi
if [[ "$1" == inspect ]]; then
  case "$*" in
    *'.Config.Image'*) echo "example/runner@sha256:$OLD_DIGEST" ;;
    *'.State.Running'*) echo true ;;
    *'.Config.Env'*) echo 'AUTOMATON_SERVER_URL=https://api.example.test' ;;
  esac
  exit 0
fi
if [[ "$1" == run ]]; then
  image="${@: -1}"; printf '%s' "$image" >"$LOG.image"
  [[ "${MOCK_TARGET_RUN_FAIL:-}" == 1 && "$image" == "example/runner@sha256:$NEW_DIGEST" ]] && exit 1
  [[ "${MOCK_ROLLBACK_RUN_FAIL:-}" == 1 && "$image" == "example/runner@sha256:$OLD_DIGEST" ]] && exit 1
  exit 0
fi
if [[ "$1" == exec ]]; then
  [[ "${MOCK_ALL_HEALTH_FAIL:-}" != 1 && ( "${MOCK_TARGET_HEALTH_FAIL:-}" != 1 || "$(cat "$LOG.image")" != "example/runner@sha256:$NEW_DIGEST" ) ]] && exit 0
  exit 1
fi
if [[ "$1" == logs ]]; then echo 'Successfully connected to server'; exit 0; fi
exit 0
EOF
cat >"$TMP/bin/curl" <<'EOF'
#!/usr/bin/env bash
set -e
printf '%s\n' "$*" >>"$CURL_LOG"
[[ "${MOCK_CURL_FAIL:-}" != 1 ]] || exit 1
output=""
while [[ $# -gt 0 ]]; do
  if [[ "$1" == -o ]]; then output="$2"; shift 2; continue; fi
  shift
done
[[ -n "$output" ]] || exit 1
cat >"$output" <<'CONFIG'
RabbitMq__Host=rabbitmq.test
RabbitMq__Port=5672
RabbitMq__Username=infra
RabbitMq__Password=test-only
RabbitMq__VirtualHost=/
RabbitMq__ExchangeName=cap.default.router
CONFIG
EOF
chmod +x "$TMP/bin"/*
PATH="$TMP/bin:$PATH"

# A persisted v1 configuration cannot downgrade the executing v2 installer.
cat >"$TMP/data/config/container-specification.env" <<EOF
AUTOMATON_SERVER_URL=https://api.example.test
AUTOMATON_RUNNER_NAME=infrastructure-test
AUTOMATON_PRIVATE_IP=10.0.0.2
AUTOMATON_CONTAINER_NAME=automaton-infrastructure-runner
AUTOMATON_DATA_ROOT=/var/lib/automaton/infrastructure-runner
AUTOMATON_ROOT=/var/lib/automaton
AUTOMATON_RUNNER_IMAGE=example/runner@sha256:$OLD_DIGEST
SPECIFICATION_VERSION=1
EOF
sed -i "s#/var/lib/automaton/infrastructure-runner#$TMP/data#g; s#/var/lib/automaton#$TMP/root#g" "$TMP/data/config/container-specification.env"
printf '{"InstallationId":"test-installation-id"}\n' >"$TMP/data/config/installation-profile.json"
printf '{"RunnerId":"test-runner-id","RunnerHostId":"test-runner-host-id"}\n' >"$TMP/data/config/runner.json"
printf 'test-installation-id\n' >"$TMP/data/config/installation-id"
AUTOMATON_API_KEY=test-key AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" repair --data-root "$TMP/data" --server-url https://api.example.test
grep -Eq '^run -d --name automaton-infrastructure-runner .*automaton.specification-version=2.*AUTOMATON_CONTAINER_SPECIFICATION_VERSION=2.*AUTOMATON_RUNTIME_HOST_ADDRESS=10.0.0.2' "$LOG"
grep -Fq 'SPECIFICATION_VERSION=2' "$TMP/data/config/container-specification.env"
grep -Fq 'automaton.installation-id=test-installation-id' "$LOG"
[[ "$(cat "$TMP/data/config/runner.json")" == '{"RunnerId":"test-runner-id","RunnerHostId":"test-runner-host-id"}' ]]
[[ "$(cat "$TMP/data/config/installation-profile.json")" == '{"InstallationId":"test-installation-id"}' ]]
[[ "$(cat "$TMP/data/config/installation-id")" == 'test-installation-id' ]]

# Missing broker configuration fails before a container is started.
mkdir -p "$TMP/missing/config"
if MOCK_CURL_FAIL=1 AUTOMATON_API_KEY=test-key AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" repair --data-root "$TMP/missing" --server-url https://api.example.test --version example/runner:latest >"$TMP/missing.out" 2>&1; then
  echo 'expected missing RabbitMQ configuration failure' >&2; exit 1
fi
grep -Fq 'RabbitMQ configuration could not be resolved' "$TMP/missing.out"

# A fresh installation using only the supported enrollment token can bootstrap
# broker configuration before the Runner has exchanged the token for its
# durable API key, and reaches container launch.
mkdir -p "$TMP/token-data/config"
: >"$LOG"
: >"$CURL_LOG"
AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" install --data-root "$TMP/token-data" \
  --server-url https://api.example.test --enrollment-token test-token \
  --version example/runner:latest
grep -Fq 'X-Runner-Enrollment-Token: test-token' "$CURL_LOG"
! grep -Fq 'X-API-Key:' "$CURL_LOG"
grep -Fq "example/runner@sha256:$NEW_DIGEST" "$LOG"

# Repair uses the installed immutable image, not the default mutable tag.
AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" repair --data-root "$TMP/data" --server-url https://api.example.test
grep -Fq "pull example/runner@sha256:$OLD_DIGEST" "$LOG"
! grep -Fq 'pull automaton-runner:latest' "$LOG"
grep -Fq "run -d --name automaton-infrastructure-runner" "$LOG"
grep -Fq "example/runner@sha256:$OLD_DIGEST" "$LOG"

# If the replacement and rollback are both unhealthy, the installer reports
# the distinct manual-recovery outcome rather than claiming success.
: >"$LOG"
if MOCK_ALL_HEALTH_FAIL=1 AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" update --data-root "$TMP/data" --server-url https://api.example.test --version example/runner:new >"$TMP/failed-rollback.out" 2>&1; then
  echo 'expected failed rollback' >&2; exit 1
fi
grep -Fq 'Update failed; rollback also failed. Manual recovery is required.' "$TMP/failed-rollback.out"

# A failed replacement resolves before it removes the durable identity.
printf 'existing-api-key' >"$TMP/data/config/api-key"
printf 'existing-runner-config' >"$TMP/data/config/runner.json"
printf 'existing-profile' >"$TMP/data/config/installation-profile.json"
printf 'existing-identity' >"$TMP/data/config/installation-id"
if MOCK_PULL_FAIL=1 AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" replace --data-root "$TMP/data" --server-url https://api.example.test --enrollment-key key --version example/runner:bad; then
  echo 'expected replacement pull failure' >&2; exit 1
fi
[[ "$(cat "$TMP/data/config/api-key")" == existing-api-key ]]
[[ "$(cat "$TMP/data/config/runner.json")" == existing-runner-config ]]
[[ "$(cat "$TMP/data/config/installation-profile.json")" == existing-profile ]]
[[ "$(cat "$TMP/data/config/installation-id")" == existing-identity ]]

# A failed update automatically recreates and verifies the prior digest.
: >"$LOG"
if MOCK_TARGET_HEALTH_FAIL=1 AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" update --data-root "$TMP/data" --server-url https://api.example.test --version example/runner:new >"$TMP/health-failure.out" 2>&1; then
  echo 'expected failed update with rollback' >&2; exit 1
fi
grep -Fq "example/runner@sha256:$NEW_DIGEST" "$LOG"
grep -Fq "example/runner@sha256:$OLD_DIGEST" "$LOG"
grep -Fq 'Update failed; rollback succeeded.' "$TMP/health-failure.out"

: >"$LOG"
if MOCK_TARGET_RUN_FAIL=1 AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" update --data-root "$TMP/data" --server-url https://api.example.test --version example/runner:new >"$TMP/start-failure.out" 2>&1; then
  echo 'expected failed target start' >&2; exit 1
fi
grep -Fq "example/runner@sha256:$OLD_DIGEST" "$LOG"
grep -Fq 'Update failed; rollback succeeded.' "$TMP/start-failure.out"

: >"$LOG"
if MOCK_TARGET_HEALTH_FAIL=1 MOCK_ROLLBACK_RUN_FAIL=1 AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" update --data-root "$TMP/data" --server-url https://api.example.test --version example/runner:new >"$TMP/rollback-start-failure.out" 2>&1; then
  echo 'expected failed rollback start' >&2; exit 1
fi
grep -Fq 'Update failed; rollback also failed. Manual recovery is required.' "$TMP/rollback-start-failure.out"

TEST_REPOSITORY='localhost:5000/automaton/runner'
TEST_PINNED_IMAGE="$TEST_REPOSITORY@sha256:$TEST_DIGEST"
TEST_TAGGED_IMAGE='registry.example.test:5443/automaton/runner:release-2026.09'
printf '{"InstallationId":"test-installation-id"}\n' >"$TMP/data/config/installation-profile.json"
printf '{"RunnerId":"test-runner-id","RunnerHostId":"test-runner-host-id"}\n' >"$TMP/data/config/runner.json"
printf 'test-installation-id\n' >"$TMP/data/config/installation-id"
: >"$LOG"
MOCK_REPO_DIGEST="$TEST_PINNED_IMAGE" AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" update --data-root "$TMP/data" --server-url https://api.example.test --version "$TEST_PINNED_IMAGE"
grep -Fq "pull $TEST_PINNED_IMAGE" "$LOG"
grep -Fq -- "--label automaton.runner-version=$TEST_DIGEST" "$LOG" || {
  echo 'the pinned digest label was corrupted by registry-port parsing' >&2; exit 1
}
[[ "$(cat "$TMP/data/config/runner.json")" == '{"RunnerId":"test-runner-id","RunnerHostId":"test-runner-host-id"}' ]]
[[ "$(cat "$TMP/data/config/installation-profile.json")" == '{"InstallationId":"test-installation-id"}' ]]
[[ "$(cat "$TMP/data/config/installation-id")" == 'test-installation-id' ]]

: >"$LOG"
MOCK_REPO_DIGEST="$TEST_REPOSITORY@sha256:$TEST_DIGEST" AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" update --data-root "$TMP/data" --server-url https://api.example.test --version "$TEST_TAGGED_IMAGE"
grep -Fq "pull $TEST_TAGGED_IMAGE" "$LOG"

expect_invalid_image_reference() {
  local image="$1" name="$2"
  local output="$TMP/$name.out"
  cp "$TMP/data/config/container-specification.env" "$TMP/container-specification.before-$name"
  cp "$TMP/data/config/runner.json" "$TMP/runner.before-$name"
  cp "$TMP/data/config/installation-profile.json" "$TMP/profile.before-$name"
  cp "$TMP/data/config/installation-id" "$TMP/installation-id.before-$name"
  : >"$LOG"
  : >"$CURL_LOG"
  if AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" repair --data-root "$TMP/data" --server-url https://api.example.test --version "$image" >"$output" 2>&1; then
    echo "expected image reference '$name' to fail" >&2; exit 1
  fi
  grep -Fq 'Invalid Infrastructure Runner image reference' "$output"
  ! grep -Eq '^(pull |rm -f |run )' "$LOG"
  [[ ! -s "$CURL_LOG" ]]
  cmp -s "$TMP/container-specification.before-$name" "$TMP/data/config/container-specification.env"
  cmp -s "$TMP/runner.before-$name" "$TMP/data/config/runner.json"
  cmp -s "$TMP/profile.before-$name" "$TMP/data/config/installation-profile.json"
  cmp -s "$TMP/installation-id.before-$name" "$TMP/data/config/installation-id"
}

expect_invalid_image_reference "sha256:$TEST_DIGEST" bare-digest
expect_invalid_image_reference "$TEST_REPOSITORY@sha256:${TEST_DIGEST%?}" malformed-digest
expect_invalid_image_reference $'example/runner@sha256:'"$TEST_DIGEST"$'\r' crlf-contaminated

: >"$LOG"
if MOCK_PULL_FAIL=1 AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" update --data-root "$TMP/data" --server-url https://api.example.test --version "$TEST_TAGGED_IMAGE" >"$TMP/pull-failure.out" 2>&1; then
  echo 'expected image pull failure' >&2; exit 1
fi
grep -Fq "pull $TEST_TAGGED_IMAGE" "$LOG"
! grep -Eq '^(rm -f |run )' "$LOG"
[[ "$(cat "$TMP/data/config/runner.json")" == '{"RunnerId":"test-runner-id","RunnerHostId":"test-runner-host-id"}' ]]
[[ "$(cat "$TMP/data/config/installation-profile.json")" == '{"InstallationId":"test-installation-id"}' ]]
[[ "$(cat "$TMP/data/config/installation-id")" == 'test-installation-id' ]]

# A held host lock rejects a competing lifecycle operation.
exec 8>"$TMP/data/update.lock"; flock -n 8
if AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" repair --data-root "$TMP/data" --server-url https://api.example.test; then
  echo 'expected lock contention failure' >&2; exit 1
fi

echo 'Infrastructure Runner installer behavioral checks passed.'
