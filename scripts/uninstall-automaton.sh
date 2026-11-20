#!/usr/bin/env bash
# Remove an Automaton server installed by install-automaton.sh. Docker itself and fail2ban stay installed.
# Safe to run twice and on a half-installed server. Only Automaton's own containers, networks and units are touched.
set -euo pipefail
umask 077
install_dir=/opt/automaton-server
config_dir=/etc/automaton
runners_dir=/opt/automaton/self-hosted-runners
systemd_dir=/etc/systemd/system
f2b_dir=/etc/fail2ban
state_dir=/var/lib/automaton
logrotate_dir=/etc/logrotate.d
delete_data=false
assume_yes=false
confirm=''
final_backup=true
dry_run=false
while (($#)); do
    case "$1" in
        --delete-data) delete_data=true; shift ;;
        --yes) assume_yes=true; shift ;;
        --confirm) confirm="${2:?Missing domain}"; shift 2 ;;
        --no-final-backup) final_backup=false; shift ;;
        --dry-run) dry_run=true; shift ;;
        --install-dir) install_dir="${2:?Missing installation directory}"; shift 2 ;;
        --config-dir) config_dir="${2:?Missing configuration directory}"; shift 2 ;;
        --help)
            printf '%s\n' 'Usage: sudo bash uninstall-automaton.sh [--delete-data --confirm DOMAIN [--no-final-backup]] [--dry-run]' \
                'Removes the Automaton server stack of the install it is pointed at (its own containers, network and, with --delete-data, volumes),' \
                'the Runner installed with "runners install --local" and its app containers and networks, the systemd units, the fail2ban jails and filters,' \
                'and /opt/automaton-server and /etc/automaton. Docker stays installed. Other installs on the host are left alone.' \
                'Data is kept by default: Docker volumes (database, app data), nightly backups in <install dir>/backups and the backup key.' \
                '--delete-data       also delete all volumes, backups and the backup key; needs --confirm DOMAIN (or typing the domain in a terminal)' \
                '--confirm DOMAIN    the server domain (see /etc/automaton/installation.json); --yes alone never confirms deleting data' \
                '--no-final-backup   skip the final backup that --delete-data takes into /var/backups first' \
                '--dry-run           print what would be removed and change nothing' \
                'Optional: --install-dir PATH --config-dir PATH'
            exit 0 ;;
        *) printf 'Unknown option: %s\n' "$1" >&2; exit 1 ;;
    esac
done
for dir in "$install_dir" "$config_dir" "$runners_dir" "$systemd_dir" "$f2b_dir" "$state_dir" "$logrotate_dir"; do
    [[ "$dir" =~ ^/[a-zA-Z0-9_./-]+$ && "$dir" != / && ! "$dir" =~ (^|/)\.\.(/|$) && $(realpath -m -- "$dir") == "$dir" ]] \
        || { echo "Refusing directory '$dir': use an absolute path without shell metacharacters or '..'; it and its parents must not be symlinks." >&2; exit 1; }
done
only_kept() { # dir keptname...: true when the directory holds nothing but the named entries
    local dir=$1 entry; shift
    for entry in "$dir"/* "$dir"/.[!.]*; do
        [[ -e "$entry" ]] || continue
        [[ " $* " == *" ${entry##*/} "* ]] || return 1
    done
}
marker_name=.automaton-uninstalled
# Keep mode writes this root-owned marker into each kept directory; only a directory that carries it (and nothing but kept items) may be removed on a later run.
has_marker() { [[ -f "$1/$marker_name" && ! -L "$1/$marker_name" && $(stat -c %u -- "$1/$marker_name") == 0 ]]; }
write_marker() { # dir: record that an earlier keep-mode run left this directory
    $dry_run || [[ ! -d "$1" ]] || printf 'Kept by uninstall-automaton.sh for %s on %s\n' "${domain:-unknown domain}" "$(date -u +%F)" > "$1/$marker_name" 2>/dev/null || true
}
# A custom directory must carry Automaton's own markers (a generic compose.yaml or update.sh is not enough); an empty one is refused.
# A directory an earlier keep-mode run emptied still counts only with its marker, so a second run stays safe and idempotent.
has_compose_name() { grep -Eqs '^name:[[:space:]]*"?automaton-server"?[[:space:]]*$' "$1/compose.yaml" "$1/docker-compose.yml"; }
if [[ "$install_dir" != /opt/automaton-server ]]; then
    if [[ ! -d "$install_dir" ]] || { ! { has_compose_name "$install_dir" && [[ -e "$install_dir/update.sh" || -e "$install_dir/release.env" ]]; } \
        && ! { only_kept "$install_dir" backups "$marker_name" && has_marker "$install_dir"; }; }; then
        echo "Refusing $install_dir: it is not /opt/automaton-server and has no compose file named automaton-server with update.sh or release.env." >&2; exit 1
    fi
