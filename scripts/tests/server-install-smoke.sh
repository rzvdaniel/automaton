#!/usr/bin/env bash
# Exercise a new release against isolated disposable Compose volumes, never production.
set -euo pipefail
[[ "${AUTOMATON_RUN_INSTALL_SMOKE:-}" == 1 ]] || { echo 'Set AUTOMATON_RUN_INSTALL_SMOKE=1 to run this isolated Docker test.' >&2; exit 1; }
bundle=$(realpath "${1:?Pass a release bundle directory}")
fixture=$(mktemp -d)
project="automaton-install-smoke-$$"
export CONFIG_DIR="$fixture/config"
export AUTOMATON_CLOUDFLARE_TOKEN=isolated-test-token-not-a-real-credential
runner_image=$(sed -n 's/^RUNNER_IMAGE=//p' "$bundle/release.env")
python3 "$bundle/configure.py" --config-dir "$CONFIG_DIR" --domain example.invalid --email admin@example.invalid --runner-image "$runner_image"
# Replace ACME only inside the disposable fixture; use Traefik's local test certificate.
python3 - "$CONFIG_DIR" <<'PYCONFIG'
import json, sys
from pathlib import Path
root=Path(sys.argv[1])
p=root/'traefik.yml'; config=json.loads(p.read_text()); config.pop('certificatesResolvers')
config['ping']={'entryPoint':'health'}; config['entryPoints']['health']={'address':':8082'}
p.write_text(json.dumps(config))
for name in ('installation.yml', 'store.yml'):
    p=root/'dynamic'/name; edge=json.loads(p.read_text()); edge.pop('tls',None)
    for group in ('http','tcp'):
        for router in edge.get(group, {}).get('routers', {}).values(): router['tls']={}
    p.write_text(json.dumps(edge))
PYCONFIG
# Preinstalled images may be selected for local validation; released manifests use digests.
cat > "$fixture/override.yaml" <<'YAML'
services:
  api:
    ports: ['127.0.0.1::8080']
  traefik:
    ports: !override ['127.0.0.1::443', '127.0.0.1::5671']
YAML
compose=(docker compose -p "$project" --env-file "$bundle/release.env" -f "$bundle/compose.yaml" -f "$fixture/override.yaml")
cleanup() { local status=$?; if (( status != 0 )); then "${compose[@]}" logs --tail 60 api worker traefik >&2; fi; "${compose[@]}" down -v --remove-orphans >/dev/null; rm -rf -- "$fixture"; }
trap cleanup EXIT
"${compose[@]}" up -d --wait --wait-timeout 600 mysql rabbitmq api worker studio store traefik
address="http://$("${compose[@]}" port api 8080)"
https_address="$("${compose[@]}" port traefik 443)"
curl -kfSs -H 'Host: studio.example.invalid' "https://$https_address/" | grep -q '<!doctype html>'
curl -kfSs -H 'Host: studio.example.invalid' "https://$https_address/runtime-config.js" | python3 -c 'import sys; data=sys.stdin.read(); assert "https://api.example.invalid" in data and "https://apps.example.invalid" in data'
curl -kfSsI -H 'Host: studio.example.invalid' "https://$https_address/runtime-config.js" | grep -qi 'cache-control: no-store'
curl -kfSs -H 'Host: store.example.invalid' "https://$https_address/apps/any-app" | grep -q '<!doctype html>'
curl -kfSs -H 'Host: store.example.invalid' "https://$https_address/runtime-config.js" | python3 -c 'import sys; data=sys.stdin.read(); assert "https://api.example.invalid" in data and "https://studio.example.invalid" in data'
curl -fSs -o /dev/null -D - -H 'Origin: https://store.example.invalid' "$address/api/public/marketplace/categories" | grep -qi 'access-control-allow-origin: https://store.example.invalid'
curl -kfSs -H 'Host: studio.example.invalid' "https://$https_address/api/install/status" | python3 -c 'import json,sys; assert not json.load(sys.stdin)["installationComplete"]'
python3 - "$("${compose[@]}" port traefik 5671)" <<'PYBROKER'
import socket, ssl, sys
host,port=sys.argv[1].rsplit(':',1)
context=ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT); context.check_hostname=False; context.verify_mode=ssl.CERT_NONE
with socket.create_connection((host,int(port)),timeout=10) as raw:
    with context.wrap_socket(raw,server_hostname='api.example.invalid') as broker:
        broker.sendall(b'AMQP\x00\x00\x09\x01')
        assert broker.recv(8), 'Broker TLS router did not forward the AMQP handshake'
