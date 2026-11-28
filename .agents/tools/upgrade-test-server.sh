#!/usr/bin/env bash
# Upgrade the test server to a published release and then upgrade any online Runners that need it (issue #1887).
# Runs on runner-01 (self-hosted, label `agent`) after server-release.yml publishes a release on main.
# Usage: upgrade-test-server.sh <version>   (e.g. 0.2.200 or server-v0.2.200)
# Env (overridable for tests):
#   UPGRADE_UPDATE_CMD      (default: sudo /usr/bin/bash /opt/automaton-server/update.sh)
#   UPGRADE_VERSION_FILE    (default: /opt/automaton-server/version)
#   AUTOMATON_TEST_ACCOUNTS (default: ~/.config/automaton-test/accounts.env)
#   UPGRADE_CURL            (default: curl)
#   UPGRADE_RUNNER_TIMEOUT  (default: 600)
#   UPGRADE_POLL_INTERVAL   (default: 10)
#   UPGRADE_RECONNECT_WAIT  (default: 30)
set -euo pipefail

raw_version=${1:?usage: upgrade-test-server.sh <version>}
version=${raw_version#server-v}
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][a-zA-Z0-9.-]+)?$ ]] || {
  echo "Invalid version: $raw_version" >&2
  exit 2
}
tag="server-v$version"

version_file=${UPGRADE_VERSION_FILE:-/opt/automaton-server/version}
accounts_file=${AUTOMATON_TEST_ACCOUNTS:-$HOME/.config/automaton-test/accounts.env}
curl_bin=${UPGRADE_CURL:-curl}
runner_timeout=${UPGRADE_RUNNER_TIMEOUT:-600}
poll_interval=${UPGRADE_POLL_INTERVAL:-10}
reconnect_wait=${UPGRADE_RECONNECT_WAIT:-30}

read_version() {
  (tr -d '[:space:]' < "$version_file") 2>/dev/null || true
}

installed=$(read_version)
if [[ "$installed" == "$tag" ]]; then
  echo "Test server is already on $tag."
else
  echo "Upgrading test server from ${installed:-unknown} to $tag."
  if [[ -n "${UPGRADE_UPDATE_CMD:-}" ]]; then
    read -r -a update_cmd <<< "$UPGRADE_UPDATE_CMD"
  else
    update_cmd=(sudo /usr/bin/bash /opt/automaton-server/update.sh)
  fi
  "${update_cmd[@]}" --version "$tag"
  installed=$(read_version)
  [[ "$installed" == "$tag" ]] || {
    echo "Expected $version_file to be $tag after update, got: ${installed:-empty}" >&2
    exit 1
  }
  echo "Test server upgraded to $tag."
fi

[[ -f "$accounts_file" ]] || {
  echo "Test accounts file not found: $accounts_file" >&2
  exit 1
}

get_account() {
  grep -m1 "^$1=" "$accounts_file" | cut -d= -f2- | tr -d '\r'
}

api_url=$(get_account AUTOMATON_TEST_API_URL)
api_url=${api_url%/}
email=$(get_account AUTOMATON_TEST_ADMIN_EMAIL)
password=$(get_account AUTOMATON_TEST_ADMIN_PASSWORD)
[[ -n "$api_url" && -n "$email" && -n "$password" ]] || {
  echo "Missing AUTOMATON_TEST_API_URL or admin credentials in $accounts_file" >&2
  exit 1
}

login_payload=$(jq -n --arg email "$email" --arg password "$password" '{email: $email, password: $password}')
login_response=$("$curl_bin" -fsS -X POST "$api_url/api/Account/LoginUser" \
  -H 'Content-Type: application/json' \
  --data-raw "$login_payload")
token=$(jq -r '.accessToken // empty' <<< "$login_response")
[[ -n "$token" ]] || {
  echo "Failed to sign in to $api_url/api/Account/LoginUser." >&2
  exit 1
}

# After an API restart, give the Runner a brief window to reconnect before checking online status.
deadline=$((SECONDS + reconnect_wait))
runners_json='[]'
while true; do
  runners_json=$("$curl_bin" -fsS "$api_url/api/runners" -H "Authorization: Bearer $token")
  total=$(jq 'length' <<< "$runners_json")
  online=$(jq '[.[] | select(.status == "Online" or .status == 1)] | length' <<< "$runners_json")
  if [[ "$total" -eq 0 || "$online" -gt 0 || "$SECONDS" -ge "$deadline" ]]; then
    break
  fi
  sleep "$poll_interval"
done

mapfile -t to_upgrade < <(jq -r '.[] | select((.status == "Online" or .status == 1) and .upgradeAvailable == true) | "\(.id)\t\(.name // .id)"' <<< "$runners_json")

if [[ "${#to_upgrade[@]}" -eq 0 ]]; then
  echo "All online Runners are already on the current image."
  exit 0
fi

for entry in "${to_upgrade[@]}"; do
  runner_id=${entry%%$'\t'*}
  runner_name=${entry#*$'\t'}
  echo "Starting Runner upgrade for $runner_name ($runner_id)."
  start_json=$("$curl_bin" -fsS -X POST "$api_url/api/runners/$runner_id/upgrade" \
    -H "Authorization: Bearer $token")
  state=$(jq -r '.state // empty' <<< "$start_json")
  if [[ "$state" == "Reconnected" ]]; then
    echo "Runner $runner_name is already on the current image."
    continue
  fi

  upgrade_deadline=$((SECONDS + runner_timeout))
  while true; do
    status_json=$("$curl_bin" -fsS "$api_url/api/runners/$runner_id/upgrade" \
      -H "Authorization: Bearer $token")
    state=$(jq -r '.state // empty' <<< "$status_json")
    message=$(jq -r '.message // empty' <<< "$status_json")
    case "$state" in
      Reconnected)
        echo "Runner $runner_name upgraded and reconnected."
        break
        ;;
      RolledBack)
        echo "Runner $runner_name upgrade rolled back: $message" >&2
        exit 1
        ;;
      InProgress)
        if [[ "$SECONDS" -ge "$upgrade_deadline" ]]; then
          echo "Timed out waiting for Runner $runner_name to upgrade: $message" >&2
          exit 1
        fi
        sleep "$poll_interval"
        ;;
      *)
        echo "Unexpected upgrade state for Runner $runner_name: ${state:-empty} ($message)" >&2
        exit 1
        ;;
    esac
  done
done
