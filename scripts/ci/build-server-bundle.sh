#!/usr/bin/env bash
# Assemble automaton-server/ (manifest, scripts, release.env) from built component image references
# and verify that each application image can be pulled anonymously.
# Expects: VERSION, GITHUB_SHA, GH_TOKEN, GITHUB_REPOSITORY (or GH_REPO), and *.image files in PWD.
set -euo pipefail

VERSION=${VERSION:?VERSION is required}
GITHUB_SHA=${GITHUB_SHA:?GITHUB_SHA is required}
REPO=${GITHUB_REPOSITORY:-${GH_REPO:-rzvdaniel/automaton}}
REPO_LOWER=${REPO,,}

[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][a-zA-Z0-9.-]+)?$ ]] || { echo "Invalid VERSION: $VERSION" >&2; exit 1; }

rm -rf automaton-server previous-release
mkdir automaton-server
cp docker/install/{compose.yaml,configure.py,acme_responder.py,check_dns.py,explain_certificate_failure.py,host_security.py,backup.sh,cleanup.sh,fail2ban.sh,watchdog.py,restore.sh,update-from-request.sh,restart-from-request.sh,os-update-from-request.sh,migrate-to-postgresql.sh,mysql_to_postgres.py,migration_app_check.py} automaton-server/
cp -r docker/install/fail2ban automaton-server/fail2ban
cp -r docker/production-host automaton-server/production-host
cp scripts/install-automaton.sh automaton-server/install.sh
cp scripts/update-automaton.sh automaton-server/update.sh
cp src/automaton-vue/docs/install.md automaton-server/README.md
printf 'server-v%s\n' "$VERSION" > automaton-server/version

previous=$(gh release list --repo "$REPO" --limit 100 --json tagName --jq "[.[] | select(.tagName | startswith(\"server-v\")) | select(.tagName != \"server-v$VERSION\")][0].tagName // empty")
if [[ -n "$previous" ]]; then
    mkdir previous-release
    gh release download "$previous" --repo "$REPO" --pattern automaton-server.tar.gz --pattern automaton-server.tar.gz.sha256 --dir previous-release
    (cd previous-release && sha256sum -c automaton-server.tar.gz.sha256)
    tar -xzf previous-release/automaton-server.tar.gz -C previous-release automaton-server/release.env
fi

{
    echo "API_IMAGE=$(cat server.image)"
    echo "WORKER_IMAGE=$(cat worker.image)"
    echo "STUDIO_IMAGE=$(cat studio.image)"
    echo "STORE_IMAGE=$(cat store.image)"
    echo "AGE_IMAGE=$(cat age.image)"
    resolve() {
        docker pull "$2" >&2
        printf '%s=%s\n' "$1" "$(docker image inspect --format '{{index .RepoDigests 0}}' "$2")"
    }
    if [[ -n "$previous" ]]; then
        # Infrastructure stays pinned across releases; changing it needs a reviewed migration.
        grep -E '^(RABBITMQ|TRAEFIK)_IMAGE=' previous-release/automaton-server/release.env
        if grep -q '^POSTGRES_IMAGE=' previous-release/automaton-server/release.env; then
            grep '^POSTGRES_IMAGE=' previous-release/automaton-server/release.env
        else
            # The first PostgreSQL release follows a MySQL release, whose manifest has no PostgreSQL image.
            resolve POSTGRES_IMAGE postgres:16
        fi
        # The ACME responder image stays pinned too; the first release that has it resolves it once.
        if grep -q '^RESPONDER_IMAGE=' previous-release/automaton-server/release.env; then
            grep '^RESPONDER_IMAGE=' previous-release/automaton-server/release.env
        else
            resolve RESPONDER_IMAGE python:3-alpine
        fi
    else
        resolve POSTGRES_IMAGE postgres:16
        resolve RABBITMQ_IMAGE rabbitmq:4-management
        resolve TRAEFIK_IMAGE traefik:v3
        resolve RESPONDER_IMAGE python:3-alpine
    fi
    # New Runners install the Runner image built from this commit. That workflow only runs when Runner code
    # changed, so with no run for this commit the unchanged :latest image is the right one.
    runner_run=$(gh run list --repo "$REPO" --workflow runner-image.yml --commit "$GITHUB_SHA" --limit 1 --json databaseId --jq '.[0].databaseId // empty')
    if [[ -n "$runner_run" ]]; then
        gh run watch "$runner_run" --repo "$REPO" --exit-status >&2 \
            || { echo "The Runner image workflow run $runner_run for $GITHUB_SHA did not succeed; rerun it, then rerun this check." >&2; exit 1; }
        resolve RUNNER_IMAGE "ghcr.io/${REPO_LOWER}-runner:sha-$GITHUB_SHA"
    else
        resolve RUNNER_IMAGE "ghcr.io/${REPO_LOWER}-runner:latest"
    fi
} > automaton-server/release.env

public_config=$(mktemp -d)
trap 'rm -rf "$public_config"' EXIT
while IFS= read -r reference; do
    docker --config "$public_config" pull "$reference"
# A new GHCR package starts private; make it public, then rerun, if this fails for a release image.
done < <(cat server.image worker.image studio.image store.image age.image)
