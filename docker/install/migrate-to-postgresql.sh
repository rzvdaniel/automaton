#!/usr/bin/env bash
# Move an existing Automaton server from MySQL to PostgreSQL.
#
#   migrate-to-postgresql.sh --version server-vX.Y.Z             report what would happen; changes nothing (the default)
#   migrate-to-postgresql.sh --version server-vX.Y.Z --run       migrate (a short downtime of API and Worker)
#   migrate-to-postgresql.sh --version server-vX.Y.Z --finish    complete a migration that stopped after the switch
#   migrate-to-postgresql.sh --rollback                          go back to the MySQL installation
#
# --version downloads and verifies the release bundle; --bundle-dir DIR uses an extracted, verified one instead.
# --postgres-override FILE uses a separate Compose override after the switch; the installed override remains MySQL's.
# Run it in tmux or screen. See docs/prod/migrate-to-postgresql.md.
#
# Until the new stack is started the old installation is only stopped and its configuration patched (and saved first); the MySQL
# volume is never modified. Any failure before the switch returns to the MySQL services. After it, --finish completes the
# migration or --rollback returns to MySQL (everything written since the switch is lost).
set -euo pipefail
umask 077

install_dir=/opt/automaton-server
config_dir=/etc/automaton
backup_root=''
version=''
bundle_dir=''
bundle_archive=''
bundle_checksum=''
mode=plan
confirm=''
discard_in_flight=0
rehearsal=0
skip_app_check=0
allow_override=0
postgres_override_arg=''
recovery_arg=''
python_image=python:3.12-slim
loader_packages=(pymysql==1.1.1 cryptography==43.0.3 'psycopg[binary]==3.2.3')
while (($#)); do
    case "$1" in
        --run) mode=run; shift ;;
        --plan) mode=plan; shift ;;
        --finish) mode=finish; shift ;;
        --rollback) mode=rollback; shift ;;
        --recovery) recovery_arg="${2:?Missing recovery directory}"; shift 2 ;;
        --confirm) confirm="${2:?Missing domain}"; shift 2 ;;
        --version) version="${2:?Missing release version}"; shift 2 ;;
        --bundle-dir) bundle_dir="${2:?Missing bundle directory}"; shift 2 ;;
        --bundle) bundle_archive="${2:?Missing release bundle}"; shift 2 ;;
        --checksum) bundle_checksum="${2:?Missing checksum}"; shift 2 ;;
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --config-dir) config_dir="${2:?Missing configuration directory}"; shift 2 ;;
        --backup-dir) backup_root="${2:?Missing backup directory}"; shift 2 ;;
        --python-image) python_image="${2:?Missing image}"; shift 2 ;;
        --discard-in-flight) discard_in_flight=1; shift ;;
        --skip-app-check) skip_app_check=1; shift ;;
        --allow-override) allow_override=1; shift ;;
        --postgres-override) postgres_override_arg="${2:?Missing PostgreSQL override file}"; shift 2 ;;
        --rehearsal) rehearsal=1; shift ;;
        --help) sed -n '2,15p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done
[[ $EUID == 0 || $rehearsal == 1 ]] || { echo 'Run as root (sudo).' >&2; exit 1; }
for dir in "$install_dir" "$config_dir"; do
    [[ "$dir" =~ ^/[a-zA-Z0-9_./-]+$ && "$dir" != / && ! -L "$dir" ]] || { echo 'Use absolute, non-symlink installation and configuration directories.' >&2; exit 1; }
done
backup_root=${backup_root:-$install_dir/backups}
for tool in docker python3 curl sha256sum flock; do
    command -v "$tool" >/dev/null || { echo "$tool is required but was not found." >&2; exit 1; }
done
docker compose version >/dev/null || { echo 'The Docker Compose plugin is required.' >&2; exit 1; }
docker info >/dev/null || { echo 'Docker is not reachable.' >&2; exit 1; }
exec 9>"${AUTOMATON_LOCK_FILE:-/run/lock/automaton-install.lock}"
flock -n 9 || { echo 'Another installation, update or migration is running.' >&2; exit 1; }

working=$(mktemp -d)
recovery=''
distribution=''
bundle_args=''
phase=checks
trap 'exit 130' INT TERM HUP    # a signal ends the script through the cleanup below
cleanup() {
    local status=$?
    trap - EXIT
    trap '' INT TERM HUP        # the return to MySQL must not be interrupted halfway
    if ((status != 0)) && [[ "$mode" == run || "$mode" == finish ]]; then
        case "$phase" in
            downtime)
                echo "The migration failed before the switch; returning to the MySQL installation." >&2
                if ! restore_mysql_stack; then
                    echo "The automatic return failed. Fix the reported problem, then run: sudo bash $install_dir/migrate-to-postgresql.sh --rollback --install-dir $install_dir --config-dir $config_dir --confirm $domain" >&2
                fi ;;
            checks)
                # Nothing was stopped or changed yet; leave no half-started migration behind.
                if [[ -n "$recovery" && -f "$recovery/STATE" && "$(state)" == started ]]; then set_state rolled-back; rm -f "$pointer"; fi ;;
            switched)
                echo "The migration stopped after the switch. The PostgreSQL stack is running. Fix the reported problem, then complete it with:" >&2
                echo "  sudo -E bash $install_dir/migrate-to-postgresql.sh --finish ${bundle_args:-<the same release arguments>} --install-dir $install_dir --config-dir $config_dir   (with the AUTOMATON_CHECK_* variables set)" >&2
                echo "or return to MySQL (loses what was written since the switch):" >&2
                echo "  sudo bash $install_dir/migrate-to-postgresql.sh --rollback --install-dir $install_dir --config-dir $config_dir --confirm $domain" >&2 ;;
        esac
    fi
    [[ -z "$distribution" ]] || docker rm -f "$distribution" >/dev/null 2>&1 || true
    rm -rf -- "$working"
    exit "$status"
}
trap cleanup EXIT

