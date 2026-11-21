#!/usr/bin/env bash
# Prove that a backup made by backup.sh restores with restore.sh on an isolated, disposable installation:
# install, create data, back up, wipe containers and volumes, restore, check the data is back. Never touches
# a real installation: everything lives in a temporary directory and a uniquely named Compose project.
#
#   AUTOMATON_RUN_INSTALL_SMOKE=1 sudo -E bash scripts/tests/server-restore-smoke.sh <release bundle directory>
set -euo pipefail
[[ "${AUTOMATON_RUN_INSTALL_SMOKE:-}" == 1 ]] || { echo 'Set AUTOMATON_RUN_INSTALL_SMOKE=1 to run this isolated Docker test.' >&2; exit 1; }
[[ $EUID -eq 0 ]] || { echo 'Run as root (sudo), as restore.sh requires.' >&2; exit 1; }
bundle=$(realpath "${1:?Pass a release bundle directory}")
fixture=$(mktemp -d)
install_dir="$fixture/automaton-restore-smoke-$$"
export CONFIG_DIR="$fixture/config"
export BACKUP_DIR="$install_dir/backups"
export AUTOMATON_CLOUDFLARE_TOKEN=isolated-test-token-not-a-real-credential
domain=example.invalid
password='Isolated-Setup-Test-123!'
mkdir -p "$install_dir"
cp -r "$bundle"/. "$install_dir"/
printf 'CONFIG_DIR=%s\nBACKUP_DIR=%s\n' "$CONFIG_DIR" "$BACKUP_DIR" > "$install_dir/.env"
runner_image=$(sed -n 's/^RUNNER_IMAGE=//p' "$install_dir/release.env")
python3 "$install_dir/configure.py" --config-dir "$CONFIG_DIR" --domain "$domain" --email admin@example.invalid --runner-image "$runner_image"
# Use Traefik's local test certificate and a loopback port, only inside the fixture.
python3 - "$CONFIG_DIR" <<'PYCONFIG'
import json, sys
from pathlib import Path
root = Path(sys.argv[1])
p = root / 'traefik.yml'; config = json.loads(p.read_text()); config.pop('certificatesResolvers')
p.write_text(json.dumps(config))
for name in ('installation.yml', 'store.yml'):
    p = root / 'dynamic' / name; edge = json.loads(p.read_text()); edge.pop('tls', None)
    for group in ('http', 'tcp'):
        for router in edge.get(group, {}).get('routers', {}).values():
            router['tls'] = {}
    p.write_text(json.dumps(edge))
PYCONFIG
cat > "$install_dir/compose.override.yaml" <<'YAML'
services:
  api:
    ports: ['127.0.0.1::8080']
  traefik:
    ports: !override ['127.0.0.1::443', '127.0.0.1::5671']
YAML
compose=(docker compose --project-directory "$install_dir" --env-file "$install_dir/release.env" --env-file "$install_dir/.env" -f "$install_dir/compose.yaml" -f "$install_dir/compose.override.yaml")
printf 'server-v0.0.0-restore-smoke\n' > "$install_dir/version"
cleanup() {
    local status=$?
    if (( status != 0 )); then "${compose[@]}" logs --tail 60 api worker postgres >&2 || true; fi
    "${compose[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
    rm -rf -- "$fixture"
}
trap cleanup EXIT

start() { "${compose[@]}" up -d --wait --wait-timeout 600 postgres rabbitmq api worker studio store traefik; }
api_address() { echo "http://$("${compose[@]}" port api 8080)"; }
login() {
    curl -fsS -H 'Content-Type: application/json' -d "{\"email\":\"owner@example.invalid\",\"password\":\"$password\"}" "$(api_address)/api/Account/LoginUser" \
        | python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])'
}
installation_marker_count() {
    "${compose[@]}" exec -T postgres psql -U automaton -d automaton -At -c "SELECT COUNT(*) FROM \"Settings\" WHERE \"Key\" = 'Installation.Complete'" < /dev/null
}

echo 'Installing and creating data...'
start
address=$(api_address)
token=$(cat "$CONFIG_DIR/secrets/setup-token")
curl -fsS -H 'Content-Type: application/json' -H "X-Automaton-Setup-Token: $token" \
    -d "{\"name\":\"Initial Administrator\",\"email\":\"owner@example.invalid\",\"password\":\"$password\"}" "$address/api/install" >/dev/null
login >/dev/null
[[ "$(installation_marker_count)" == 1 ]]

echo 'Backing up...'
bash "$install_dir/backup.sh" --install-dir "$install_dir" --keep 5
backup=$(bash "$install_dir/backup.sh" --install-dir "$install_dir" --list | head -1)
[[ "$backup" =~ ^automaton-[0-9]{8}T[0-9]{6}Z$ ]] || { echo "No backup was made: $backup" >&2; exit 1; }
# Keep the backup outside the volumes and the install directory's data, then wipe the server.
mv "$install_dir/backups/nightly/$backup" "$fixture/$backup"

echo 'Wiping containers and volumes...'
"${compose[@]}" down -v --remove-orphans
start
# A wiped server has no administrator: the data is really gone before the restore.
curl -fsS "$(api_address)/api/install/status" | python3 -c 'import json,sys; assert not json.load(sys.stdin)["installationComplete"]'
mv "$fixture/$backup" "$install_dir/backups/nightly/$backup"

echo 'Restoring...'
# restore.sh checks health at https://studio.<domain>, which does not resolve here: send it to the local Traefik port.
https_address=$("${compose[@]}" port traefik 443)
shim="$fixture/bin"
mkdir -p "$shim"
real_curl=$(command -v curl)
cat > "$shim/curl" <<SHIM
#!/usr/bin/env bash
exec "$real_curl" -k --connect-to "studio.$domain:443:${https_address%:*}:${https_address##*:}" "\$@"
SHIM
chmod +x "$shim/curl"
mkdir -p "$CONFIG_DIR"
PATH="$shim:$PATH" bash "$install_dir/restore.sh" "$backup" --install-dir "$install_dir" --confirm "$domain"

echo 'Checking the data is back...'
curl -fsS "$(api_address)/api/install/status" | python3 -c 'import json,sys; assert json.load(sys.stdin)["installationComplete"]'
login >/dev/null
[[ "$(installation_marker_count)" == 1 ]]
echo 'Backup and restore round trip passed.'
