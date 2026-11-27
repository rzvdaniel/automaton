#!/usr/bin/env bash
# Installs one Automaton server release on a throwaway Hetzner server and checks it by script, then deletes it.
#   release-install-check.sh fresh            install $VERSION, run the checks
#   release-install-check.sh upgrade          install $PREVIOUS, update to $VERSION with the CLI, run the checks again
#   release-install-check.sh restore          install $VERSION, create an app and a backup, rebuild the same server with
#                                             a fresh install, restore the backup there with its key, check user, app and app data
#   release-install-check.sh all              the three in one run on ONE server: upgrade $PREVIOUS to $VERSION and scan, back up,
#                                             rebuild (hcloud server rebuild), fresh install and scan, restore, check, delete
#   release-install-check.sh fullscan        install $VERSION, run the checks, then the weekly signed-in full ZAP scan (release-full-scan.sh,
#                                             active scan: junk data, so only here on a throwaway server); needs API_SPEC
#   release-install-check.sh sweep            delete automaton-ci servers older than $MAX_AGE_MINUTES (default 180)
# Environment: HCLOUD_TOKEN (project "automaton-ci" only), CLOUDFLARE_DNS_TOKEN (Zone.DNS edit on the test domain only),
# API_SPEC (path of the release's openapi.json: the fresh scan also scans the whole API signed in), VERSION / PREVIOUS (X.Y.Z), optional CI_DOMAIN (automatontest.fyi), CI_EMAIL, SERVER_TYPES ("cx23 cpx12"),
# LOCATIONS ("nbg1 fsn1 hel1"), CI_TLS_CERT / CI_TLS_KEY (PEM of the wildcard for *.ci.$CI_DOMAIN and *.apps.ci.$CI_DOMAIN, from
# ci-certificate.yml: installs with --tls-mode custom on the fixed ci names and asks Let's Encrypt for nothing; an installer
# that predates custom mode, or no certificate, falls back to one Let's Encrypt sub-domain per run), RUN_ID, RESTORE_WRONG_KEY=1 (restore mode: use a wrong key, the job must fail). Secrets are never printed; the administrator password is generated here and
# lives only in a variable and a root-only file on the throwaway server.
set -euo pipefail

mode="${1:?Usage: release-install-check.sh fresh|upgrade|restore|all|fullscan|sweep}"
zone_name="${CI_DOMAIN:-automatontest.fyi}"
# One sub-domain per run and mode: Let's Encrypt allows five certificates a week for the same set of names, and every
# throwaway install asks for new ones.
domain="${mode:0:1}${RUN_ID:-local}-$$.$zone_name"
domain="${domain,,}"
email="${CI_EMAIL:-admin@$zone_name}"
types="${SERVER_TYPES:-cx23 cpx12}"
locations="${LOCATIONS:-nbg1 fsn1 hel1}"
run_id="${RUN_ID:-local-$$}"
label='automaton-ci=true'
names=(api studio store apps '*.apps')

step() { printf '\n== %s\n' "$*"; }
fail() { printf '::error::%s\n' "$*" >&2; exit 1; }
summary() { if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then printf '%s\n' "$*" >> "$GITHUB_STEP_SUMMARY"; fi; }

cf() { # cf METHOD PATH [json]
    curl -fsS -X "$1" "https://api.cloudflare.com/client/v4/$2" -H "Authorization: Bearer $CLOUDFLARE_DNS_TOKEN" \
        -H 'Content-Type: application/json' ${3:+--data "$3"}
}

sweep() {
    local cutoff
    cutoff=$(date -u -d "-${MAX_AGE_MINUTES:-180} minutes" +%s)
    hcloud server list -l "$label" -o json | jq -r '.[] | "\(.id) \(.created)"' | while read -r id created; do
        if (( $(date -u -d "$created" +%s) < cutoff )); then
            echo "Deleting $id created $created"; hcloud server delete "$id"
        fi
    done
    hcloud ssh-key list -l "$label" -o json | jq -r '.[] | "\(.id) \(.created)"' | while read -r id created; do
        if (( $(date -u -d "$created" +%s) < cutoff )); then hcloud ssh-key delete "$id"; fi
    done
}
if [[ "$mode" == sweep ]]; then sweep; exit 0; fi
[[ "$mode" == fresh || "$mode" == upgrade || "$mode" == restore || "$mode" == all || "$mode" == fullscan ]] || fail "Unknown mode $mode"