# ---- the installation ------------------------------------------------------------------------------------------------

for file in "$install_dir/release.env" "$install_dir/compose.yaml" "$install_dir/.env" "$config_dir/installation.json" "$config_dir/appsettings.Production.json"; do
    [[ -f "$file" && ! -L "$file" ]] || { echo "Existing installation is incomplete: $file." >&2; exit 1; }
done
grep -Fxq "CONFIG_DIR=$config_dir" "$install_dir/.env" || { echo 'The installation uses another configuration directory.' >&2; exit 1; }
domain=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["domain"])' "$config_dir/installation.json")
override="$install_dir/compose.override.yaml"
extra=()
if [[ -e "$override" || -L "$override" ]]; then
    [[ -f "$override" && ! -L "$override" ]] || { echo 'The installation Compose override must be a regular, non-symlink file.' >&2; exit 1; }
    if ((! allow_override)) && [[ -z "$postgres_override_arg" && "$mode" != rollback && "$mode" != finish ]]; then
        echo "This installation has a Compose override ($override). Supply --postgres-override FILE for the PostgreSQL stack, or review this override and repeat with --allow-override." >&2
        exit 1
    fi
    extra=(-f "$override")
fi
old_compose=(docker compose --project-directory "$install_dir" --env-file "$install_dir/release.env" --env-file "$install_dir/.env" -f "$install_dir/compose.yaml" "${extra[@]}")
pointer="$install_dir/.postgresql-migration"
# A resumed finish can find the installed files halfway through replacement. Recovery modes
# resolve the old project and MySQL volume from the verified pre-switch manifest instead.
if [[ "$mode" == finish || "$mode" == rollback ]]; then
    saved_recovery=${recovery_arg:-$(cat "$pointer" 2>/dev/null || true)}
    if [[ -f "$saved_recovery/release.env" && -f "$saved_recovery/compose.yaml" && -f "$saved_recovery/.env" ]]; then
        recovery_extra=()
        [[ ! -f "$saved_recovery/compose.override.yaml" ]] || recovery_extra=(-f "$saved_recovery/compose.override.yaml")
        old_compose=(docker compose --project-directory "$install_dir" --env-file "$saved_recovery/release.env" --env-file "$saved_recovery/.env" -f "$saved_recovery/compose.yaml" "${recovery_extra[@]}")
    fi
fi

# ---- rollback ----------------------------------------------------------------------------------------------------------

state() { cat "$recovery/STATE" 2>/dev/null || echo none; }
set_state() { printf '%s\n' "$1" > "$recovery/STATE"; }

project_name() { "${old_compose[@]}" config --format json | python3 -c 'import json,sys; print(json.load(sys.stdin)["name"])'; }

