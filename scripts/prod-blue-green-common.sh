#!/bin/bash
# Shared helpers for nginx-based production blue/green deploy scripts.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/docker-compose.prod.yml"
ENV_FILE="$REPO_ROOT/.env"
NGINX_TEMPLATE_DIR="$REPO_ROOT/docker/nginx/prod"
RUNTIME_ROOT="$REPO_ROOT/.prod-runtime"
NGINX_RUNTIME_DIR="$RUNTIME_ROOT/nginx"
NGINX_CONF_DIR="$NGINX_RUNTIME_DIR/conf.d"
NGINX_UPSTREAMS_DIR="$NGINX_RUNTIME_DIR/upstreams"
STATE_DIR="$NGINX_RUNTIME_DIR/state"
STATE_FILE="$STATE_DIR/blue-green-state.env"
CERTBOT_WWW_DIR="$RUNTIME_ROOT/certbot-www"
LETSENCRYPT_DIR="$RUNTIME_ROOT/letsencrypt"

LIVE_API_COLOR_DEFAULT="blue"
LIVE_STUDIO_COLOR_DEFAULT="blue"
LIVE_WEBSITE_COLOR_DEFAULT="blue"
BOOTSTRAP_UPSTREAM_HOST="127.0.0.1"

ensure_runtime_dirs() {
    mkdir -p "$NGINX_CONF_DIR" "$NGINX_UPSTREAMS_DIR" "$STATE_DIR" "$CERTBOT_WWW_DIR" "$LETSENCRYPT_DIR"
}

ensure_env_file() {
    if [[ ! -f "$ENV_FILE" ]]; then
        echo "[ERROR] Missing $ENV_FILE" >&2
        echo "Copy .env.prod.example to .env on the production server before running blue/green deploy scripts." >&2
        exit 1
    fi
}

load_prod_env() {
    ensure_env_file
    set -a
    # shellcheck disable=SC1090
    source "$ENV_FILE"
    set +a

    : "${API_DOMAIN:?API_DOMAIN is required in .env}"
    : "${STUDIO_DOMAIN:?STUDIO_DOMAIN is required in .env}"
    : "${WEBSITE_DOMAIN:?WEBSITE_DOMAIN is required in .env}"
    : "${ACME_EMAIL:?ACME_EMAIL is required in .env}"

    CERT_NAME="${CERT_NAME:-$WEBSITE_DOMAIN}"
    RUNNER_RECONNECT_GRACE_SECONDS="${RUNNER_RECONNECT_GRACE_SECONDS:-20}"
}

write_default_state() {
    ensure_runtime_dirs
    cat > "$STATE_FILE" <<EOF
LIVE_API_COLOR=$LIVE_API_COLOR_DEFAULT
LIVE_STUDIO_COLOR=$LIVE_STUDIO_COLOR_DEFAULT
LIVE_WEBSITE_COLOR=$LIVE_WEBSITE_COLOR_DEFAULT
EOF
}

load_state() {
    ensure_runtime_dirs

    if [[ ! -f "$STATE_FILE" ]]; then
        write_default_state
    fi

    # shellcheck disable=SC1090
    source "$STATE_FILE"

    LIVE_API_COLOR="${LIVE_API_COLOR:-$LIVE_API_COLOR_DEFAULT}"
    LIVE_STUDIO_COLOR="${LIVE_STUDIO_COLOR:-$LIVE_STUDIO_COLOR_DEFAULT}"
    LIVE_WEBSITE_COLOR="${LIVE_WEBSITE_COLOR:-$LIVE_WEBSITE_COLOR_DEFAULT}"
}

save_state() {
    ensure_runtime_dirs
    cat > "$STATE_FILE" <<EOF
LIVE_API_COLOR=$LIVE_API_COLOR
LIVE_STUDIO_COLOR=$LIVE_STUDIO_COLOR
LIVE_WEBSITE_COLOR=$LIVE_WEBSITE_COLOR
EOF
}

render_template() {
    local template_file="$1"
    local destination_file="$2"

    sed \
        -e "s|__API_DOMAIN__|$API_DOMAIN|g" \
        -e "s|__STUDIO_DOMAIN__|$STUDIO_DOMAIN|g" \
        -e "s|__WEBSITE_DOMAIN__|$WEBSITE_DOMAIN|g" \
        -e "s|__CERT_NAME__|$CERT_NAME|g" \
        "$template_file" > "$destination_file"
}