fi
if [[ "$config_dir" != /etc/automaton ]]; then
    if [[ ! -d "$config_dir" ]] || { [[ ! -e "$config_dir/installation.json" && ! -e "$config_dir/secrets/setup-token" ]] \
        && ! { only_kept "$config_dir" backup-recipient secrets "$marker_name" && has_marker "$config_dir"; }; }; then
        echo "Refusing $config_dir: it is not /etc/automaton and has no installation.json or secrets/setup-token." >&2; exit 1
    fi
fi
[[ $EUID == 0 ]] || $dry_run || { echo 'Run the uninstaller with sudo.' >&2; exit 1; }
command -v docker >/dev/null && docker info >/dev/null 2>&1 && have_docker=true || have_docker=false
act() { # run a command, or only print it with --dry-run
    if $dry_run; then printf '  [dry-run] %s\n' "$*"; else "$@" >/dev/null 2>&1 || true; fi
}
removed=(); kept=(); warnings=()
domain=''
[[ -r "$config_dir/installation.json" ]] && domain=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["domain"])' "$config_dir/installation.json" 2>/dev/null || true)
if $delete_data && ! $dry_run; then
    word=${domain:-delete}
    if [[ -z $confirm ]]; then
        [[ -t 0 ]] || { echo "Deleting data needs --confirm $word (--yes does not confirm it)." >&2; exit 1; }
        read -r -p "This permanently deletes all Automaton data, volumes and backups. Type '$word' to continue: " confirm
    fi
    [[ "$confirm" == "$word" ]] || { echo 'Confirmation did not match; nothing was changed.' >&2; exit 1; }
fi
# The stack is this install's Compose project, matched by the project name and the directory it runs from; a project of the same name
# that runs from another directory is another install, and neither its containers nor its network and volumes are touched.
project=$(sed -n 's/^name:[[:space:]]*"\{0,1\}\([A-Za-z0-9_-]*\)"\{0,1\}[[:space:]]*$/\1/p' "$install_dir/compose.yaml" "$install_dir/docker-compose.yml" 2>/dev/null | head -n1 || true)
project=${project:-automaton-server}
own_stack=true; stack_note=''
if $have_docker; then
    other=$(docker ps -a --filter "label=com.docker.compose.project=$project" --format '{{.Label "com.docker.compose.project.working_dir"}}' | grep -vxF -- "$install_dir" | head -n1 || true)
    if [[ -n $other ]]; then
        own_stack=false
        stack_note="Compose project $project also runs from $other: its network and volumes were left alone."
    fi
fi
# Units, fail2ban jails, the state directory and the Runner folder are host-wide: only the install they point at may remove them.
own_host=true
if [[ -e "$systemd_dir/automaton-backup.service" ]] && ! grep -qE -- "(^|[[:space:]=])${install_dir//./\\.}/" "$systemd_dir/automaton-backup.service"; then
    own_host=false
    host_note="The systemd units, fail2ban jails, $state_dir and $runners_dir belong to another install on this host and were left alone."
fi
final_backup_dir=''
if $delete_data && $final_backup; then
    stamp=$(date -u +%Y%m%dT%H%M%SZ)
    final_backup_dir=/var/backups/automaton-final-$stamp
    if $dry_run; then echo "  [dry-run] final backup into $final_backup_dir"
    else
        $have_docker && [[ -f "$install_dir/backup.sh" && -r "$config_dir/secrets/backup-key" ]] \
            || { echo "No final backup possible (Docker, $install_dir/backup.sh or the backup key is missing). Nothing was changed; pass --no-final-backup to delete without one." >&2; exit 1; }
        bash "$install_dir/backup.sh" --install-dir "$install_dir" --keep 1000 >&2 \
            || { echo 'The final backup failed. Nothing was changed; pass --no-final-backup to delete without one.' >&2; exit 1; }
        newest=$(find "$install_dir/backups/nightly" -maxdepth 1 -name 'automaton-*.tar.age' -printf '%f\n' | sort | tail -n1)
        [[ -n $newest && -s "$install_dir/backups/nightly/$newest" ]] || { echo 'The final backup left no archive. Nothing was changed.' >&2; exit 1; }
        install -d -m 0755 /var/backups && final_backup_dir=$(mktemp -d "$final_backup_dir.XXXX") || { echo 'Cannot create the final backup folder. Nothing was changed.' >&2; exit 1; }
        env_config=$(grep -m1 '^CONFIG_DIR=' "$install_dir/.env" 2>/dev/null | cut -d= -f2- || true)
        [[ -z $env_config || $env_config == "$config_dir" ]] || { rmdir "$final_backup_dir"; echo "The install's .env says CONFIG_DIR=$env_config, not $config_dir: the backup key would not match. Nothing was changed." >&2; exit 1; }
        cp -- "$install_dir/backups/nightly/$newest" "$config_dir/secrets/backup-key" "$final_backup_dir/"
        (cd "$final_backup_dir" && [[ $(sha256sum < "$newest") == $(sha256sum < "$install_dir/backups/nightly/$newest") ]]) \
            || { echo 'The copy of the final backup does not match. Nothing was changed.' >&2; exit 1; }
        removed+=("final backup saved in $final_backup_dir ($newest and backup-key; keep it private)")
    fi
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

