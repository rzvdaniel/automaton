#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
INSTALLER="$ROOT/scripts/install-infrastructure-runner.sh"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
mkdir -p "$TMP/bin" "$TMP/data/config"
LOG="$TMP/docker.log"; export LOG

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
if [[ "$1" == image && "$2" == inspect ]]; then echo 'example/runner@sha256:new'; exit 0; fi
if [[ "$1" == inspect ]]; then
  case "$*" in
    *'.Config.Image'*) echo 'example/runner@sha256:old' ;;
    *'.State.Running'*) echo true ;;
    *'.Config.Env'*) echo 'AUTOMATON_SERVER_URL=https://api.example.test' ;;
  esac
  exit 0
fi
if [[ "$1" == exec ]]; then exit 0; fi
if [[ "$1" == logs ]]; then echo 'Successfully connected to server'; exit 0; fi
exit 0
EOF
chmod +x "$TMP/bin"/*
PATH="$TMP/bin:$PATH"

# Repair uses the installed immutable image, not the default mutable tag.
AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" repair --data-root "$TMP/data" --server-url https://api.example.test
grep -Fq 'pull example/runner@sha256:old' "$LOG"
! grep -Fq 'pull automaton-runner:latest' "$LOG"

# A failed replacement resolves before it removes the durable identity.
printf 'existing-identity' >"$TMP/data/config/installation-id"
if MOCK_PULL_FAIL=1 AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" replace --data-root "$TMP/data" --server-url https://api.example.test --enrollment-key key --version example/runner:bad; then
  echo 'expected replacement pull failure' >&2; exit 1
fi
[[ "$(cat "$TMP/data/config/installation-id")" == existing-identity ]]

# A held host lock rejects a competing lifecycle operation.
exec 8>"$TMP/data/update.lock"; flock -n 8
if AUTOMATON_ROOT="$TMP/root" bash "$INSTALLER" repair --data-root "$TMP/data" --server-url https://api.example.test; then
  echo 'expected lock contention failure' >&2; exit 1
fi

echo 'Infrastructure Runner installer behavioral checks passed.'