PYBROKER
token=$(cat "$CONFIG_DIR/secrets/setup-token")
curl -fsS "$address/api/install/status" | python3 -c 'import json,sys; d=json.load(sys.stdin); assert not d["installationComplete"] and d["setupTokenRequired"]'
[[ $(curl -s -o /dev/null -w '%{http_code}' -X POST "$address/api/install/checks") == 403 ]]
curl -fsS -X POST -H "X-Automaton-Setup-Token: $token" "$address/api/install/checks" | python3 -c 'import json,sys; assert all(x["status"]!="error" for x in json.load(sys.stdin))'
python3 - "$CONFIG_DIR/appsettings.Production.json" <<'PYAUTH' | "${compose[@]}" exec -T api curl -fsS --retry 5 --retry-delay 2 --retry-all-errors --config - http://rabbitmq:15672/api/whoami | python3 -c 'import json,sys; assert "administrator" in json.load(sys.stdin)["tags"]'
import json,sys
broker=json.load(open(sys.argv[1]))['RabbitMq']
print('user = "' + broker['Username'] + ':' + broker['Password'] + '"')
PYAUTH
# Concurrent claims must produce exactly one administrator, even with valid tokens.
for attempt in 1 2; do
    curl -s -o "$fixture/claim-$attempt.json" -w '%{http_code}' -H 'Content-Type: application/json' \
        -H "X-Automaton-Setup-Token: $token" -d '{"name":"Initial Administrator","email":"owner@example.invalid","password":"Isolated-Setup-Test-123!"}' \
        "$address/api/install" > "$fixture/status-$attempt" &
done
wait
python3 - "$fixture" <<'PY'
from pathlib import Path
import sys
root=Path(sys.argv[1]); codes=[(root/f'status-{i}').read_text() for i in (1,2)]
assert codes.count('200')==1 and all(c in ('200','400','409') for c in codes), codes
PY
curl -fsS "$address/api/install/status" | python3 -c 'import json,sys; assert json.load(sys.stdin)["installationComplete"]'
[[ $(curl -s -o /dev/null -w '%{http_code}' -X POST -H "X-Automaton-Setup-Token: $token" "$address/api/install/checks") == 403 ]]
curl -fsS -H 'Content-Type: application/json' -d '{"email":"owner@example.invalid","password":"Isolated-Setup-Test-123!"}' "$address/api/Account/LoginUser" > "$fixture/login.json"
access_token=$(python3 - "$fixture/login.json" <<'PYTOKEN'
import json,sys
print(json.load(open(sys.argv[1]))['accessToken'])
PYTOKEN
)
curl -fsS -X POST -H "Authorization: Bearer $access_token" "$address/api/install/checks" | python3 -c 'import json,sys; assert all(x["status"]!="error" for x in json.load(sys.stdin))'
curl -fsS -H "Authorization: Bearer $access_token" -H 'Content-Type: application/json' -d '{"runnerName":"Installer smoke test","applicationProfile":"MachineManagement","acknowledgeHostAccess":true}' "$address/api/self-hosted-runners/installations" > "$fixture/runner-installation.json"
python3 - "$fixture/runner-installation.json" "$runner_image" <<'PYRUNNER'
import json,sys
response=json.load(open(sys.argv[1]))
assert response['applicationProfile']=='MachineManagement'
assert sys.argv[2] in response['installationCommand']
assert response['requiresHostAccessAcknowledgement']
PYRUNNER
# The persistent completion marker prevents setup reopening after later account changes.
"${compose[@]}" exec -T mysql sh -c 'MYSQL_PWD=$(cat /run/secrets/mysql_root_password) exec mysql -u root -N' > "$fixture/marker" <<'SQL'
SELECT COUNT(*) FROM automaton.Settings WHERE `Key` = 'Installation.Complete';
SQL
[[ "$(cat "$fixture/marker")" == 1 ]]
echo 'Isolated installation, readiness and concurrent administrator claim passed.'