# 2. The server stack (this install's Compose project): containers and network; volumes only with --delete-data.
if $have_docker; then
    mapfile -t c < <(docker ps -aq --filter "label=com.docker.compose.project=$project" --filter "label=com.docker.compose.project.working_dir=$install_dir")
    remove_ids container "${c[@]}"
fi
if $have_docker && $own_stack; then
    mapfile -t n < <(docker network ls -q --filter "label=com.docker.compose.project=$project")
    remove_ids network "${n[@]}"
    mapfile -t v < <(docker volume ls -q --filter "label=com.docker.compose.project=$project")
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

if $own_host; then
# 3. systemd units, the fail2ban jails and filters the installer added.
for unit in automaton-update.path automaton-update.service automaton-backup.timer automaton-backup.service \
    automaton-backup-request.path automaton-backup-request.service automaton-cleanup.timer automaton-cleanup.service \
    automaton-restart.path automaton-restart.service automaton-os-update.path automaton-os-update.service; do
    if [[ -e "$systemd_dir/$unit" ]]; then
        act systemctl disable --now "$unit"; act rm -f -- "$systemd_dir/$unit"; removed+=("unit $unit")
    fi
done
for file in "$systemd_dir/fail2ban.service.d/automaton.conf" "$f2b_dir/jail.d/automaton.conf" "$f2b_dir/jail.d/automaton-allowlist.local" \
    "$f2b_dir/filter.d/automaton-scanner.conf" "$f2b_dir/filter.d/automaton-signin.conf" "$logrotate_dir/automaton-traefik"; do
    if [[ -e "$file" ]]; then act rm -f -- "$file"; removed+=("file $file"); fi
done
# A restart scheduled from Studio is a transient timer; stop it so the host does not reboot after the uninstall.
if ! $dry_run && command -v systemctl >/dev/null; then
    systemctl stop automaton-restart-scheduled.timer >/dev/null 2>&1 || true
fi
if ! $dry_run; then
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

fi
[[ -z ${host_note:-} ]] || warnings+=("$host_note")
[[ -z $stack_note ]] || warnings+=("$stack_note")

# 4. Installation and configuration directories. Without --delete-data the encrypted backups and the key that decrypts them stay.
backup_dir="$install_dir/backups"
if [[ -d "$install_dir" ]]; then
    if $delete_data; then
        act rm -rf -- "$install_dir"; removed+=("directory $install_dir")
    else
        n=0
        for entry in "$install_dir"/* "$install_dir"/.[!.]*; do
            [[ -e "$entry" && "$entry" != "$backup_dir" && "$entry" != "$install_dir/$marker_name" ]] || continue
            act rm -rf -- "$entry"; n=1
        done
        ((n)) && removed+=("directory $install_dir (except backups)")
        [[ -d "$backup_dir" ]] && kept+=("backups in $backup_dir") && write_marker "$install_dir"
    fi
fi
if [[ -d "$config_dir" ]]; then
    if $delete_data; then
        act rm -rf -- "$config_dir"; removed+=("directory $config_dir")
    else
        n=0
        for entry in "$config_dir"/* "$config_dir"/.[!.]*; do
            [[ -e "$entry" && "$entry" != "$config_dir/backup-recipient" && "$entry" != "$config_dir/secrets" && "$entry" != "$config_dir/$marker_name" ]] || continue
            act rm -rf -- "$entry"; n=1
        done
        for entry in "$config_dir/secrets"/* "$config_dir/secrets"/.[!.]*; do
            [[ -e "$entry" && "$entry" != "$config_dir/secrets/backup-key" ]] || continue
            act rm -rf -- "$entry"; n=1
        done
        ((n)) && removed+=("directory $config_dir (except the backup key)")
        [[ -e "$config_dir/secrets/backup-key" ]] && kept+=("backup key $config_dir/secrets/backup-key (decrypts the backups; keep it private)") && write_marker "$config_dir"
    fi
fi

echo
if $dry_run; then echo 'Would remove:'; else echo 'Removed:'; fi
((${#removed[@]})) && printf '  %s\n' "${removed[@]}" || echo '  nothing (already uninstalled)'
if ((${#warnings[@]})); then echo 'Warnings:'; printf '  %s\n' "${warnings[@]}"; fi
if $delete_data && ! $have_docker; then echo 'Warning: Docker is not running, so no volumes were removed (database and app data volumes remain). Start Docker and run this script again with --delete-data.'; fi
if ! $delete_data && ! $have_docker; then echo 'Warning: Docker is not running, so volumes could not be checked: database and app data volumes may remain. Start Docker and run this script again to list them.'; fi
if ! $delete_data; then
    echo 'Kept:'
    ((${#kept_volumes[@]})) && printf '  volume %s\n' "${kept_volumes[@]}"
    ((${#kept[@]})) && printf '  %s\n' "${kept[@]}"
    ((${#kept_volumes[@]} + ${#kept[@]})) || echo '  nothing to keep'
    echo 'To restore after a fresh install, use restore.sh with a backup and the key above. Remove everything with: --delete-data'
    echo 'The installer refuses to start over kept volumes without their configuration: for a clean reinstall run this script again with --delete-data.'
fi