upstream_destination() {
    local service="$1"
    local color="$2"

    case "$service" in
        api) echo "server api-$color:8080;" ;;
        studio) echo "server studio-$color:80;" ;;
        website) echo "server website-$color:80;" ;;
        *)
            echo "[ERROR] Unknown service: $service" >&2
            exit 1
            ;;
    esac
}

bootstrap_upstream_destination() {
    local service="$1"

    case "$service" in
        api) echo "server $BOOTSTRAP_UPSTREAM_HOST:8080;" ;;
        studio) echo "server $BOOTSTRAP_UPSTREAM_HOST:8081;" ;;
        website) echo "server $BOOTSTRAP_UPSTREAM_HOST:8082;" ;;
        *)
            echo "[ERROR] Unknown service: $service" >&2
            exit 1
            ;;
    esac
}

render_service_upstream() {
    local service="$1"
    local color="$2"
    local destination="$NGINX_UPSTREAMS_DIR/${service}-live.conf"

    printf '%s\n' "$(upstream_destination "$service" "$color")" > "$destination"
}

render_bootstrap_upstreams() {
    printf '%s\n' "$(bootstrap_upstream_destination api)" > "$NGINX_UPSTREAMS_DIR/api-live.conf"
    printf '%s\n' "$(bootstrap_upstream_destination studio)" > "$NGINX_UPSTREAMS_DIR/studio-live.conf"
    printf '%s\n' "$(bootstrap_upstream_destination website)" > "$NGINX_UPSTREAMS_DIR/website-live.conf"
}

render_all_upstreams() {
    load_state
    render_service_upstream api "$LIVE_API_COLOR"
    render_service_upstream studio "$LIVE_STUDIO_COLOR"
    render_service_upstream website "$LIVE_WEBSITE_COLOR"
}

render_nginx_runtime() {
    load_prod_env
    ensure_runtime_dirs

    cp "$NGINX_TEMPLATE_DIR/nginx.conf" "$NGINX_RUNTIME_DIR/nginx.conf"
    render_template "$NGINX_TEMPLATE_DIR/conf.d/api.conf.template" "$NGINX_CONF_DIR/api.conf"
    render_template "$NGINX_TEMPLATE_DIR/conf.d/studio.conf.template" "$NGINX_CONF_DIR/studio.conf"
    render_template "$NGINX_TEMPLATE_DIR/conf.d/website.conf.template" "$NGINX_CONF_DIR/website.conf"

    if [[ -f "$STATE_FILE" ]]; then
        render_all_upstreams
    else
        render_bootstrap_upstreams
    fi
}

ensure_placeholder_certificate() {
    local cert_dir="$LETSENCRYPT_DIR/live/$CERT_NAME"
    local fullchain="$cert_dir/fullchain.pem"
    local privkey="$cert_dir/privkey.pem"
    local marker="$cert_dir/.automaton-bootstrap-placeholder"

    if [[ -f "$fullchain" && -f "$privkey" ]]; then
        return
    fi

    echo "[INFO] Creating temporary self-signed certificate for nginx bootstrap"
    mkdir -p "$cert_dir"

    openssl req -x509 -nodes -newkey rsa:2048 -days 7 \
        -keyout "$privkey" \
        -out "$fullchain" \
        -subj "/CN=$WEBSITE_DOMAIN" \
        -addext "subjectAltName=DNS:$WEBSITE_DOMAIN,DNS:www.$WEBSITE_DOMAIN,DNS:$API_DOMAIN,DNS:$STUDIO_DOMAIN" \
        > /dev/null 2>&1

    printf 'bootstrap\n' > "$marker"
}

bootstrap_placeholder_exists() {
    [[ -f "$LETSENCRYPT_DIR/live/$CERT_NAME/.automaton-bootstrap-placeholder" ]]
}

remove_bootstrap_placeholder_certificate() {
    local cert_dir="$LETSENCRYPT_DIR/live/$CERT_NAME"

    if bootstrap_placeholder_exists; then
        echo "[INFO] Removing temporary bootstrap certificate files before Certbot issuance"
        rm -rf "$cert_dir"
    fi
}

ensure_acme_webroot() {
    mkdir -p "$CERTBOT_WWW_DIR/.well-known/acme-challenge"
    printf 'ok\n' > "$CERTBOT_WWW_DIR/.well-known/acme-challenge/healthz"
}