# Stop what the migration started, put the old files and configuration back and start the MySQL services.
# Every step is attempted; the function fails if any of them did, and keeps the recovery pointer then.
restore_mysql_stack() {
    # Called inside an `if`, where set -e does not apply: every step is attempted, and the function fails if any step did.
    local project failed=0 old_dir full=0
    try() { "$@" || { echo "Step failed: $*" >&2; failed=1; }; }
    project=$(project_name) || return 1
    if [[ -n "$recovery" && -f "$recovery/compose.yaml" && -f "$recovery/release.env" && -f "$recovery/.env" && -f "$recovery/config.tar.gz" && -f "$recovery/SHA256SUMS" ]]; then
        full=1
        old_dir=$recovery
    else
        old_dir=$install_dir   # no backup was made yet, so nothing was changed; the live files are the old ones
    fi
    local restore_extra=()
    if ((full)); then
        [[ ! -f "$old_dir/compose.override.yaml" ]] || restore_extra=(-f "$old_dir/compose.override.yaml")
    else
        restore_extra=("${extra[@]}")
    fi
    local restore_compose=(docker compose --project-name "$project" --project-directory "$install_dir" --env-file "$old_dir/release.env" --env-file "$old_dir/.env" -f "$old_dir/compose.yaml" "${restore_extra[@]}")
    echo 'Stopping the PostgreSQL services.'
    docker rm -f "$project-migration-schema" >/dev/null 2>&1 || true
    "${restore_compose[@]}" stop api worker >/dev/null 2>&1 || true
    local postgres
    postgres=$(docker ps -aq --filter "label=com.docker.compose.project=$project" --filter 'label=com.docker.compose.service=postgres') || failed=1
    if ((full)) && [[ "$(state)" == complete || "$(state)" == switched ]]; then
        # What was written to PostgreSQL after the switch is about to be discarded; keep a dump of it.
        local running
        running=$(docker ps -q --filter "label=com.docker.compose.project=$project" --filter 'label=com.docker.compose.service=postgres')
        if [[ -n "$running" ]] && docker exec "$running" pg_dump -U automaton -d automaton --format=custom > "$recovery/postgresql-before-rollback.dump" 2>/dev/null; then
            echo "Saved the PostgreSQL data to $recovery/postgresql-before-rollback.dump before discarding it."
        else
            echo 'Warning: could not save the PostgreSQL data before discarding it.' >&2
        fi
    fi
    if [[ -n "$postgres" ]]; then try docker rm -f $postgres; fi
    if ((full)); then
        if grep -Fxq 'postgres_volume_created=1' "$recovery/NOTES" 2>/dev/null; then
            local postgres_volume_name
            postgres_volume_name=$(sed -n 's/^postgres_volume_name=//p' "$recovery/NOTES" | head -n 1)
            try docker volume rm -f "${postgres_volume_name:-${project}_postgres-data}"
        fi
        echo 'Restoring the previous configuration.'
        (cd "$recovery" && sha256sum -c --quiet SHA256SUMS) || { echo 'The recovery data failed its checksum verification.' >&2; failed=1; }
        try tar -C "$(dirname "$config_dir")" --no-overwrite-dir -xzf "$old_dir/config.tar.gz"
        if [[ -f "$old_dir/config-added.txt" ]]; then
            # Only the files the migration itself added (generated routes, the PostgreSQL password, set-aside credentials).
            while IFS= read -r added; do
                if [[ -n "$added" ]]; then rm -f -- "${config_dir:?}/$added"; fi
            done < "$old_dir/config-added.txt"
        fi
        for file in release.env compose.yaml .env version update.sh install.sh backup.sh restore.sh; do
            if [[ -f "$old_dir/$file" ]]; then try install -m "$(stat -c %a "$old_dir/$file")" "$old_dir/$file" "$install_dir/$file"; fi
        done
        if [[ -f "$old_dir/compose.override.yaml" ]]; then
            try install -m "$(stat -c %a "$old_dir/compose.override.yaml")" "$old_dir/compose.override.yaml" "$override"
        elif [[ -f "$override" ]]; then
            try rm -f -- "$override"
        fi
    fi
    echo 'Starting the MySQL services.'
    local source_volume=${mysql_volume_name:-}
    if [[ -z "$source_volume" && -f "$recovery/NOTES" ]]; then
        source_volume=$(sed -n 's/^mysql_volume_name=//p' "$recovery/NOTES" | head -n 1)
    fi
    source_volume=${source_volume:-${project}_mysql-data} # recovery data from older migrations used this name
    if ((full)) && [[ -z "$(docker volume ls -q --filter "name=^${source_volume}$")" ]]; then
        echo 'The MySQL volume is gone; recreating it from the recovery dump.'
        try "${restore_compose[@]}" up -d --wait --wait-timeout 300 mysql
        zcat "$old_dir/mysql.sql.gz" | "${restore_compose[@]}" exec -T mysql sh -c 'MYSQL_PWD=$(cat /run/secrets/mysql_root_password) mysql -uroot --default-character-set=utf8mb4 automaton' || { echo 'Step failed: loading the MySQL dump' >&2; failed=1; }
    fi
    try "${restore_compose[@]}" up -d --wait --wait-timeout 600 mysql rabbitmq
    local old_services_now others service
    old_services_now=$("${restore_compose[@]}" config --services) || failed=1
    # Services that only the new release has must not keep running against the removed database.
    for service in $(docker ps -a --filter "label=com.docker.compose.project=$project" --format '{{.Label "com.docker.compose.service"}}' | sort -u); do
        if [[ " $(echo $old_services_now) " != *" $service "* ]]; then
            docker rm -f $(docker ps -aq --filter "label=com.docker.compose.project=$project" --filter "label=com.docker.compose.service=$service") >/dev/null 2>&1 || true
        fi
    done
    others=$(echo "$old_services_now" | grep -Ev '^(mysql|rabbitmq|traefik)$' || true)
    if [[ -n "$others" ]]; then try "${restore_compose[@]}" up -d --wait --wait-timeout 600 --force-recreate $others; fi
    docker volume rm -f automaton-migration-pip >/dev/null 2>&1 || true
    if ((failed)); then
        echo 'The return to MySQL did not complete. Recovery data is kept; fix the problem above and run --rollback again (it is safe to repeat).' >&2
        return 1
    fi
    if [[ -n "$recovery" ]]; then set_state rolled-back || true; fi
    rm -f "$pointer"
    echo "Returned to MySQL. Recovery data stays in ${recovery:-the installation}."
}

# Which recovery data a rollback or --finish works on, and whether there is anything to do.
select_recovery() {
    recovery=${recovery_arg:-$(cat "$pointer" 2>/dev/null || true)}
    [[ -n "$recovery" && -d "$recovery" ]] || { echo "No migration recovery data found (no migration is in progress). Use --recovery DIR for an older one." >&2; exit 1; }
}

if [[ "$mode" == rollback ]]; then
    if [[ -z "$recovery_arg" && ! -f "$pointer" ]]; then
        echo 'No migration is in progress, so there is nothing to roll back. (Use --recovery DIR to roll back an older one.)'
        exit 0
    fi
    select_recovery
    if [[ "$(state)" == rolled-back ]]; then echo "That migration was already rolled back ($recovery)."; rm -f "$pointer"; exit 0; fi
    echo "Recovery data: $recovery (state: $(state))"
    if [[ "$(state)" == complete || "$(state)" == switched ]]; then
        echo 'This returns to the MySQL data as it was when the migration started. Everything written since is lost.'
    fi
    if [[ -z "$confirm" ]]; then
        [[ -t 0 ]] || { echo "Confirm non-interactively with --confirm $domain" >&2; exit 2; }
        read -r -p "Type the server domain ($domain) to continue: " confirm
    fi
    [[ "$confirm" == "$domain" ]] || { echo 'Confirmation did not match; nothing was changed.' >&2; exit 1; }
    if ! restore_mysql_stack; then exit 1; fi
    exit 0
fi

# ---- the release bundle ------------------------------------------------------------------------------------------------

if [[ "$mode" != finish ]]; then
    if grep -q '^POSTGRES_IMAGE=' "$install_dir/release.env" && [[ ! -f "$config_dir/secrets/mysql-password" ]]; then
        echo 'This installation already runs on PostgreSQL; nothing to migrate.'
        exit 0
    fi
    grep -q '^MYSQL_IMAGE=' "$install_dir/release.env" && [[ -f "$config_dir/secrets/mysql-password" ]] || { echo 'This does not look like a MySQL installation; nothing was changed.' >&2; exit 1; }
fi

if [[ -n "$bundle_dir" ]]; then
    bundle=$(realpath "$bundle_dir")
