#!/usr/bin/env bash
# Remove an Automaton server installed by install-automaton.sh. Docker itself and fail2ban stay installed.
# Safe to run twice and on a half-installed server. Only Automaton's own containers, networks and units are touched.
set -euo pipefail
umask 077
install_dir=/opt/automaton-server
config_dir=/etc/automaton
# The AUTOMATON_* overrides exist for tests only.
runners_dir=${AUTOMATON_RUNNERS_DIR:-/opt/automaton/self-hosted-runners}
systemd_dir=${AUTOMATON_SYSTEMD_DIR:-/etc/systemd/system}
f2b_dir=${AUTOMATON_FAIL2BAN_ETC:-/etc/fail2ban}
state_dir=${AUTOMATON_STATE_DIR:-/var/lib/automaton}
logrotate_dir=${AUTOMATON_LOGROTATE_DIR:-/etc/logrotate.d}
test_mode=${AUTOMATON_UNINSTALL_TEST:-}
delete_data=false
assume_yes=false
dry_run=false
while (($#)); do
    case "$1" in
        --delete-data) delete_data=true; shift ;;
        --yes) assume_yes=true; shift ;;
        --dry-run) dry_run=true; shift ;;
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --config-dir) config_dir="${2:?Missing configuration directory}"; shift 2 ;;
        --help)
            printf '%s\n' 'Usage: sudo bash uninstall-automaton.sh [--delete-data] [--yes] [--dry-run]' \
                'Removes the Automaton server stack, the Runner installed with "runners install --local" and its app containers and networks,' \
                'the systemd units, the fail2ban jails and filters, and /opt/automaton-server and /etc/automaton. Docker stays installed.' \
                'Data is kept by default: Docker volumes (database, app data), nightly backups in <install dir>/backups and the backup key.' \
                '--delete-data  also delete all volumes, backups and the backup key; you must type the domain to confirm' \
                '--yes          do not ask (scripts and agents)' \
                '--dry-run      print what would be removed and change nothing' \
                'Optional: --install-dir PATH --config-dir PATH'
            exit 0 ;;
        *) printf 'Unknown option: %s\n' "$1" >&2; exit 1 ;;
    esac
done
for dir in "$install_dir" "$config_dir" "$runners_dir" "$systemd_dir" "$f2b_dir" "$state_dir" "$logrotate_dir"; do
    [[ "$dir" =~ ^/[a-zA-Z0-9_./-]+$ && "$dir" != / && ! -L "$dir" && ! "$dir" =~ (^|/)\.\.(/|$) ]] \
        || { echo "Refusing directory '$dir': use an absolute path without shell metacharacters or '..'; symlinks are refused." >&2; exit 1; }
done
# Only delete a directory that is the default or is recognisably an Automaton install. A custom directory that an earlier
# (keep-mode) run already emptied, leaving only the kept items, still counts, so a second run stays safe and idempotent.
only_kept() { # dir keptname...: true when the directory holds nothing but the named entries
    local dir=$1 entry; shift
    for entry in "$dir"/* "$dir"/.[!.]*; do
        [[ -e "$entry" ]] || continue
        [[ " $* " == *" ${entry##*/} "* ]] || return 1
    done
}
if [[ "$install_dir" != /opt/automaton-server && -d "$install_dir" && ! -e "$install_dir/docker-compose.yml" && ! -e "$install_dir/compose.yaml" && ! -e "$install_dir/update.sh" ]] \
    && ! only_kept "$install_dir" backups; then
    echo "Refusing $install_dir: it is not /opt/automaton-server and holds no compose file or update.sh, so it does not look like an Automaton install." >&2; exit 1
fi
if [[ "$config_dir" != /etc/automaton && -d "$config_dir" && ! -e "$config_dir/installation.json" ]] \
    && ! only_kept "$config_dir" backup-recipient secrets; then
    echo "Refusing $config_dir: it is not /etc/automaton and holds no installation.json, so it does not look like Automaton configuration." >&2; exit 1
fi
[[ $EUID == 0 || -n $test_mode ]] || $dry_run || { echo 'Run the uninstaller with sudo.' >&2; exit 1; }
# Test mode never talks to Docker.
[[ -z $test_mode ]] && command -v docker >/dev/null && docker info >/dev/null 2>&1 && have_docker=true || have_docker=false
removed=(); kept=(); warnings=()
act() { # run a command, or only print it with --dry-run
    if $dry_run; then printf '  [dry-run] %s\n' "$*"; else "$@" >/dev/null 2>&1 || true; fi
}
domain=''
[[ -r "$config_dir/installation.json" ]] && domain=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["domain"])' "$config_dir/installation.json" 2>/dev/null || true)
if $delete_data && ! $dry_run && ! $assume_yes; then
    word=${domain:-delete}
    [[ -t 0 ]] || { echo 'Deleting data needs a terminal to confirm, or pass --yes.' >&2; exit 1; }
    read -r -p "This permanently deletes all Automaton data, volumes and backups. Type '$word' to continue: " answer
    [[ "$answer" == "$word" ]] || { echo 'Cancelled; nothing was changed.' >&2; exit 1; }