work=$(mktemp -d); server_id=''; ssh_key_id=''; ip=''; zone_id=''; extra_domains=()
ssh_opts=(-i "$work/key" -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o LogLevel=ERROR -o ConnectTimeout=15)
remote() { ssh "${ssh_opts[@]}" "root@$ip" "$@"; }

dns_delete() {
    [[ -n "$zone_id" ]] || return 0
    local name id d
    for d in "$domain" "${extra_domains[@]}"; do
        for name in "${names[@]}"; do
            for id in $(cf GET "zones/$zone_id/dns_records?type=A&name=$name.$d" | jq -r '.result[].id'); do
                cf DELETE "zones/$zone_id/dns_records/$id" >/dev/null || true
            done
        done
    done
}
cleanup() {
    set +e
    step 'Cleanup'
    [[ -n "$server_id" ]] && hcloud server delete "$server_id"
    [[ -n "$ssh_key_id" ]] && hcloud ssh-key delete "$ssh_key_id"
    dns_delete
    rm -rf "$work"
}
trap cleanup EXIT

first="$VERSION"; [[ "$mode" == upgrade || "$mode" == all ]] && first="${PREVIOUS:?PREVIOUS is required for upgrade}"
for v in "$first" "$VERSION"; do [[ "$v" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][a-zA-Z0-9.-]+)?$ ]] || fail "Bad version $v"; done
tag="server-v$first"

tls_args=''; write_files=''
if [[ -n "${CI_TLS_CERT:-}" && -n "${CI_TLS_KEY:-}" ]] \
    && curl -fsSL "https://github.com/rzvdaniel/automaton/releases/download/$tag/install-automaton.sh" | grep -q -- '--cert-file'; then
    # The runs are one at a time (workflow concurrency group), so the fixed names never collide.
    domain="ci.$zone_name"
    tls_args='--tls-mode custom --cert-file /root/ci-cert.pem --key-file /root/ci-key.pem'
    # The certificate is for this throwaway server only; user data of a server in the CI project dies with it.
    write_files=$(printf 'write_files:\n  - path: /root/ci-cert.pem\n    permissions: "0644"\n    encoding: b64\n    content: %s\n  - path: /root/ci-key.pem\n    permissions: "0600"\n    encoding: b64\n    content: %s\n' \
        "$(printf '%s' "$CI_TLS_CERT" | base64 -w0)" "$(printf '%s' "$CI_TLS_KEY" | base64 -w0)")
    echo "Installing with the CI certificate (--tls-mode custom) on $domain"
else
    echo "Installing with Let's Encrypt on $domain (no CI certificate, or the release predates --tls-mode custom)"
fi

make_user_data() { # make_user_data TAG DOMAIN FILE: cloud-config that installs the release; also puts our SSH key on the server (a rebuild does not)
    cat > "$3" <<YAML
#cloud-config
disable_root: false  # lets the key below reach root; do not remove
ssh_authorized_keys:
  - $(cat "$work/key.pub")
$write_files
runcmd:
  - curl -fL https://github.com/rzvdaniel/automaton/releases/download/$1/install-automaton.sh -o /root/install-automaton.sh
  - bash /root/install-automaton.sh --version $1 --unattended --domain $2 --email $email --admin-email $email $tls_args
YAML
}