else
    [[ -n "$version" ]] || { echo 'Give the release with --version server-vX.Y.Z, --bundle-dir, or --bundle with --checksum.' >&2; exit 2; }
    [[ "$version" =~ ^server-v[0-9]+\.[0-9]+\.[0-9]+([.-][a-zA-Z0-9.-]+)?$ ]] || { echo 'Specify --version server-vX.Y.Z.' >&2; exit 2; }
    if [[ -z "$bundle_archive" ]]; then
        image="ghcr.io/rzvdaniel/automaton-server-distribution:$version"
        docker pull "$image"
        distribution=$(docker create "$image")
        docker cp "$distribution:/automaton-server.tar.gz" "$working/automaton-server.tar.gz"
        docker cp "$distribution:/automaton-server.tar.gz.sha256" "$working/automaton-server.tar.gz.sha256"
        docker rm "$distribution" >/dev/null
        distribution=''
    else
        [[ -n "$bundle_checksum" && -f "$bundle_archive" && -f "$bundle_checksum" ]] || { echo 'An offline bundle needs its --checksum file.' >&2; exit 1; }
        cp -- "$bundle_archive" "$working/automaton-server.tar.gz"
        cp -- "$bundle_checksum" "$working/automaton-server.tar.gz.sha256"
    fi
    (cd "$working" && sha256sum -c automaton-server.tar.gz.sha256)
    python3 - "$working/automaton-server.tar.gz" "$working" <<'PY'
import pathlib, sys, tarfile
with tarfile.open(sys.argv[1], 'r:gz') as archive:
    for member in archive.getmembers():
        parts = pathlib.PurePosixPath(member.name).parts
        if not parts or parts[0] != 'automaton-server' or '..' in parts or not (member.isdir() or member.isfile()):
            raise SystemExit('Invalid release bundle entry.')
    archive.extractall(sys.argv[2], filter='data')
PY
    bundle="$working/automaton-server"
fi
if [[ -n "$bundle_dir" ]]; then bundle_args="--bundle-dir $bundle"
elif [[ -n "$bundle_archive" ]]; then bundle_args="--version $version --bundle $bundle_archive --checksum $bundle_checksum"
else bundle_args="--version $version"; fi
for file in release.env compose.yaml version update.sh install.sh configure.py migrate-to-postgresql.sh mysql_to_postgres.py migration_app_check.py; do
    [[ -f "$bundle/$file" ]] || { echo "The release bundle is incomplete: $file is missing. Use a release that includes the migration." >&2; exit 1; }
done
cmp -s "$bundle/migrate-to-postgresql.sh" "${BASH_SOURCE[0]}" || echo 'Note: the executing migration script differs from the release bundle; it will remain installed for --finish and --rollback.' >&2
grep -q '^POSTGRES_IMAGE=' "$bundle/release.env" && ! grep -q '^MYSQL_IMAGE=' "$bundle/release.env" || { echo 'This release does not run on PostgreSQL.' >&2; exit 1; }
# --finish uses the checked copy saved before downtime, even when the caller omits the original path.
if [[ "$mode" == finish && -z "$postgres_override_arg" ]]; then
    saved_recovery=${recovery_arg:-$(cat "$pointer" 2>/dev/null || true)}
    if grep -Fxq 'postgres_override_saved=1' "$saved_recovery/NOTES" 2>/dev/null && [[ ! -f "$saved_recovery/postgres-override.yaml" ]]; then
        echo 'The saved PostgreSQL override is missing from recovery data.' >&2; exit 1
    fi
    [[ -z "$saved_recovery" || ! -f "$saved_recovery/postgres-override.yaml" ]] || postgres_override_arg="$saved_recovery/postgres-override.yaml"
fi
postgres_extra=("${extra[@]}")
if [[ -n "$postgres_override_arg" ]]; then
    [[ -f "$postgres_override_arg" && ! -L "$postgres_override_arg" ]] || { echo 'The PostgreSQL override must be a regular, non-symlink file.' >&2; exit 1; }
    if [[ "$mode" == finish ]]; then
        saved_recovery=${recovery_arg:-$(cat "$pointer" 2>/dev/null || true)}
        [[ -f "$saved_recovery/postgres-override.yaml" ]] || { echo 'The saved PostgreSQL override is missing from recovery data.' >&2; exit 1; }
        (cd "$saved_recovery" && sha256sum -c --quiet SHA256SUMS) || { echo 'The recovery data failed its checksum verification.' >&2; exit 1; }
        cmp -s "$postgres_override_arg" "$saved_recovery/postgres-override.yaml" || { echo 'The PostgreSQL override differs from the saved migration copy.' >&2; exit 1; }
        postgres_override_arg="$saved_recovery/postgres-override.yaml"
    else
        cp -p -- "$postgres_override_arg" "$working/postgres-override.yaml"
        postgres_override_arg="$working/postgres-override.yaml"
    fi
    postgres_extra=(-f "$postgres_override_arg")
fi
# The new Compose file mounts nightly backups read-only. Older installs only have CONFIG_DIR in .env;
# add the default backup path to a temporary candidate env without changing the live installation in plan mode.
candidate_env="$working/postgres.env"
cp -- "$install_dir/.env" "$candidate_env"
if ! grep -q '^BACKUP_DIR=' "$candidate_env"; then printf 'BACKUP_DIR=%s/backups\n' "$install_dir" >> "$candidate_env"; fi
new_compose=(docker compose --project-directory "$install_dir" --env-file "$bundle/release.env" --env-file "$candidate_env" -f "$bundle/compose.yaml" "${postgres_extra[@]}")
# Validate the resolved stack without showing environment values or secrets.
"${new_compose[@]}" config --format json | python3 -c '
import json, re, sys
config = json.load(sys.stdin)
if any(name.lower() == "mysql" for name in config.get("services", {})):
    raise SystemExit("The PostgreSQL override adds a mysql service.")
for service in config.get("services", {}).values():
    environment = service.get("environment", {})
    if isinstance(environment, list):
        environment = dict(item.split("=", 1) for item in environment if "=" in item)
    for key, value in environment.items():
        if (re.search(r"mysqlconnection", key, re.I)
                or (re.search(r"connectionstring|database_url", key, re.I) and re.search(r"mysql", str(value), re.I))
                or re.search(r"mysql://|(?:server|host)\s*=\s*mysql(?:[.;:/-]|$)", str(value), re.I)):
            raise SystemExit("The PostgreSQL override contains a MySQL connection string.")