fi
$dry_run && echo 'Dry run: nothing is changed.'

# 1. Runners this server's installer created on this host (container env points at this server, found via the root directory).
runner_ids=(); runner_guids=()
if $have_docker; then
    while read -r name; do
        [[ "$name" =~ ^automaton-self-hosted-[0-9a-f]{12}$ ]] || continue
        env=$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$name" 2>/dev/null || true)
        if [[ -z "$domain" ]]; then echo "Warning: $name was left running: the domain is unknown (no $config_dir/installation.json). Remove it by hand if it belongs to this server." >&2; continue; fi
        grep -Eq "^AUTOMATON_SERVER_URL=https?://(api\.)?${domain//./\\.}/?$" <<<"$env" || continue
        id=$(sed -n 's/^AUTOMATON_RUNNER_INSTALLATION_ID=//p' <<<"$env" | head -n1)
        [[ "$id" =~ ^sh_[0-9a-f]{32}$ ]] || continue
        runner_ids+=("$id")
        guid=$(sed -n 's/.*"RunnerId"[[:space:]]*:[[:space:]]*"\([0-9a-fA-F-]\{36\}\)".*/\1/p' "$runners_dir/$id/runner.json" 2>/dev/null | head -n1 || true)
        runner_guids+=("${guid,,}")
    done < <(docker ps -a --format '{{.Names}}' 2>/dev/null)
fi
remove_ids() { # kind(container|network) ids...
    local kind=$1; shift
    local id
    for id in "$@"; do
        [[ -n "$id" ]] || continue
        local name
        name=$(docker "$kind" inspect --format '{{.Name}}' "$id" 2>/dev/null | tr -d / || true)
        name=${name:-$id}
        if [[ $kind == container ]]; then act docker rm --force --volumes=false "$id"; else act docker network rm "$id"; fi
        if ! $dry_run && docker "$kind" inspect "$id" >/dev/null 2>&1; then warnings+=("$kind $name could not be removed"); else removed+=("$kind $name"); fi
    done
}
volumes_to_delete=()
for i in "${!runner_ids[@]}"; do
    id=${runner_ids[$i]}; guid=${runner_guids[$i]}; short=${id:3:12}
    filters=()
    if [[ -n "$guid" ]]; then
        # App, build and Action containers and networks of this Runner (labels carry its Runner id).
        for label in "com.automaton.machine-runner-id=$guid" "automaton.machine-runner-id=$guid" "automaton.machine-runner-id=${guid//-/}"; do
            mapfile -t c < <(docker ps -aq --filter "label=$label")
            remove_ids container "${c[@]}"
            mapfile -t n < <(docker network ls -q --filter "label=$label")
            remove_ids network "${n[@]}"
            mapfile -t v < <(docker volume ls -q --filter "label=$label")
            volumes_to_delete+=("${v[@]}")
        done
    fi
    for c in "automaton-self-hosted-$short" "automaton-static-${id:3}"; do
        docker inspect "$c" >/dev/null 2>&1 && remove_ids container "$c"
    done
    docker network inspect "automaton-self-hosted-$id-runtime" >/dev/null 2>&1 && remove_ids network "automaton-self-hosted-$id-runtime"
    volumes_to_delete+=("automaton-machine-runner-$id-workspace")
    if [[ -d "$runners_dir/$id" ]]; then act rm -rf -- "$runners_dir/$id"; removed+=("directory $runners_dir/$id"); fi
done
[[ -d "$runners_dir" ]] && ! $dry_run && rmdir "$runners_dir" "$(dirname "$runners_dir")" 2>/dev/null || true

# 2. The server stack (Compose project automaton-server): containers and network; volumes only with --delete-data.
if $have_docker; then
    mapfile -t c < <(docker ps -aq --filter label=com.docker.compose.project=automaton-server)
    remove_ids container "${c[@]}"
    mapfile -t n < <(docker network ls -q --filter label=com.docker.compose.project=automaton-server)
    remove_ids network "${n[@]}"
    mapfile -t v < <(docker volume ls -q --filter label=com.docker.compose.project=automaton-server)
    volumes_to_delete+=("${v[@]}")
fi
kept_volumes=()
for volume in "${volumes_to_delete[@]}"; do
    [[ -n "$volume" ]] && docker volume inspect "$volume" >/dev/null 2>&1 || continue
    if $delete_data; then
        act docker volume rm "$volume"
        if ! $dry_run && docker volume inspect "$volume" >/dev/null 2>&1; then warnings+=("volume $volume could not be removed"); else removed+=("volume $volume"); fi
    else kept_volumes+=("$volume"); fi
done