step "Create the server ($mode, install $tag)"
ssh-keygen -q -t ed25519 -N '' -f "$work/key"
ssh_key_id=$(hcloud ssh-key create --name "automaton-ci-$run_id-$mode" --public-key-from-file "$work/key.pub" --label "$label" -o json | jq -r '.ssh_key.id')
make_user_data "$tag" "$domain" "$work/user-data"
server_type=''; location=''
for t in $types; do
    for l in $locations; do
        if out=$(hcloud server create --name "automaton-ci-$run_id-$mode" --type "$t" --location "$l" --image ubuntu-24.04 \
                --ssh-key "$ssh_key_id" --user-data-from-file "$work/user-data" --label "$label" --label "run=$run_id" -o json 2>"$work/err"); then
            server_id=$(jq -r '.server.id' <<< "$out"); ip=$(jq -r '.server.public_net.ipv4.ip' <<< "$out")
            server_type="$t"; location="$l"; break 2
        fi
        echo "Could not create $t in $l: $(head -c 300 "$work/err")"
    done
done
[[ -n "$server_id" ]] || fail "No server could be created from types: $types in locations: $locations"
echo "Running on $server_type in $location"
summary "- $mode: server type \`$server_type\`, location \`$location\`"

point_dns() { # the names of $domain to $ip, after deleting what is there
    step 'DNS'
    zone_id=$(cf GET "zones?name=$zone_name" | jq -r '.result[0].id // empty')
    [[ -n "$zone_id" ]] || fail "Zone $zone_name not found for the Cloudflare token"
    dns_delete
    for name in "${names[@]}"; do
        cf POST "zones/$zone_id/dns_records" "$(jq -n --arg n "$name.$domain" --arg ip "$ip" '{type:"A",name:$n,content:$ip,ttl:60,proxied:false}')" >/dev/null
    done
}
point_dns

low_memory_note() {
    [[ "$server_type" == cpx12 ]] || return 0
    if remote 'dmesg 2>/dev/null | grep -qiE "out of memory|oom-kill|killed process"; grep -qiE "out of memory|Killed" /var/log/automaton-install.log 2>/dev/null' 2>/dev/null; then
        echo "::error::This looks like lack of memory: the cpx12 server (2 GB RAM) ran out of memory."
    fi
}
on_failure() {
    echo "--- last lines of the install log (it holds no secrets)"
    remote "tail -n 40 /var/log/automaton-install.log | grep -viE 'token|secret|password|key|link|code='" 2>/dev/null || true
    low_memory_note
}

