#!/usr/bin/env bash
# Installs one Automaton server release on a throwaway Hetzner server and checks it by script, then deletes it.
#   release-install-check.sh fresh            install $VERSION, run the checks
#   release-install-check.sh upgrade          install $PREVIOUS, update to $VERSION with the CLI, run the checks again
#   release-install-check.sh sweep            delete automaton-ci servers older than $MAX_AGE_MINUTES (default 120)
# Environment: HCLOUD_TOKEN (project "automaton-ci" only), CLOUDFLARE_DNS_TOKEN (Zone.DNS edit on the test domain only),
# VERSION / PREVIOUS (X.Y.Z), optional CI_DOMAIN (automatontest.fyi), CI_EMAIL, SERVER_TYPES ("cx23 cpx12"),
# LOCATIONS ("nbg1 fsn1 hel1"), RUN_ID. Secrets are never printed; the administrator password is generated here and
# lives only in a variable and a root-only file on the throwaway server.
set -euo pipefail

mode="${1:?Usage: release-install-check.sh fresh|upgrade|sweep}"
domain="${CI_DOMAIN:-automatontest.fyi}"
email="${CI_EMAIL:-admin@$domain}"
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
    cutoff=$(date -u -d "-${MAX_AGE_MINUTES:-120} minutes" +%s)
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
[[ "$mode" == fresh || "$mode" == upgrade ]] || fail "Unknown mode $mode"

work=$(mktemp -d); server_id=''; ssh_key_id=''; ip=''; zone_id=''
ssh_opts=(-i "$work/key" -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o LogLevel=ERROR -o ConnectTimeout=15)
remote() { ssh "${ssh_opts[@]}" "root@$ip" "$@"; }

dns_delete() {
    [[ -n "$zone_id" ]] || return 0
    local name id
    for name in "${names[@]}"; do
        for id in $(cf GET "zones/$zone_id/dns_records?type=A&name=$name.$domain" | jq -r '.result[].id'); do
            cf DELETE "zones/$zone_id/dns_records/$id" >/dev/null || true
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

first="$VERSION"; [[ "$mode" == upgrade ]] && first="${PREVIOUS:?PREVIOUS is required for upgrade}"
for v in "$first" "$VERSION"; do [[ "$v" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][a-zA-Z0-9.-]+)?$ ]] || fail "Bad version $v"; done
tag="server-v$first"

step "Create the server ($mode, install $tag)"
ssh-keygen -q -t ed25519 -N '' -f "$work/key"
ssh_key_id=$(hcloud ssh-key create --name "automaton-ci-$run_id-$mode" --public-key-from-file "$work/key.pub" --label "$label" -o json | jq -r '.ssh_key.id')
cat > "$work/user-data" <<YAML
#cloud-config
runcmd:
  - curl -fL https://github.com/rzvdaniel/automaton/releases/download/$tag/install-automaton.sh -o /root/install-automaton.sh
  - bash /root/install-automaton.sh --version $tag --unattended --domain $domain --email $email --admin-email $email
YAML
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

step 'DNS'
zone_id=$(cf GET "zones?name=$domain" | jq -r '.result[0].id // empty')
[[ -n "$zone_id" ]] || fail "Zone $domain not found for the Cloudflare token"
dns_delete
for name in "${names[@]}"; do
    cf POST "zones/$zone_id/dns_records" "$(jq -n --arg n "$name.$domain" --arg ip "$ip" '{type:"A",name:$n,content:$ip,ttl:60,proxied:false}')" >/dev/null
done

low_memory_note() {
    [[ "$server_type" == cpx12 ]] || return 0
    if remote 'dmesg 2>/dev/null | grep -qiE "out of memory|oom-kill|killed process"; grep -qiE "out of memory|Killed" /var/log/automaton-install.log 2>/dev/null' 2>/dev/null; then
        echo "::error::This looks like lack of memory: the cpx12 server (2 GB RAM) ran out of memory."
    fi
}
on_failure() {
    echo "--- last lines of the install log (it holds no secrets)"
    remote 'tail -n 40 /var/log/automaton-install.log' 2>/dev/null || true
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
publish=\$(automaton publish --yes --slug "ci-hello-\$RANDOM")
url=\$(grep -oE 'https://[^ ]+' <<< "\$publish" | head -1)
test -n "\$url"
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

if [[ "$mode" == upgrade ]]; then
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
summary "- $mode: passed"
echo "All checks passed ($mode)"