ensure_blue_green_bootstrap() {
    load_prod_env
    ensure_runtime_dirs
    ensure_acme_webroot

    render_nginx_runtime
    ensure_placeholder_certificate
}

other_color() {
    case "$1" in
        blue) echo "green" ;;
        green) echo "blue" ;;
        *)
            echo "[ERROR] Unknown color: $1" >&2
            exit 1
            ;;
    esac
}

get_live_color() {
    load_state
    case "$1" in
        api) echo "$LIVE_API_COLOR" ;;
        studio) echo "$LIVE_STUDIO_COLOR" ;;
        website) echo "$LIVE_WEBSITE_COLOR" ;;
        *)
            echo "[ERROR] Unknown service: $1" >&2
            exit 1
            ;;
    esac
}

set_live_color_record() {
    local service="$1"
    local color="$2"

    load_state

    case "$service" in
        api) LIVE_API_COLOR="$color" ;;
        studio) LIVE_STUDIO_COLOR="$color" ;;
        website) LIVE_WEBSITE_COLOR="$color" ;;
        *)
            echo "[ERROR] Unknown service: $service" >&2
            exit 1
            ;;
    esac

    save_state
}

compose_service_name() {
    local service="$1"
    local color="$2"
    echo "${service}-${color}"
}

service_container_ids() {
    local compose_service="$1"
    docker compose -f "$COMPOSE_FILE" ps -q "$compose_service"
}

service_exists() {
    local compose_service="$1"
    if [[ -n "$(service_container_ids "$compose_service")" ]]; then
        return 0
    fi
    return 1
}

resolve_target_color() {
    local service="$1"
    local live_color
    live_color="$(get_live_color "$service")"
    local live_service
    local inactive_service

    live_service="$(compose_service_name "$service" "$live_color")"
    inactive_service="$(compose_service_name "$service" "$(other_color "$live_color")")"

    if ! service_exists "$live_service" && ! service_exists "$inactive_service"; then
        echo "$live_color"
        return
    fi

    echo "$(other_color "$live_color")"
}

wait_for_service_healthy() {
    local compose_service="$1"
    local timeout_seconds="${2:-240}"
    local started_at
    started_at="$(date +%s)"

    echo "[INFO] Waiting for $compose_service to become healthy"

    while true; do
        mapfile -t container_ids < <(service_container_ids "$compose_service")

        if [[ ${#container_ids[@]} -gt 0 ]]; then
            local all_ready=true

            for container_id in "${container_ids[@]}"; do
                if [[ -z "$container_id" ]]; then
                    all_ready=false
                    break
                fi

                local status
                status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container_id")"

                if [[ "$status" != "healthy" && "$status" != "running" ]]; then
                    all_ready=false
                    break
                fi
            done

            if [[ "$all_ready" == "true" ]]; then
                echo "[INFO] $compose_service is ready"
                return 0
            fi
        fi

        if (( "$(date +%s)" - started_at >= timeout_seconds )); then
            echo "[ERROR] Timed out waiting for $compose_service to become healthy" >&2
            docker compose -f "$COMPOSE_FILE" ps "$compose_service" >&2 || true
            docker compose -f "$COMPOSE_FILE" logs --tail 100 "$compose_service" >&2 || true
            exit 1
        fi

        sleep 2
    done
}

nginx_test() {
    docker compose -f "$COMPOSE_FILE" exec -T nginx nginx -t
}

nginx_reload() {
    nginx_test
    docker compose -f "$COMPOSE_FILE" exec -T nginx nginx -s reload
}

verify_live_origin() {
    local service="$1"

    case "$service" in
        api)
            curl -kfsS --resolve "$API_DOMAIN:443:127.0.0.1" "https://$API_DOMAIN/healthz" > /dev/null
            ;;
        studio)
            curl -kIfsS --resolve "$STUDIO_DOMAIN:443:127.0.0.1" "https://$STUDIO_DOMAIN/" > /dev/null
            ;;
        website)
            curl -kIfsS --resolve "$WEBSITE_DOMAIN:443:127.0.0.1" "https://$WEBSITE_DOMAIN/" > /dev/null
            curl -kIfsS --resolve "www.$WEBSITE_DOMAIN:443:127.0.0.1" "https://www.$WEBSITE_DOMAIN/" > /dev/null
            ;;
        *)
            echo "[ERROR] Unknown service: $service" >&2
            exit 1
            ;;
    esac
}