'
old_services=$("${old_compose[@]}" config --services | tr '\n' ' ')
new_services=$("${new_compose[@]}" config --services | tr '\n' ' ')
[[ " $new_services " == *' postgres '* ]] || { echo 'The Compose files do not match a MySQL to PostgreSQL migration.' >&2; exit 1; }
# After the release files were replaced (--finish) the installation's own Compose file no longer has a mysql service.
[[ "$mode" == finish || " $old_services " == *' mysql '* ]] || { echo 'The Compose files do not match a MySQL to PostgreSQL migration.' >&2; exit 1; }
application_services=()
for service in $new_services; do
    [[ " postgres rabbitmq traefik mysql " == *" $service "* ]] || application_services+=("$service")
done
project=$(project_name)
# Compose resolves an external MySQL volume to its actual name; it need not be <project>_mysql-data.
mysql_volume_name=$("${old_compose[@]}" config --format json | python3 -c '
import json, sys
config = json.load(sys.stdin)
mounts = config.get("services", {}).get("mysql", {}).get("volumes", [])
source = next((mount.get("source") for mount in mounts if mount.get("type") == "volume" and mount.get("target") == "/var/lib/mysql"), None)
print(config.get("volumes", {}).get(source, {}).get("name", source or ""))
')
if [[ "$mode" == finish && -z "$mysql_volume_name" ]]; then
    saved_recovery=${recovery_arg:-$(cat "$pointer" 2>/dev/null || true)}
    mysql_volume_name=$(sed -n 's/^mysql_volume_name=//p' "$saved_recovery/NOTES" | head -n 1)
fi
[[ -n "$mysql_volume_name" ]] || { echo 'Could not resolve the MySQL data volume.' >&2; exit 1; }
mysql_container=$(docker ps -aq --filter "label=com.docker.compose.project=$project" --filter 'label=com.docker.compose.service=mysql' | head -n 1)
[[ -n "$mysql_container" ]] || { echo 'The MySQL container was not found; repair the installation first.' >&2; exit 1; }
if [[ "$mode" != finish ]]; then
    [[ "$(docker inspect -f '{{.State.Running}}' "$mysql_container")" == true ]] || { echo 'MySQL is not running; repair the installation first.' >&2; exit 1; }
    for service in api worker; do
        container=$("${old_compose[@]}" ps -q "$service")
        [[ -n "$container" && "$(docker inspect -f '{{.State.Running}}' "$container")" == true ]] || { echo "$service is not running; repair the installation first." >&2; exit 1; }
    done
fi
network=$(docker inspect -f '{{range $name, $_ := .NetworkSettings.Networks}}{{$name}} {{end}}' "$mysql_container" | awk '{print $1}')

# ---- helpers for the data copy and checks ----------------------------------------------------------------------------

read_secret() { cat "$config_dir/secrets/$1"; }

# Runs a subcommand of mysql_to_postgres.py in a Python container on the installation's network.
run_loader() {
    local env_file="$working/loader.env"
    (umask 077; {
        echo 'MYSQL_HOST=mysql'; echo 'MYSQL_USER=automaton'; echo "MYSQL_PASSWORD=$(read_secret mysql-password)"; echo 'MYSQL_DATABASE=automaton'
        if [[ -f "$config_dir/secrets/postgres-password" ]]; then
            echo 'PG_HOST=postgres'; echo 'PG_USER=automaton'; echo "PG_PASSWORD=$(read_secret postgres-password)"; echo 'PG_DATABASE=automaton'
        fi
    } > "$env_file")
    docker run --rm --network "$network" --memory 512m --env-file "$env_file" -v automaton-migration-pip:/root/.cache/pip -v "$bundle/mysql_to_postgres.py:/migrate/mysql_to_postgres.py:ro" "$python_image" \
        sh -c 'pip install -q --disable-pip-version-check --root-user-action=ignore "$@" >/dev/null && python /migrate/mysql_to_postgres.py "$0"' "$1" "${loader_packages[@]}"
}

# Saves (before) or compares (after) what the administrator and a regular user see through the API.
run_app_check() {
    local action=$1 file=$2
    if ((skip_app_check)); then return 0; fi
    local env_file="$working/check.env"
    (umask 077; {
        echo "AUTOMATON_CHECK_EMAIL=$AUTOMATON_CHECK_EMAIL"; echo "AUTOMATON_CHECK_PASSWORD=$AUTOMATON_CHECK_PASSWORD"
        [[ -z "${AUTOMATON_CHECK_USER_EMAIL:-}" ]] || { echo "AUTOMATON_CHECK_USER_EMAIL=$AUTOMATON_CHECK_USER_EMAIL"; echo "AUTOMATON_CHECK_USER_PASSWORD=${AUTOMATON_CHECK_USER_PASSWORD:-}"; }
    } > "$env_file")
    [[ -e "$file" ]] || (umask 000; : > "$file")
    docker run --rm --network "$network" --memory 128m --env-file "$env_file" -v "$bundle/migration_app_check.py:/migrate/migration_app_check.py:ro" -v "$file:/out/snapshot.json" \
        "$python_image" python /migrate/migration_app_check.py --api-url http://api:8080 "--$action" /out/snapshot.json
}

require_check_credentials() {
    if ((! skip_app_check)) && [[ -z "${AUTOMATON_CHECK_EMAIL:-}" || -z "${AUTOMATON_CHECK_PASSWORD:-}" ]]; then
        echo 'Set AUTOMATON_CHECK_EMAIL and AUTOMATON_CHECK_PASSWORD to an administrator account (and optionally AUTOMATON_CHECK_USER_EMAIL / _PASSWORD) so the result can be compared with what people see now, or pass --skip-app-check.' >&2
        exit 2
    fi
}

# The last part of a migration: what people see must match, then the release files replace the MySQL ones.
finish_switch() {
    echo 'Comparing what people see with the snapshot from before.'
    run_app_check compare "$recovery/before.json"
    if ((! rehearsal)); then
        curl --fail --silent --show-error --retry 12 --retry-delay 5 --retry-all-errors "https://studio.$domain/api/install/status" | python3 -c 'import json,sys; assert json.load(sys.stdin)["installationComplete"]'
        curl --fail --silent --show-error "https://studio.$domain/" >/dev/null
    fi
    local started
    started=$(sed -n 's/^downtime_start=//p' "$recovery/NOTES" | head -n 1)
    downtime=$(( $(date +%s) - ${started:-$(date +%s)} ))
    echo 'Installing the release files.'
    install -m 0600 "$bundle/release.env" "$install_dir/release.env"
    install -m 0644 "$bundle/compose.yaml" "$install_dir/compose.yaml"
    if ! grep -q '^BACKUP_DIR=' "$install_dir/.env"; then printf 'BACKUP_DIR=%s/backups\n' "$install_dir" >> "$install_dir/.env"; fi
    [[ ! -f "$recovery/postgres-override.yaml" ]] || install -m 0644 "$recovery/postgres-override.yaml" "$override"
    install -m 0644 "$bundle/version" "$install_dir/version"
    for script in update.sh install.sh backup.sh restore.sh; do
        [[ ! -f "$bundle/$script" ]] || install -m 0700 "$bundle/$script" "$install_dir/$script"
    done
    if ! cmp -s "${BASH_SOURCE[0]}" "$install_dir/migrate-to-postgresql.sh"; then
        install -m 0700 "${BASH_SOURCE[0]}" "$install_dir/migrate-to-postgresql.sh"
    fi
    echo 'Stopping MySQL (its data stays for rollback) and setting its credentials aside.'
    docker stop "$mysql_container" >/dev/null
    install -d -m 0700 "$config_dir/pre-postgresql"
    for secret in mysql-password mysql-root-password; do
        if [[ -f "$config_dir/secrets/$secret" ]]; then mv "$config_dir/secrets/$secret" "$config_dir/pre-postgresql/$secret"; fi
        grep -Fxq "pre-postgresql/$secret" "$recovery/config-added.txt" 2>/dev/null || echo "pre-postgresql/$secret" >> "$recovery/config-added.txt"
    done
    docker volume rm -f automaton-migration-pip >/dev/null 2>&1 || true
    set_state complete
    phase=complete
    if ((! rehearsal)) && [[ -f "$install_dir/backup.sh" ]]; then
        bash "$install_dir/backup.sh" --install-dir "$install_dir" --install-timer || echo "Warning: nightly backups could not be enabled. Run: sudo bash $install_dir/backup.sh --install-timer" >&2
        bash "$install_dir/backup.sh" --install-dir "$install_dir" || echo 'Warning: the first PostgreSQL backup failed.' >&2
    fi
    cat <<EOF

Migrated to PostgreSQL (API and Worker were down for about ${downtime:-?}s, counting from the stop). Recovery data (keep until you are sure): $recovery
  - return to MySQL:  sudo bash $install_dir/migrate-to-postgresql.sh --rollback --install-dir $install_dir --config-dir $config_dir --confirm $domain
    (this loses everything written since the switch)
  - when satisfied, remove the old database: docker rm $mysql_container; docker volume rm $mysql_volume_name
EOF
}

if [[ "$mode" == finish ]]; then
    select_recovery
    [[ "$(state)" == switched ]] || { echo "Nothing to finish: the migration state is '$(state)', not 'switched'." >&2; exit 1; }
    recorded=$(sed -n 's/^bundle_version=//p' "$recovery/NOTES" | head -n 1)
    [[ -z "$recorded" || "$recorded" == "$(cat "$bundle/version")" ]] || { echo "This migration used release $recorded, but the bundle is $(cat "$bundle/version"). Use the same release." >&2; exit 1; }
    phase=switched
    require_check_credentials
    finish_switch
    exit 0
fi

# ---- the plan ----------------------------------------------------------------------------------------------------------

echo "Installation: $install_dir on $domain (Compose project $project)"
echo "Release:      $(cat "$bundle/version")"
echo "Services:     MySQL -> PostgreSQL; application services to restart: ${application_services[*]}"
echo 'Source data:'
rc=0
run_loader inspect || rc=$?
if ((rc == 3)); then
    echo 'Work is still in flight (see above); a migration needs --discard-in-flight, or wait until it is done.'
    if [[ "$mode" == run ]] && ((! discard_in_flight)); then echo 'Nothing was changed.' >&2; exit 1; fi
elif ((rc != 0)); then
    echo 'The source could not be inspected (see above).' >&2
    exit 1
fi
if [[ "$mode" == plan ]]; then
    cat <<EOF

Nothing was changed. A real run (--run):
  1. the checks, and an API snapshot of what the administrator and a user see now;
  2. API and Worker stop (the downtime starts), and the pending work is checked;
  3. a verified recovery backup in $backup_root/mysql-to-postgresql-<time>: configuration, keys, app data, a MySQL dump, the old release files;
  4. the PostgreSQL password and connection string are set, PostgreSQL starts, and the new API creates the schema in a container that receives no traffic;
  5. all tables are copied and verified (row counts, content hashes, foreign keys, sequences);
  6. the new stack starts (the switch), and the API must answer exactly as in step 1;
  7. the release files replace the MySQL ones, MySQL is stopped (its volume is kept for rollback), nightly backups continue.
A failure before step 6 starts the MySQL services again. From step 6 on, --finish completes the migration or --rollback returns to MySQL.
Run it in tmux or screen; if the connection drops during the downtime, run --rollback or --finish as the last message says.
EOF
    exit 0
fi

# ---- the migration -----------------------------------------------------------------------------------------------------

if [[ -f "$pointer" ]]; then
    existing=$(cat "$pointer")
    case "$(cat "$existing/STATE" 2>/dev/null || echo none)" in
        rolled-back|none) rm -f "$pointer" ;;
        switched) echo "An earlier migration stopped after the switch (recovery data $existing). Run --finish, or --rollback; running again would overwrite the PostgreSQL data with the old MySQL data." >&2; exit 1 ;;
        complete) echo "An earlier migration completed (recovery data $existing); this installation runs on PostgreSQL." >&2; exit 1 ;;
        *) echo "An earlier migration was interrupted (recovery data $existing, state $(cat "$existing/STATE")). Run --rollback first." >&2; exit 1 ;;
    esac