# 3. systemd units, the fail2ban jails and filters the installer added.
for unit in automaton-update.path automaton-update.service automaton-backup.timer automaton-backup.service \
    automaton-backup-request.path automaton-backup-request.service automaton-cleanup.timer automaton-cleanup.service; do
    if [[ -e "$systemd_dir/$unit" ]]; then
        [[ -n $test_mode ]] || act systemctl disable --now "$unit"; act rm -f -- "$systemd_dir/$unit"; removed+=("unit $unit")
    fi
done
for file in "$systemd_dir/fail2ban.service.d/automaton.conf" "$f2b_dir/jail.d/automaton.conf" "$f2b_dir/jail.d/automaton-allowlist.local" \
    "$f2b_dir/filter.d/automaton-scanner.conf" "$f2b_dir/filter.d/automaton-signin.conf" "$logrotate_dir/automaton-traefik"; do
    if [[ -e "$file" ]]; then act rm -f -- "$file"; removed+=("file $file"); fi
done
if ! $dry_run && [[ -z $test_mode ]]; then
    rmdir "$systemd_dir/fail2ban.service.d" 2>/dev/null || true
    command -v systemctl >/dev/null && systemctl daemon-reload >/dev/null 2>&1 || true
    if command -v fail2ban-client >/dev/null && systemctl is-active --quiet fail2ban 2>/dev/null; then
        fail2ban-client reload >/dev/null 2>&1 || echo 'Warning: fail2ban did not reload; run: sudo fail2ban-client reload' >&2
    fi
fi
# update/ and backup/ only hold request and status files. The Runner's workflows and apps may hold app data: keep them unless --delete-data.
if [[ -d "$state_dir" ]]; then
    if $delete_data; then
        act rm -rf -- "$state_dir"; removed+=("directory $state_dir")
    else
        for entry in "$state_dir"/* "$state_dir"/.[!.]*; do
            [[ -e "$entry" && "$entry" != "$state_dir/infrastructure-runner" && "$entry" != "$state_dir/apps" ]] || continue
            act rm -rf -- "$entry"; removed+=("$entry")
        done
        for keep in infrastructure-runner apps; do [[ -e "$state_dir/$keep" ]] && kept+=("app data in $state_dir/$keep"); done
    fi
fi

# 4. Installation and configuration directories. Without --delete-data the encrypted backups and the key that decrypts them stay.
backup_dir="$install_dir/backups"
if [[ -d "$install_dir" ]]; then
    if $delete_data; then
        act rm -rf -- "$install_dir"; removed+=("directory $install_dir")
    else
        n=0
        for entry in "$install_dir"/* "$install_dir"/.[!.]*; do
            [[ -e "$entry" && "$entry" != "$backup_dir" ]] || continue
            act rm -rf -- "$entry"; n=1
        done
        ((n)) && removed+=("directory $install_dir (except backups)")
        [[ -d "$backup_dir" ]] && kept+=("backups in $backup_dir")
    fi
fi
if [[ -d "$config_dir" ]]; then
    if $delete_data; then
        act rm -rf -- "$config_dir"; removed+=("directory $config_dir")
    else
        n=0
        for entry in "$config_dir"/* "$config_dir"/.[!.]*; do
            [[ -e "$entry" && "$entry" != "$config_dir/backup-recipient" && "$entry" != "$config_dir/secrets" ]] || continue
            act rm -rf -- "$entry"; n=1
        done
        for entry in "$config_dir/secrets"/* "$config_dir/secrets"/.[!.]*; do
            [[ -e "$entry" && "$entry" != "$config_dir/secrets/backup-key" ]] || continue
            act rm -rf -- "$entry"; n=1
        done
        ((n)) && removed+=("directory $config_dir (except the backup key)")
        [[ -e "$config_dir/secrets/backup-key" ]] && kept+=("backup key $config_dir/secrets/backup-key (decrypts the backups; keep it private)")
    fi
fi

echo
if $dry_run; then echo 'Would remove:'; else echo 'Removed:'; fi
((${#removed[@]})) && printf '  %s\n' "${removed[@]}" || echo '  nothing (already uninstalled)'
if ((${#warnings[@]})); then echo 'Warnings:'; printf '  %s\n' "${warnings[@]}"; fi
if $delete_data && ! $have_docker; then echo 'Warning: Docker is not running, so no volumes were removed (database and app data volumes remain). Start Docker and run this script again with --delete-data.'; fi
if ! $delete_data; then
    echo 'Kept:'
    ((${#kept_volumes[@]})) && printf '  volume %s\n' "${kept_volumes[@]}"
    ((${#kept[@]})) && printf '  %s\n' "${kept[@]}"
    ((${#kept_volumes[@]} + ${#kept[@]})) || echo '  nothing to keep'
    echo 'To restore after a fresh install, use restore.sh with a backup and the key above. Remove everything with: --delete-data'
    echo 'The installer refuses to start over kept volumes without their configuration: for a clean reinstall run this script again with --delete-data.'
fi