switch_live_color() {
    local service="$1"
    local color="$2"
    local previous_color

    previous_color="$(get_live_color "$service")"

    render_service_upstream "$service" "$color"

    if ! nginx_reload; then
        echo "[ERROR] nginx reload failed while switching $service to $color. Reverting selector." >&2
        render_service_upstream "$service" "$previous_color"
        nginx_reload || true
        exit 1
    fi

    verify_live_origin "$service"
    set_live_color_record "$service" "$color"
}

ensure_infrastructure() {
    ensure_blue_green_bootstrap
    echo "[INFO] Ensuring MySQL and nginx are up"
    docker compose -f "$COMPOSE_FILE" up -d --remove-orphans mysql nginx
    wait_for_service_healthy nginx 120
}

reset_blue_green_to_blue() {
    LIVE_API_COLOR="$LIVE_API_COLOR_DEFAULT"
    LIVE_STUDIO_COLOR="$LIVE_STUDIO_COLOR_DEFAULT"
    LIVE_WEBSITE_COLOR="$LIVE_WEBSITE_COLOR_DEFAULT"
    save_state
    render_all_upstreams
}

deploy_color() {
    local service="$1"
    local color="$2"
    local compose_service
    compose_service="$(compose_service_name "$service" "$color")"

    echo "[INFO] Deploying $compose_service"
    docker compose -f "$COMPOSE_FILE" up -d --build --remove-orphans "$compose_service"
    wait_for_service_healthy "$compose_service"
}

stop_color() {
    local service="$1"
    local color="$2"
    local compose_service
    compose_service="$(compose_service_name "$service" "$color")"

    if service_exists "$compose_service"; then
        echo "[INFO] Stopping previous color $compose_service"
        docker compose -f "$COMPOSE_FILE" stop "$compose_service"
        docker compose -f "$COMPOSE_FILE" rm -f "$compose_service"
    fi
}

signal_api_color_runners_reconnect_when_idle() {
    local color="$1"
    local reason="${2:-API deploy cutover complete. Reconnect to the new live API instance when idle.}"
    local compose_service
    compose_service="$(compose_service_name api "$color")"

    if ! service_exists "$compose_service"; then
        echo "[INFO] Skip runner reconnect signal for $compose_service because it is not running"
        return 0
    fi

    mapfile -t container_ids < <(service_container_ids "$compose_service")
    if [[ ${#container_ids[@]} -eq 0 ]]; then
        echo "[INFO] No containers found for $compose_service"
        return 0
    fi

    for container_id in "${container_ids[@]}"; do
        local container_ip
        container_ip="$(docker inspect --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$container_id")"

        if [[ -z "$container_ip" ]]; then
            echo "[WARN] Could not resolve IP for API container $container_id" >&2
            continue
        fi

        echo "[INFO] Requesting reconnect-when-idle from API container $container_id ($container_ip)"
        if ! curl -fsS \
            -X POST \
            -H 'Content-Type: application/json' \
            --data "{\"reason\":\"${reason//\"/\\\"}\"}" \
            "http://$container_ip:8080/internal/runners/reconnect-when-idle" \
            > /dev/null; then
            echo "[WARN] Reconnect-when-idle signal was not accepted by API container $container_id. This is expected when the previous color is still on an older API version." >&2
        fi
    done
}

assert_service_exists() {
    local service="$1"
    local color="$2"
    local compose_service
    compose_service="$(compose_service_name "$service" "$color")"

    if ! service_exists "$compose_service"; then
        echo "[ERROR] Cannot switch to $compose_service because it is not running." >&2
        echo "Deploy that color first, then switch traffic." >&2
        exit 1
    fi
}

status_table() {
    load_state
    printf -- "Service   Live color\n"
    printf -- "-------   ----------\n"
    printf -- "api       %s\n" "$LIVE_API_COLOR"
    printf -- "studio    %s\n" "$LIVE_STUDIO_COLOR"
    printf -- "website   %s\n" "$LIVE_WEBSITE_COLOR"
    printf -- "\nState file: %s\nUpstreams dir: %s\n" "$STATE_FILE" "$NGINX_UPSTREAMS_DIR"
}
