#!/usr/bin/env bash
# Checks uninstall-automaton.sh against two installs inside a throwaway Docker-in-Docker container (privileged, its own daemon, removed at the end).
# The script is never run on this machine. Stand-in containers carry the labels Compose gives the real stack.
set -u
here=$(cd "$(dirname "$0")" && pwd)
command -v docker >/dev/null && docker info >/dev/null 2>&1 || { echo 'SKIP: needs Docker to start the throwaway container'; exit 0; }
ctr=automaton-uninstall-test-$$
trap 'docker rm -f "$ctr" >/dev/null 2>&1' EXIT
docker run -d --privileged --name "$ctr" docker:dind >/dev/null || exit 1
for _ in $(seq 1 60); do docker exec "$ctr" docker info >/dev/null 2>&1 && break; sleep 1; done
docker exec "$ctr" docker info >/dev/null 2>&1 || { echo 'FAIL: inner Docker did not start'; exit 1; }
docker exec "$ctr" apk add --no-cache bash python3 util-linux findutils coreutils grep sed >/dev/null || { echo 'FAIL: apk add'; exit 1; }
(docker image inspect busybox:1.37 >/dev/null 2>&1 || docker pull -q busybox:1.37 >/dev/null) && docker save busybox:1.37 | docker exec -i "$ctr" docker load >/dev/null || { echo 'FAIL: busybox'; exit 1; }
docker cp "$here/../uninstall-automaton.sh" "$ctr:/uninstall-automaton.sh"
docker exec -i "$ctr" bash -s <<'INNER'
set -u
fail=0
t() { "$@" >/dev/null || { echo "FAIL: $*"; cat /out 2>/dev/null; fail=1; }; }
no() { if "$@" >/dev/null 2>&1; then echo "FAIL (should not hold): $*"; fail=1; fi; }
un() { bash /uninstall-automaton.sh "$@" >/out 2>&1; }
mk() { # dir cfg domain: an install with a recognisable compose file and configuration
    mkdir -p "$1/backups/nightly" "$2/secrets"
    printf 'name: automaton-server\n' > "$1/compose.yaml"; touch "$1/update.sh" "$1/marker-file" "$1/backups/old"
    printf '{"domain":"%s"}' "$3" > "$2/installation.json"; echo key-of-$3 > "$2/secrets/backup-key"; echo age1recipient-of-$3 > "$2/backup-recipient"
    printf '#!/bin/bash\necho archive > "%s/backups/nightly/automaton-20260101T000000Z.tar.age"\n' "$1" > "$1/backup.sh"
}
stack() { # name dir: a stand-in server container labelled like Compose, from that directory
    docker run -d --name "$1" --label com.docker.compose.project=automaton-server --label "com.docker.compose.project.working_dir=$2" busybox:1.37 sleep 3600 >/dev/null
}
running() { [[ $(docker inspect -f '{{.State.Running}}' "$1" 2>/dev/null) == true ]]; }
docker volume create --label com.docker.compose.project=automaton-server automaton-server_postgres-data >/dev/null
mk /opt/automaton-server /etc/automaton a.test; mk /srv/b /srv/bcfg b.test
mkdir -p /var/lib/automaton/apps; touch /var/lib/automaton/apps/data
mkdir -p /etc/systemd/system; printf 'ExecStart=/usr/bin/env bash /opt/automaton-server/backup.sh\n' > /etc/systemd/system/automaton-backup.service
stack a1 /opt/automaton-server; stack b1 /srv/b
# Deleting data needs --confirm; --yes does not confirm it.
no un --delete-data --yes --install-dir /srv/b --config-dir /srv/bcfg
grep -q -- '--confirm b.test' /out || { echo 'FAIL: no hint about --confirm'; fail=1; }
no un --delete-data --confirm wrong.test --install-dir /srv/b --config-dir /srv/bcfg
t test -e /srv/b/marker-file; t running b1
# A failing final backup stops everything.
printf '#!/bin/bash\nexit 1\n' > /srv/b/backup.sh
no un --delete-data --confirm b.test --install-dir /srv/b --config-dir /srv/bcfg
t test -e /srv/b/marker-file; t running b1
# Paths that are not an install are refused.
no un --install-dir /srv/b/../etc --config-dir /srv/bcfg
mkdir -p /srv/other; touch /srv/other/precious
no un --install-dir /srv/other --config-dir /srv/bcfg
no un --install-dir /srv/b --config-dir /srv/other
t test -e /srv/other/precious
# Keep mode on the second install: only its own containers go; the first install keeps running with its data and host units.
t un --install-dir /srv/b --config-dir /srv/bcfg
no running b1; t running a1
no test -e /srv/b/marker-file; t test -e /srv/b/backups/old; t test -e /opt/automaton-server/marker-file
t docker volume inspect automaton-server_postgres-data
t test -e /etc/systemd/system/automaton-backup.service; t test -e /var/lib/automaton/apps/data
# Deleting the second install with a confirmation and a final backup leaves the first untouched.
mk /srv/b /srv/bcfg b.test   # the install comes back (a retry)
stack b2 /srv/b
t un --delete-data --confirm b.test --install-dir /srv/b --config-dir /srv/bcfg
no running b2; t running a1
no test -e /srv/b; no test -e /srv/bcfg
t test -e /opt/automaton-server/marker-file; t test -e /etc/automaton/secrets/backup-key
t docker volume inspect automaton-server_postgres-data
fb=$(ls -d /var/backups/automaton-final-* 2>/dev/null | head -n1)
t test -s "$fb/automaton-20260101T000000Z.tar.age"; t grep -q key-of-b.test "$fb/backup-key"
# Pointing it at the one install it can see removes that install, with its volumes and a final backup.
t un --delete-data --confirm a.test --install-dir /opt/automaton-server --config-dir /etc/automaton
no running a1; no docker volume inspect automaton-server_postgres-data
no test -e /opt/automaton-server; no test -e /etc/automaton; no test -e /var/lib/automaton
[[ $(ls -d /var/backups/automaton-final-* | wc -l) -eq 2 ]] || { echo 'FAIL: no second final backup'; fail=1; }
# Retry flow: --no-final-backup deletes without one; nothing left to remove is not an error.
mk /srv/c /srv/ccfg c.test
t un --delete-data --confirm c.test --no-final-backup --install-dir /srv/c --config-dir /srv/ccfg
no test -e /srv/c
[[ $fail -eq 0 ]] && echo ok; exit $fail
INNER