fi
postgres_volume_name=$("${new_compose[@]}" config --format json | python3 -c '
import json, sys
config = json.load(sys.stdin)
mounts = config.get("services", {}).get("postgres", {}).get("volumes", [])
source = next((mount.get("source") for mount in mounts if mount.get("type") == "volume" and mount.get("target") == "/var/lib/postgresql/data"), None)
print(config.get("volumes", {}).get(source, {}).get("name", source or ""))
')
[[ -n "$postgres_volume_name" ]] || { echo 'Could not resolve the PostgreSQL data volume.' >&2; exit 1; }
if grep -q 'PostgreSqlConnection' "$config_dir/appsettings.Production.json" || [[ -n "$(docker volume ls -q --filter "name=^${postgres_volume_name}$")" ]]; then
    echo "A PostgreSQL volume or connection string already exists in this installation. Check $postgres_volume_name or finish the earlier migration; nothing was changed." >&2
    exit 1
fi
require_check_credentials
if ((! rehearsal)); then
    docker_root=$(docker info --format '{{.DockerRootDir}}')
    existing_backup_path=$backup_root
    while [[ ! -d "$existing_backup_path" ]]; do existing_backup_path=$(dirname -- "$existing_backup_path"); done
    for path in "$install_dir" "$docker_root" "$existing_backup_path"; do
        available_kb=$(df -Pk "$path" | awk 'NR==2 {print $4}')
        (( available_kb >= 10485760 )) || { echo "At least 10 GiB free disk space is required on $path before migrating." >&2; exit 1; }
    done
fi
if [[ -z "$confirm" ]]; then
    [[ -t 0 ]] || { echo "Confirm non-interactively with --confirm $domain" >&2; exit 2; }
    read -r -p "Type the server domain ($domain) to start the migration: " confirm
fi
[[ "$confirm" == "$domain" ]] || { echo 'Confirmation did not match; nothing was changed.' >&2; exit 1; }
[[ -n "$(docker volume ls -q --filter "name=^${mysql_volume_name}$")" ]] || { echo "The MySQL data volume ($mysql_volume_name) was not found." >&2; exit 1; }

install -d -m 0700 "$backup_root"
recovery=$(mktemp -d "$backup_root/mysql-to-postgresql-$(date -u +%Y%m%dT%H%M%SZ)-XXXXXX")
echo "Recovery data: $recovery"
{ echo 'postgres_volume_created=1'; echo "postgres_volume_name=$postgres_volume_name"; echo "mysql_volume_name=$mysql_volume_name"; [[ -z "$postgres_override_arg" ]] || echo 'postgres_override_saved=1'; echo "bundle_version=$(cat "$bundle/version")"; } > "$recovery/NOTES"
set_state started
# From here on a rollback is possible whatever happens, so the pointer and the script exist before anything is stopped.
printf '%s\n' "$recovery" > "$pointer"
if ! cmp -s "${BASH_SOURCE[0]}" "$install_dir/migrate-to-postgresql.sh"; then
    install -m 0700 "${BASH_SOURCE[0]}" "$install_dir/migrate-to-postgresql.sh"
fi
if ((! rehearsal)); then
    echo 'Pulling the release images and the copy tool now, so the downtime does not wait for the network.'
    "${new_compose[@]}" pull postgres "${application_services[@]}"
    docker pull "$python_image" >/dev/null
fi
run_loader inspect >/dev/null || [[ $? -eq 3 ]]   # also fills the pip cache used during the downtime
(cd "$config_dir" && find . -type f -printf '%P\n' | LC_ALL=C sort) > "$recovery/config-files.txt"

echo 'Taking the API snapshot (what people see before the migration).'
: > "$recovery/before.json"
run_app_check save "$recovery/before.json"

phase=downtime
echo 'Stopping API and Worker (downtime starts).'
echo "downtime_start=$(date +%s)" >> "$recovery/NOTES"
"${old_compose[@]}" stop api worker
echo 'Checking for work that cannot be carried over.'
rc=0
run_loader inspect >/dev/null || rc=$?
if ((rc == 3)); then
    if ((discard_in_flight)); then echo 'Queued messages and background jobs are discarded (--discard-in-flight).'
    else echo 'Work was still in flight when the services stopped. Nothing was changed; rerun with --discard-in-flight to accept losing it.' >&2; false; fi
elif ((rc != 0)); then
    echo 'The source could not be inspected; nothing was changed.' >&2; false
fi

echo 'Creating the verified recovery backup.'
cp -p "$install_dir/release.env" "$install_dir/compose.yaml" "$install_dir/.env" "$recovery/"
for file in version update.sh install.sh backup.sh restore.sh; do [[ ! -f "$install_dir/$file" ]] || cp -p "$install_dir/$file" "$recovery/$file"; done
[[ ! -f "$override" ]] || cp -p "$override" "$recovery/compose.override.yaml"
[[ -z "$postgres_override_arg" ]] || cp -p "$postgres_override_arg" "$recovery/postgres-override.yaml"
if [[ -n "$postgres_override_arg" ]]; then
    postgres_override_arg="$recovery/postgres-override.yaml"
    new_compose[-1]="$postgres_override_arg"
fi
tar -C "$(dirname "$config_dir")" -czf "$recovery/config.tar.gz" "$(basename "$config_dir")"
docker cp "$("${old_compose[@]}" ps -aq api):/app/data-protection-keys/." - | gzip > "$recovery/api-keys.tar.gz"
docker cp "$("${old_compose[@]}" ps -aq worker):/app/data-protection-keys/." - | gzip > "$recovery/worker-keys.tar.gz"
docker cp "$("${old_compose[@]}" ps -aq api):/app/AppData/." - | gzip > "$recovery/api-data.tar.gz"
"${old_compose[@]}" exec -T mysql sh -c 'MYSQL_PWD=$(cat /run/secrets/mysql_root_password) mysqldump -uroot --single-transaction --no-tablespaces --routines=false --triggers=false --set-gtid-purged=OFF --default-character-set=utf8mb4 automaton' < /dev/null | gzip > "$recovery/mysql.sql.gz"
for archive in config api-keys worker-keys api-data; do tar -tzf "$recovery/$archive.tar.gz" >/dev/null; done
gzip -t "$recovery/mysql.sql.gz"
expected_tables=$("${old_compose[@]}" exec -T mysql sh -c 'MYSQL_PWD=$(cat /run/secrets/mysql_root_password) mysql -uroot -N -e "select count(*) from information_schema.tables where table_schema=\"automaton\" and table_type=\"BASE TABLE\""' < /dev/null 2>/dev/null)
[[ "$expected_tables" =~ ^[0-9]+$ && "$expected_tables" -gt 0 ]] || { echo 'Could not count the MySQL tables.' >&2; false; }
(( $(zcat "$recovery/mysql.sql.gz" | grep -c '^CREATE TABLE') == expected_tables )) || { echo 'The MySQL dump does not contain every table.' >&2; false; }
(cd "$recovery" && sha256sum release.env compose.yaml .env config.tar.gz config-files.txt api-keys.tar.gz worker-keys.tar.gz api-data.tar.gz mysql.sql.gz before.json ${extra:+compose.override.yaml} ${postgres_override_arg:+postgres-override.yaml} > SHA256SUMS)
(cd "$recovery" && sha256sum -c --quiet SHA256SUMS)
set_state backed-up

echo 'Preparing the PostgreSQL configuration.'
if [[ ! -f "$config_dir/secrets/postgres-password" ]]; then
    python3 - "$config_dir/secrets/postgres-password" <<'PY'
import os, secrets, sys
fd = os.open(sys.argv[1], os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
with os.fdopen(fd, 'w') as stream:
    stream.write(secrets.token_hex(32))
PY
fi
PGPW="$(read_secret postgres-password)" python3 - "$config_dir/appsettings.Production.json" <<'PY'
import json, os, sys, tempfile
path = sys.argv[1]
settings = json.load(open(path))
connections = settings.setdefault('ConnectionStrings', {})
connections.pop('MySqlConnection', None)
connections['PostgreSqlConnection'] = f'Host=postgres;Database=automaton;Username=automaton;Password={os.environ["PGPW"]}'
if isinstance(settings.get('AppConfig'), dict):
    settings['AppConfig'].pop('DatabaseType', None)
mode = os.stat(path).st_mode & 0o777
with tempfile.NamedTemporaryFile(mode='w', dir=os.path.dirname(path), delete=False) as stream:
    json.dump(settings, stream, indent=2)
    temporary = stream.name
os.chmod(temporary, mode)
os.replace(temporary, path)
PY
if [[ " ${application_services[*]} " == *' store '* ]]; then python3 "$bundle/configure.py" --config-dir "$config_dir" --ensure-store; fi
python3 "$bundle/configure.py" --config-dir "$config_dir" --ensure-studio-paths
old_runner_image=$(grep -m1 '^RUNNER_IMAGE=' "$install_dir/release.env" | cut -d= -f2-)
new_runner_image=$(grep -m1 '^RUNNER_IMAGE=' "$bundle/release.env" | cut -d= -f2-)
if [[ "$old_runner_image" != "$new_runner_image" ]]; then python3 "$bundle/configure.py" --config-dir "$config_dir" --update-runner-image "$old_runner_image" "$new_runner_image"; fi

# What the migration added to the configuration directory, so a rollback removes exactly that.
(cd "$config_dir" && find . -type f -printf '%P\n' | LC_ALL=C sort) > "$working/config-after.txt"
LC_ALL=C comm -13 "$recovery/config-files.txt" "$working/config-after.txt" > "$recovery/config-added.txt"
echo 'Starting PostgreSQL and creating the schema with the new API (it receives no traffic).'
"${new_compose[@]}" up -d --no-deps --wait --wait-timeout 300 postgres
schema_container="$project-migration-schema"
docker rm -f "$schema_container" >/dev/null 2>&1 || true
"${new_compose[@]}" run -d --no-deps --name "$schema_container" api >/dev/null
for _ in $(seq 1 120); do
    [[ "$(docker inspect -f '{{.State.Health.Status}}' "$schema_container")" == healthy ]] && break
    [[ "$(docker inspect -f '{{.State.Running}}' "$schema_container")" == true ]] || { docker logs --tail 40 "$schema_container" >&2; echo 'The new API stopped while creating the schema.' >&2; false; }
    sleep 3
done
[[ "$(docker inspect -f '{{.State.Health.Status}}' "$schema_container")" == healthy ]] || { echo 'The new API did not become healthy.' >&2; false; }
docker rm -f "$schema_container" >/dev/null

echo 'Copying the data.'
run_loader copy
echo 'Verifying the copy.'
run_loader verify

echo 'Starting the PostgreSQL stack (the switch).'
phase=switched
set_state switched
"${new_compose[@]}" up -d --no-deps --wait --wait-timeout 600 --force-recreate "${application_services[@]}"
finish_switch