wait_healthy() { # until the API and Studio answer, the install log says finished or time runs out
    local deadline=$(( $(date +%s) + 2700 ))
    until [[ "$(curl -s -o /dev/null -w '%{http_code}' "https://api.$domain/health" || true)" == 200 ]]; do
        (( $(date +%s) < deadline )) || { on_failure; fail 'The server did not become healthy within 45 minutes after first boot'; }
        sleep 20
    done
}

wait_ssh() {
    local n
    for n in $(seq 1 30); do remote true 2>/dev/null && return 0; sleep 10; done
    fail 'SSH to the new server never answered'
}

checks() { # checks LABEL
    step "Checks ($1)"
    # The certificate that Studio really serves: the Let's Encrypt run shows its issuer, the custom run the CI one.
    issuer=$(openssl s_client -connect "studio.$domain:443" -servername "studio.$domain" </dev/null 2>/dev/null | openssl x509 -noout -issuer 2>/dev/null | sed 's/^issuer= *//' || true)
    summary "- $1: certificate issuer \`${issuer:-unknown}\` ($([[ -n "$tls_args" ]] && echo custom || echo "Let's Encrypt"))"
    code() { curl -s -o /dev/null -w '%{http_code}' "$1" || true; }
    [[ "$(code "https://api.$domain/health")" == 200 ]] || fail "$1: API health is not 200"
    [[ "$(code "https://api.$domain/api/install/status")" == 200 ]] || fail "$1: API install status is not 200"
    [[ "$(code "https://studio.$domain/")" == 200 ]] || fail "$1: Studio is not 200"

    if [[ -z "${password:-}" ]]; then
        step 'Set the administrator password from the one-time link'
        link=$(remote "sed -n 's#^\(https://[^ ]*\)\$#\1#p' /root/automaton-admin-link.txt | head -1")
        [[ -n "$link" ]] || fail "$1: /root/automaton-admin-link.txt has no link"
        code_param=$(python3 -I -c 'import sys,urllib.parse as u; print(u.parse_qs(u.urlparse(sys.argv[1]).query).get("code",[""])[0])' "$link")
        [[ -n "$code_param" ]] || fail "$1: the admin link has no code"
        password="Aa1!$(openssl rand -base64 24 | tr -d '\n=')"
        status=$(jq -n --arg e "$email" --arg p "$password" --arg c "$code_param" '{email:$e,password:$p,code:$c}' |
            curl -s -o /dev/null -w '%{http_code}' -X POST "https://api.$domain/api/Account/ResetUserPassword" -H 'Content-Type: application/json' --data-binary @-)
        [[ "$status" == 204 || "$status" == 200 ]] || fail "$1: setting the administrator password returned HTTP $status"
        printf '%s' "$password" | remote 'install -m 0600 /dev/stdin /root/ci-password'
    fi

    step 'CLI login, Runner, hello app'
    remote "bash -s" <<REMOTE || { on_failure; fail "$1: a CLI check failed (see the lines above)"; }
set -euo pipefail
export PATH="\$HOME/.local/bin:\$PATH"
command -v automaton >/dev/null || curl -fsSL https://github.com/rzvdaniel/automaton/releases/download/server-v$VERSION/install.sh | AUTOMATON_VERSION=server-v$VERSION sh
automaton login --server https://api.$domain --email '$email' < /root/ci-password >/dev/null
automaton whoami
for i in \$(seq 1 30); do automaton runners | grep -qw connected && break; sleep 10; done
automaton runners | grep -w main | grep -qw connected
rm -rf /root/hello && mkdir /root/hello && cd /root/hello
automaton new "CI Hello $1" --template static
automaton validate
automaton deploy --non-interactive
publish=\$(timeout 600 automaton publish --yes --slug "ci-hello-\$RANDOM") || {
    rc=\$?
    [ "\$rc" = 124 ] && echo "automaton publish did not finish within 10 minutes" >&2 || echo "automaton publish failed (exit \$rc)" >&2
    echo "--- publish output" >&2; printf '%s\n' "\$publish" | head -20 >&2
    echo "--- Traefik log lines for acme|rate" >&2
    for c in \$(docker ps -q --filter name=traefik); do docker logs --tail 400 "\$c" 2>&1 | grep -iE 'acme|rate' | tail -20 >&2 || true; done
    exit 1
}
url=\$(grep -oE 'https://[^ ]+' <<< "\$publish" | head -1)
test -n "\$url"
echo "\$url" > /root/ci-app-url
for i in \$(seq 1 30); do [ "\$(curl -s -o /dev/null -w '%{http_code}' "\$url")" = 200 ] && exit 0; sleep 10; done
echo "The published app at \$url did not answer 200" >&2; exit 1
REMOTE
    echo "Checks passed ($1)"
}

step 'Wait for first boot'
wait_ssh
wait_healthy
password=''
checks "fresh install of $first"

scan() { # scan LABEL [api]: "api" adds the signed-in scan of the whole API (fresh installs only)
    step "External scan ($1)"
    app_url=$(remote 'cat /root/ci-app-url' 2>/dev/null || true)
    [[ -n "$app_url" ]] || app_url="https://apps.$domain/"
    local api_args=()
    if [[ "${2:-}" == api && -n "${API_SPEC:-}" ]]; then
        # Signed in: a developer CLI key created on this server (the password is on the server only), used as the Authorization header.
        remote "export PATH=\"\$HOME/.local/bin:\$PATH\"; rm -f /root/ci-scan-key; automaton keys create --name ci-scan-$run_id --email '$email' --out /root/ci-scan-key < /root/ci-password >/dev/null" \
            || fail "$1: could not create the CLI key for the API scan"
        remote 'cat /root/ci-scan-key' > "$work/scan-key" || fail "$1: could not read the CLI key for the API scan"
        api_args=(--api-spec "$API_SPEC" --api-key-file "$work/scan-key")
    fi
    bash "$(dirname "${BASH_SOURCE[0]}")/release-scan.sh" "${api_args[@]}" "$domain" "$ip" "$app_url" || fail "The external scan found problems (see the summary above)"
    summary "- $1: scan passed"
}

if [[ "$mode" == upgrade || "$mode" == all ]]; then
    step "Upgrade $first to $VERSION"
    remote "bash -s" <<REMOTE || { on_failure; fail "Upgrade to $VERSION failed"; }
set -euo pipefail
export PATH="\$HOME/.local/bin:\$PATH"
automaton server update --version $VERSION --wait --timeout 1800
REMOTE
    wait_healthy
    remote 'rm -rf /root/hello'
    checks "after the upgrade to $VERSION"
fi
if [[ "$mode" == restore || "$mode" == all ]]; then
    [[ "$mode" == all ]] && scan "upgrade"
    step 'Create an app marker and a backup on the first install'
    app_id=$(remote 'export PATH="$HOME/.local/bin:$PATH"; automaton apps --json' | jq -r '[.apps[] | select((.name // .Name) | startswith("CI Hello"))][0] | (.id // .Id) // empty')
    [[ -n "$app_id" ]] || fail 'Restore check: the CI Hello app is not on the first install'
    remote "bash -s" <<REMOTE || { on_failure; fail 'Restore check: the first install could not write the app marker or take a backup'; }
set -euo pipefail
helper=\$(docker inspect --format '{{.Config.Image}}' \$(docker compose --project-directory /opt/automaton-server -f /opt/automaton-server/compose.yaml ps -aq api | head -1))
volume=\$(docker volume ls -q --filter label=automaton.resource=machine-workspace-volume | head -1)
test -n "\$volume"
docker run --rm --user 0:0 --entrypoint sh -v "\$volume:/v" "\$helper" -c 'mkdir -p /v/workspace/data/$app_id && echo restore-marker-$run_id > /v/workspace/data/$app_id/ci-marker'
bash /opt/automaton-server/backup.sh
ls -1t /opt/automaton-server/backups/nightly/*.tar.age | head -1 > /root/ci-archive
test -s /root/ci-archive
REMOTE
    archive_path=$(remote 'cat /root/ci-archive')
    # The backup key never goes into the log: a file in the private work directory, then a root-only file on the rebuilt server.
    remote 'cat /etc/automaton/secrets/backup-key' > "$work/backup-key" || fail 'Restore check: the first install has no backup key to restore with'
    [[ -s "$work/backup-key" ]] || fail 'Restore check: the backup key is empty'
    scp "${ssh_opts[@]}" "root@$ip:$archive_path" "$work/backup.tar.age" >/dev/null
    old_password="$password"

    step "Rebuild the same server (fresh install of $VERSION)"
    tag="server-v$VERSION"
    if [[ -z "$tls_args" ]]; then
        # Let's Encrypt fallback: new names for the new install (the CI certificate covers only the fixed ones).
        domain="r${RUN_ID:-local}-$$.$zone_name"; domain="${domain,,}"
        extra_domains+=("$domain")
    fi
    make_user_data "$tag" "$domain" "$work/user-data-b"
    hcloud server rebuild "$server_id" --image ubuntu-24.04 --user-data-from-file "$work/user-data-b" >/dev/null \
        || fail 'hcloud server rebuild failed'
    [[ -n "$tls_args" ]] || point_dns
    wait_ssh
    wait_healthy
    if [[ "$mode" == all ]]; then
        password=''
        checks "fresh install of $VERSION"
        scan "fresh install" api
    fi

    step 'Restore the backup with the key'
    scp "${ssh_opts[@]}" "$work/backup.tar.age" "root@$ip:/root/backup.tar.age" >/dev/null
    if [[ "${RESTORE_WRONG_KEY:-}" == 1 ]]; then
        echo 'RESTORE_WRONG_KEY=1: restoring with a freshly generated key that did not encrypt the backup.'
        remote 'image=$(grep -m1 "^AGE_IMAGE=" /opt/automaton-server/release.env | cut -d= -f2-); docker run --rm "$image" age-keygen 2>/dev/null | grep -v "^#" > /root/backup-key; chmod 600 /root/backup-key'
    else
        remote 'install -m 0600 /dev/stdin /root/backup-key' < "$work/backup-key"
    fi
    if ! remote "bash /opt/automaton-server/restore.sh --archive /root/backup.tar.age --key /root/backup-key --confirm $domain" > "$work/restore.log" 2>&1; then
        echo '--- restore output (it holds no secrets)'; tail -n 25 "$work/restore.log" | grep -viE 'token|secret|password|AGE-SECRET'
        fail 'Restore failed: restore.sh --archive --key did not finish on the rebuilt server (a wrong or unreadable key stops it at decryption)'
    fi
    wait_healthy

    step 'Checks on the restored server'
    code() { curl -s -o /dev/null -w '%{http_code}' "$1" || true; }
    [[ "$(code "https://studio.$domain/")" == 200 ]] || fail 'Restored server: Studio is not 200'
    remote "bash -s" <<REMOTE || { on_failure; fail 'Restored server: user, app or app data is missing (see the lines above)'; }
set -euo pipefail
export PATH="\$HOME/.local/bin:\$PATH"
command -v automaton >/dev/null || curl -fsSL https://github.com/rzvdaniel/automaton/releases/download/server-v$VERSION/install.sh | AUTOMATON_VERSION=server-v$VERSION sh
# The password of the first install: signing in with it proves the user came from the backup.
printf '%s' '$old_password' | automaton login --server https://api.$domain --email '$email' >/dev/null
automaton whoami
automaton apps --json | grep -q '$app_id'
volume=\$(docker volume ls -q --filter label=automaton.resource=machine-workspace-volume | head -1)
test -n "\$volume"
helper=\$(docker inspect --format '{{.Config.Image}}' \$(docker compose --project-directory /opt/automaton-server -f /opt/automaton-server/compose.yaml ps -aq api | head -1))
docker run --rm --entrypoint cat -v "\$volume:/v:ro" "\$helper" /v/workspace/data/$app_id/ci-marker | grep -q restore-marker-$run_id
REMOTE
    summary "- restore: passed (user, app and app data came back after the rebuild)"
    echo "All checks passed ($mode)"
    exit 0
fi
if [[ "$mode" == fullscan ]]; then
    step 'Signed-in full scan'
    [[ -n "${API_SPEC:-}" ]] || fail 'fullscan needs API_SPEC (the release openapi.json)'
    remote "export PATH=\"\$HOME/.local/bin:\$PATH\"; rm -f /root/ci-scan-key; automaton keys create --name ci-full-scan-$run_id --email '$email' --out /root/ci-scan-key < /root/ci-password >/dev/null" \
        || fail 'Full scan: could not create the CLI key'
    remote 'cat /root/ci-scan-key' > "$work/scan-key" || fail 'Full scan: could not read the CLI key'
    remote 'cat /root/ci-password' > "$work/admin-password" || fail 'Full scan: could not read the administrator password'
    FAILING_JSON="${FAILING_JSON:-}" bash "$(dirname "${BASH_SOURCE[0]}")/release-full-scan.sh" --api-spec "$API_SPEC" \
        --api-key-file "$work/scan-key" --password-file "$work/admin-password" "$domain" "$email" || fail 'The full scan found problems (see the summary above)'
    summary '- full scan: passed'
    exit 0
fi
scan "$mode" "$([[ "$mode" == fresh ]] && echo api)"
summary "- $mode: passed"
echo "All checks passed ($mode)"
