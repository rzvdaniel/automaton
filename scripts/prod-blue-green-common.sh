#!/bin/bash
# Shared helpers for production blue/green deploy scripts.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/docker-compose.prod.yml"
ENV_FILE="$REPO_ROOT/.env"
DYNAMIC_DIR="$REPO_ROOT/docker/traefik/dynamic"
STATE_FILE="$DYNAMIC_DIR/blue-green-state.env"
ROUTES_FILE="$DYNAMIC_DIR/blue-green-routes.yml"

LIVE_API_COLOR_DEFAULT="blue"
LIVE_STUDIO_COLOR_DEFAULT="blue"
LIVE_WEBSITE_COLOR_DEFAULT="blue"

ensure_dynamic_dir() {
    mkdir -p "$DYNAMIC_DIR"
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
}

write_default_state() {
    ensure_dynamic_dir
    cat > "$STATE_FILE" <<EOF
LIVE_API_COLOR=$LIVE_API_COLOR_DEFAULT
LIVE_STUDIO_COLOR=$LIVE_STUDIO_COLOR_DEFAULT
LIVE_WEBSITE_COLOR=$LIVE_WEBSITE_COLOR_DEFAULT
EOF
}

load_state() {
    ensure_dynamic_dir

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
    ensure_dynamic_dir
    cat > "$STATE_FILE" <<EOF
LIVE_API_COLOR=$LIVE_API_COLOR
LIVE_STUDIO_COLOR=$LIVE_STUDIO_COLOR
LIVE_WEBSITE_COLOR=$LIVE_WEBSITE_COLOR
EOF
}

render_routes_file() {
    load_prod_env
    load_state

    cat > "$ROUTES_FILE" <<EOF
http:
  routers:
    api:
      rule: "Host(\`${API_DOMAIN}\`)"
      entryPoints:
        - websecure
      tls:
        certResolver: letsencrypt
      service: api-${LIVE_API_COLOR}@docker

    studio:
      rule: "Host(\`${STUDIO_DOMAIN}\`)"
      entryPoints:
        - websecure
      tls:
        certResolver: letsencrypt
      service: studio-${LIVE_STUDIO_COLOR}@docker

    website:
      rule: "Host(\`${WEBSITE_DOMAIN}\`) || Host(\`www.${WEBSITE_DOMAIN}\`)"
      entryPoints:
        - websecure
      tls:
        certResolver: letsencrypt
      service: website-${LIVE_WEBSITE_COLOR}@docker
EOF
}

ensure_blue_green_bootstrap() {
    ensure_dynamic_dir
    if [[ ! -f "$STATE_FILE" ]]; then
        write_default_state
    fi

    render_routes_file
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

set_live_color() {
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
    render_routes_file
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

ensure_infrastructure() {
    ensure_blue_green_bootstrap
    echo "[INFO] Ensuring MySQL, socket proxy, and Traefik are up"
    docker compose -f "$COMPOSE_FILE" up -d --remove-orphans mysql socket-proxy traefik
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
    printf -- "\nState file: %s\nRoutes file: %s\n" "$STATE_FILE" "$ROUTES_FILE"
}
