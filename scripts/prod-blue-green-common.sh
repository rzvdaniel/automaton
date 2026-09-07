#!/bin/bash
# Shared helpers for Traefik-based production blue-green deploy scripts.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/docker-compose.prod.yml"
ENV_FILE="$REPO_ROOT/.env"
NGINX_TEMPLATE_DIR="$REPO_ROOT/docker/nginx/prod"
APP_RUNTIME_SOURCE_DIR="$REPO_ROOT/docker/nginx/app-runtime"
TRAEFIK_TEMPLATE_DIR="$REPO_ROOT/docker/traefik/prod"
RUNTIME_ROOT="$REPO_ROOT/.prod-runtime"
NGINX_RUNTIME_DIR="$RUNTIME_ROOT/nginx"
NGINX_CONF_DIR="$NGINX_RUNTIME_DIR/conf.d"
NGINX_UPSTREAMS_DIR="$NGINX_RUNTIME_DIR/upstreams"
NGINX_APP_RUNTIME_DIR="$NGINX_RUNTIME_DIR/app-runtime"
TRAEFIK_RUNTIME_DIR="$RUNTIME_ROOT/traefik"
TRAEFIK_DYNAMIC_DIR="$TRAEFIK_RUNTIME_DIR/dynamic"
STATE_DIR="$NGINX_RUNTIME_DIR/state"
STATE_FILE="$STATE_DIR/blue-green-state.env"
CERTBOT_WWW_DIR="$RUNTIME_ROOT/certbot-www"
LETSENCRYPT_DIR="$RUNTIME_ROOT/letsencrypt"

MIGRATION_EXPECTED_RUNNER_IDS=""
PRODUCTION_REVISION="$(git -C "$REPO_ROOT" rev-parse HEAD)"

json_array_field() {
    local field="$1"
    if ! command -v python3 >/dev/null 2>&1; then
        echo "[ERROR] python3 is required to parse internal deployment responses" >&2
        return 1
    fi

    python3 -c 'import json, sys
data = json.load(sys.stdin)
value = data.get(sys.argv[1], [])
if not isinstance(value, list):
    raise SystemExit(f"JSON field {sys.argv[1]} is not an array")
print(",".join(str(item) for item in value))' "$field"
}

json_int_field() {
    local field="$1"
    if ! command -v python3 >/dev/null 2>&1; then
        echo "[ERROR] python3 is required to parse internal deployment responses" >&2
        return 1
    fi

    python3 -c 'import json, sys
data = json.load(sys.stdin)
value = data.get(sys.argv[1])
if not isinstance(value, int):
    raise SystemExit(f"JSON field {sys.argv[1]} is not an integer")
print(value)' "$field"
}

json_bool_field() {
    local field="$1"
    if ! command -v python3 >/dev/null 2>&1; then
        echo "[ERROR] python3 is required to parse internal deployment responses" >&2
        return 1
    fi

    python3 -c 'import json, sys
data = json.load(sys.stdin)
value = data.get(sys.argv[1])
if not isinstance(value, bool):
    raise SystemExit(f"JSON field {sys.argv[1]} is not a boolean")
print("true" if value else "false")' "$field"
}

LIVE_API_COLOR_DEFAULT="blue"
LIVE_STUDIO_COLOR_DEFAULT="blue"
LIVE_WEBSITE_COLOR_DEFAULT="blue"
BOOTSTRAP_UPSTREAM_HOST="127.0.0.1"

ensure_runtime_dirs() {
    mkdir -p "$NGINX_CONF_DIR" "$NGINX_UPSTREAMS_DIR" "$NGINX_APP_RUNTIME_DIR" "$STATE_DIR" "$CERTBOT_WWW_DIR" "$LETSENCRYPT_DIR" "$TRAEFIK_RUNTIME_DIR" "$TRAEFIK_DYNAMIC_DIR"
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
    : "${APPS_DOMAIN:?APPS_DOMAIN is required in .env}"
    : "${ACME_EMAIL:?ACME_EMAIL is required in .env}"
    : "${RUNTIME_PROXY_AUTH_KEY:?RUNTIME_PROXY_AUTH_KEY is required in .env}"

    CERT_NAME="${CERT_NAME:-$WEBSITE_DOMAIN}"
    # Maximum time to drain runners attached to the previous API color. The
    # normal path exits earlier when all old-color connections are gone.
    RUNNER_RECONNECT_GRACE_SECONDS="${RUNNER_RECONNECT_GRACE_SECONDS:-60}"
    CUTOVER_WARMUP_SECONDS="${CUTOVER_WARMUP_SECONDS:-10}"
    PREVIOUS_COLOR_OVERLAP_SECONDS="${PREVIOUS_COLOR_OVERLAP_SECONDS:-20}"
}

write_default_state() {
    ensure_runtime_dirs
    cat > "$STATE_FILE" <<EOF
LIVE_API_COLOR=$LIVE_API_COLOR_DEFAULT
LIVE_STUDIO_COLOR=$LIVE_STUDIO_COLOR_DEFAULT
LIVE_WEBSITE_COLOR=$LIVE_WEBSITE_COLOR_DEFAULT
API_CURRENT_REVISION=
API_PREVIOUS_REVISION=
STUDIO_CURRENT_REVISION=
STUDIO_PREVIOUS_REVISION=
WEBSITE_CURRENT_REVISION=
WEBSITE_PREVIOUS_REVISION=
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
    API_CURRENT_REVISION="${API_CURRENT_REVISION:-}"
    API_PREVIOUS_REVISION="${API_PREVIOUS_REVISION:-}"
    STUDIO_CURRENT_REVISION="${STUDIO_CURRENT_REVISION:-}"
    STUDIO_PREVIOUS_REVISION="${STUDIO_PREVIOUS_REVISION:-}"
    WEBSITE_CURRENT_REVISION="${WEBSITE_CURRENT_REVISION:-}"
    WEBSITE_PREVIOUS_REVISION="${WEBSITE_PREVIOUS_REVISION:-}"
}

save_state() {
    ensure_runtime_dirs
    cat > "$STATE_FILE" <<EOF
LIVE_API_COLOR=$LIVE_API_COLOR
LIVE_STUDIO_COLOR=$LIVE_STUDIO_COLOR
LIVE_WEBSITE_COLOR=$LIVE_WEBSITE_COLOR
API_CURRENT_REVISION=$API_CURRENT_REVISION
API_PREVIOUS_REVISION=$API_PREVIOUS_REVISION
STUDIO_CURRENT_REVISION=$STUDIO_CURRENT_REVISION
STUDIO_PREVIOUS_REVISION=$STUDIO_PREVIOUS_REVISION
WEBSITE_CURRENT_REVISION=$WEBSITE_CURRENT_REVISION
WEBSITE_PREVIOUS_REVISION=$WEBSITE_PREVIOUS_REVISION
EOF
}

service_image_name() {
    local service="$1"
    echo "automaton-prod-$service"
}

validate_revision() {
    local revision="$1"
    [[ "$revision" =~ ^[0-9a-fA-F]{7,64}$ ]] || {
        echo "[ERROR] Invalid production image revision: $revision" >&2
        return 1
    }
}

validate_recovery_artifact_reference() {
    local service="$1"
    local reference="$2"
    local expected_prefix="$(service_image_name "$service"):"
    local tag

    [[ "$reference" == "$expected_prefix"* ]] || {
        echo "[ERROR] Recovery artifact must use the $expected_prefix image repository." >&2
        return 1
    }
    tag="${reference#"$expected_prefix"}"
    [[ "$tag" =~ ^[A-Za-z0-9][A-Za-z0-9_.-]*$ && "$tag" != "latest" ]] || {
        echo "[ERROR] Recovery artifact must use a dedicated immutable tag, not latest or a mutable reference." >&2
        return 1
    }
    docker image inspect "$reference" >/dev/null 2>&1 || {
        echo "[ERROR] Recovery artifact $reference does not exist locally." >&2
        return 1
    }
}

validate_recovery_deployment_options() {
    local service="$1"
    local recovery_reference="$2"
    local explicit_revision="$3"

    if [[ -n "$recovery_reference" ]]; then
        [[ "$explicit_revision" != "true" ]] || {
            echo "[ERROR] Recovery artifact deployment cannot be combined with --revision." >&2
            return 1
        }
        validate_recovery_artifact_reference "$service" "$recovery_reference" || return 1
    fi
}

get_service_revision() {
    local service="$1"
    local kind="$2"
    load_state
    case "$service:$kind" in
        api:current) echo "$API_CURRENT_REVISION" ;;
        api:previous) echo "$API_PREVIOUS_REVISION" ;;
        studio:current) echo "$STUDIO_CURRENT_REVISION" ;;
        studio:previous) echo "$STUDIO_PREVIOUS_REVISION" ;;
        website:current) echo "$WEBSITE_CURRENT_REVISION" ;;
        website:previous) echo "$WEBSITE_PREVIOUS_REVISION" ;;
        *) echo "[ERROR] Unknown service revision: $service $kind" >&2; return 1 ;;
    esac
}

preserve_current_service_image() {
    local service="$1"
    local color="$2"
    local current_artifact_reference="${3:-}"
    local revision
    revision="$(get_service_revision "$service" current)"

    local container_id
    container_id="$(service_container_ids "$(compose_service_name "$service" "$color")" | head -n 1)"
    if [[ -z "$container_id" ]]; then
        echo "[ERROR] Cannot preserve $service-$color: no running container found." >&2
        return 1
    fi

    local image_id
    image_id="$(docker inspect --format '{{.Image}}' "$container_id")"

    if [[ -z "$revision" ]]; then
        if [[ -z "$current_artifact_reference" ]]; then
            echo "[ERROR] Cannot replace $service-$color without current immutable revision metadata." >&2
            echo "[ERROR] Supply the explicitly verified current artifact reference for this one-time provenance recovery." >&2
            return 1
        fi

        validate_recovery_artifact_reference "$service" "$current_artifact_reference" || return 1

        local artifact_image_id
        artifact_image_id="$(docker image inspect --format '{{.Id}}' "$current_artifact_reference" 2>/dev/null || true)"
        if [[ -z "$artifact_image_id" || "$artifact_image_id" != "$image_id" ]]; then
            echo "[ERROR] Current artifact reference does not match the running $service-$color image; refusing replacement." >&2
            return 1
        fi

        echo "[INFO] Verified $service-$color against explicitly supplied recovery artifact $current_artifact_reference"
        return 0
    fi

    validate_revision "$revision" || {
        echo "[ERROR] Cannot replace $service-$color with invalid current immutable revision metadata." >&2
        return 1
    }
    docker tag "$image_id" "$(service_image_name "$service"):$revision"
    echo "[INFO] Preserved $service-$color rollback image as $(service_image_name "$service"):$revision"
}

render_template() {
    local template_file="$1"
    local destination_file="$2"

    sed \
        -e "s|__API_DOMAIN__|$API_DOMAIN|g" \
        -e "s|__STUDIO_DOMAIN__|$STUDIO_DOMAIN|g" \
        -e "s|__WEBSITE_DOMAIN__|$WEBSITE_DOMAIN|g" \
        -e "s|__APPS_DOMAIN__|$APPS_DOMAIN|g" \
        -e "s|__CERT_NAME__|$CERT_NAME|g" \
        -e "s|__RUNTIME_PROXY_AUTH_KEY__|$RUNTIME_PROXY_AUTH_KEY|g" \
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
    render_template "$NGINX_TEMPLATE_DIR/conf.d/apps.conf.template" "$NGINX_CONF_DIR/apps.conf"
    render_template "$TRAEFIK_TEMPLATE_DIR/api-edge.yml" "$TRAEFIK_DYNAMIC_DIR/api-edge.yml"

    if [[ -f "$STATE_FILE" ]]; then
        render_all_upstreams
    else
        render_bootstrap_upstreams
    fi

    cp "$TRAEFIK_TEMPLATE_DIR/traefik.yml" "$TRAEFIK_RUNTIME_DIR/traefik.yml"
    sed -i "s|__ACME_EMAIL__|$ACME_EMAIL|g" "$TRAEFIK_RUNTIME_DIR/traefik.yml"
    render_traefik_api_edge "$(get_live_color api)"
}

render_traefik_api_edge() {
    local color="$1"
    local studio_color="${2:-$(get_live_color studio)}"
    local website_color="${3:-$(get_live_color website)}"
    local rendered_path="$TRAEFIK_DYNAMIC_DIR/api-edge.yml"
    local temp_path="${rendered_path}.tmp"

    render_template "$TRAEFIK_TEMPLATE_DIR/api-edge.yml" "$temp_path"
    sed -i "s|__API_COLOR__|$color|g" "$temp_path"
    sed -i "s|__STUDIO_COLOR__|$studio_color|g" "$temp_path"
    sed -i "s|__WEBSITE_COLOR__|$website_color|g" "$temp_path"
    mv -f "$temp_path" "$rendered_path"
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
        -addext "subjectAltName=DNS:$WEBSITE_DOMAIN,DNS:www.$WEBSITE_DOMAIN,DNS:$API_DOMAIN,DNS:$STUDIO_DOMAIN,DNS:$APPS_DOMAIN" \
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

    if [[ ! -f "$STATE_FILE" ]]; then
        write_default_state
    fi

    cp "$TRAEFIK_TEMPLATE_DIR/traefik.yml" "$TRAEFIK_RUNTIME_DIR/traefik.yml"
    sed -i "s|__ACME_EMAIL__|$ACME_EMAIL|g" "$TRAEFIK_RUNTIME_DIR/traefik.yml"
    render_traefik_api_edge "$(get_live_color api)" "$(get_live_color studio)" "$(get_live_color website)"
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

traefik_reload() {
    # The file provider watches api-edge.yml and the managed-runtime directory.
    # Rendering is atomic; a ping confirms the edge remains alive after reload.
    docker compose -f "$COMPOSE_FILE" exec -T traefik wget -q -O- http://127.0.0.1:8082/ping > /dev/null
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

verify_public_origin() {
    local service="$1"

    case "$service" in
        api)
            curl -fsS "https://$API_DOMAIN/healthz" > /dev/null
            ;;
        studio)
            curl -IfsS "https://$STUDIO_DOMAIN/" > /dev/null
            ;;
        website)
            curl -IfsS "https://$WEBSITE_DOMAIN/" > /dev/null
            curl -IfsS "https://www.$WEBSITE_DOMAIN/" > /dev/null
            ;;
        *)
            echo "[ERROR] Unknown service: $service" >&2
            exit 1
            ;;
    esac
}

soft_wait_for_public_origin() {
    local service="$1"
    local timeout_seconds="${2:-45}"
    local retry_interval_seconds="${3:-5}"

    if ! wait_for_public_origin "$service" "$timeout_seconds" "$retry_interval_seconds"; then
        echo "[WARN] Public $service hostname(s) did not recover through Cloudflare within ${timeout_seconds}s, but local origin verification passed." >&2
        return 1
    fi

    return 0
}

verify_app_host_origin() {
    curl -kfsS --resolve "$APPS_DOMAIN:443:127.0.0.1" "https://$APPS_DOMAIN/healthz" > /dev/null
}

wait_for_public_origin() {
    local service="$1"
    local timeout_seconds="${2:-180}"
    local retry_interval_seconds="${3:-5}"
    local started_at
    started_at="$(date +%s)"

    echo "[INFO] Waiting for public $service hostname(s) to respond"

    while true; do
        if verify_public_origin "$service"; then
            echo "[INFO] Public $service hostname(s) are responding"
            return 0
        fi

        if (( "$(date +%s)" - started_at >= timeout_seconds )); then
            echo "[ERROR] Timed out waiting for public $service hostname(s) to respond" >&2
            exit 1
        fi

        sleep "$retry_interval_seconds"
    done
}

switch_live_color() {
    local service="$1"
    local color="$2"
    local revision="${3:-}"
    local previous_revision="${4:-}"
    local revision_state="${5:-preserve}"
    local previous_color

    if [[ "$revision_state" == "unknown" ]]; then
        [[ -z "$revision" ]] || { echo "[ERROR] Unknown revision state cannot include a Git revision." >&2; exit 1; }
    elif [[ "$revision_state" != "preserve" ]]; then
        echo "[ERROR] Unknown revision state: $revision_state" >&2
        exit 1
    fi

    if [[ -n "$revision" ]]; then
        validate_revision "$revision"
    fi

    previous_color="$(get_live_color "$service")"

    render_service_upstream "$service" "$color"

    case "$service" in
        api) render_traefik_api_edge "$color" "$(get_live_color studio)" "$(get_live_color website)" ;;
        studio) render_traefik_api_edge "$(get_live_color api)" "$color" "$(get_live_color website)" ;;
        website) render_traefik_api_edge "$(get_live_color api)" "$(get_live_color studio)" "$color" ;;
    esac

    if ! traefik_reload; then
        echo "[ERROR] Traefik validation failed while switching $service to $color. Reverting selector." >&2
        render_service_upstream "$service" "$previous_color"
        case "$service" in
            api) render_traefik_api_edge "$previous_color" "$(get_live_color studio)" "$(get_live_color website)" ;;
            studio) render_traefik_api_edge "$(get_live_color api)" "$previous_color" "$(get_live_color website)" ;;
            website) render_traefik_api_edge "$(get_live_color api)" "$(get_live_color studio)" "$previous_color" ;;
        esac
        traefik_reload || true
        exit 1
    fi

    if ! verify_live_origin "$service"; then
        echo "[ERROR] New $service color failed after cutover; rolling back" >&2
        render_service_upstream "$service" "$previous_color"
        case "$service" in
            api) render_traefik_api_edge "$previous_color" "$(get_live_color studio)" "$(get_live_color website)" ;;
            studio) render_traefik_api_edge "$(get_live_color api)" "$previous_color" "$(get_live_color website)" ;;
            website) render_traefik_api_edge "$(get_live_color api)" "$(get_live_color studio)" "$previous_color" ;;
        esac
        traefik_reload || true
        verify_live_origin "$service" || true
        exit 1
    fi
    soft_wait_for_public_origin "$service" || true
    load_state
    case "$service" in
        api) LIVE_API_COLOR="$color" ;;
        studio) LIVE_STUDIO_COLOR="$color" ;;
        website) LIVE_WEBSITE_COLOR="$color" ;;
        *) echo "[ERROR] Unknown service: $service" >&2; exit 1 ;;
    esac
    if [[ "$revision_state" == "unknown" ]]; then
        case "$service" in
            api) API_CURRENT_REVISION=""; API_PREVIOUS_REVISION="$previous_revision" ;;
            studio) STUDIO_CURRENT_REVISION=""; STUDIO_PREVIOUS_REVISION="$previous_revision" ;;
            website) WEBSITE_CURRENT_REVISION=""; WEBSITE_PREVIOUS_REVISION="$previous_revision" ;;
        esac
    elif [[ -n "$revision" ]]; then
        case "$service" in
            api) API_CURRENT_REVISION="$revision"; API_PREVIOUS_REVISION="$previous_revision" ;;
            studio) STUDIO_CURRENT_REVISION="$revision"; STUDIO_PREVIOUS_REVISION="$previous_revision" ;;
            website) WEBSITE_CURRENT_REVISION="$revision"; WEBSITE_PREVIOUS_REVISION="$previous_revision" ;;
        esac
    fi
    save_state
}

rollback_live_color() {
    local service="$1"
    local color="$2"
    local revision="${3:-}"
    local previous_revision="${4:-}"
    local revision_state="${5:-preserve}"

    echo "[WARN] Rolling $service traffic back to $color"
    if ! switch_live_color "$service" "$color" "$revision" "$previous_revision" "$revision_state"; then
        echo "[ERROR] Failed to roll $service traffic back to $color" >&2
        return 1
    fi

    if ! verify_live_origin "$service"; then
        echo "[ERROR] Rollback origin verification failed for $service-$color" >&2
        return 1
    fi

    echo "[INFO] Rollback verified: $service traffic is serving $color"
}

rollback_all_live_colors() {
    local api_color="$1"
    local studio_color="$2"
    local website_color="$3"
    local api_revision="${4:-}"
    local api_previous_revision="${5:-}"
    local studio_revision="${6:-}"
    local studio_previous_revision="${7:-}"
    local website_revision="${8:-}"
    local website_previous_revision="${9:-}"
    local api_revision_state="${10:-preserve}"
    local studio_revision_state="${11:-preserve}"
    local website_revision_state="${12:-preserve}"

    echo "[WARN] Rolling all public traffic back to API=$api_color Studio=$studio_color Website=$website_color"
    if ! switch_all_live_colors "$api_color" "$studio_color" "$website_color" \
        "$api_revision" "$api_previous_revision" \
        "$studio_revision" "$studio_previous_revision" \
        "$website_revision" "$website_previous_revision" \
        "$api_revision_state" "$studio_revision_state" "$website_revision_state"; then
        echo "[ERROR] Failed to roll all public traffic back to the previous colors" >&2
        return 1
    fi

    if ! verify_live_origin api || ! verify_live_origin studio || ! verify_live_origin website; then
        echo "[ERROR] Coordinated rollback origin verification failed" >&2
        return 1
    fi

    echo "[INFO] Coordinated rollback verified"
}

switch_all_live_colors() {
    local api_color="$1"
    local studio_color="$2"
    local website_color="$3"
    local api_revision="${4:-}"
    local api_previous_revision="${5:-}"
    local studio_revision="${6:-}"
    local studio_previous_revision="${7:-}"
    local website_revision="${8:-}"
    local website_previous_revision="${9:-}"
    local api_revision_state="${10:-preserve}"
    local studio_revision_state="${11:-preserve}"
    local website_revision_state="${12:-preserve}"
    local previous_api_color
    local previous_studio_color
    local previous_website_color

    [[ -z "$api_revision" ]] || validate_revision "$api_revision"
    [[ -z "$studio_revision" ]] || validate_revision "$studio_revision"
    [[ -z "$website_revision" ]] || validate_revision "$website_revision"
    [[ "$api_revision_state" == "preserve" || "$api_revision_state" == "unknown" ]] || return 1
    [[ "$studio_revision_state" == "preserve" || "$studio_revision_state" == "unknown" ]] || return 1
    [[ "$website_revision_state" == "preserve" || "$website_revision_state" == "unknown" ]] || return 1

    previous_api_color="$(get_live_color api)"
    previous_studio_color="$(get_live_color studio)"
    previous_website_color="$(get_live_color website)"

    render_service_upstream api "$api_color"
    render_service_upstream studio "$studio_color"
    render_service_upstream website "$website_color"
    render_traefik_api_edge "$api_color" "$studio_color" "$website_color"

    if ! traefik_reload; then
        echo "[ERROR] Traefik validation failed while switching all public services. Reverting selectors." >&2
        render_service_upstream api "$previous_api_color"
        render_service_upstream studio "$previous_studio_color"
        render_service_upstream website "$previous_website_color"
        render_traefik_api_edge "$previous_api_color" "$previous_studio_color" "$previous_website_color"
        traefik_reload || true
        exit 1
    fi

    if ! verify_live_origin api || ! verify_live_origin studio || ! verify_live_origin website; then
        echo "[ERROR] One or more services failed after cutover; rolling back all colors" >&2
        render_service_upstream api "$previous_api_color"
        render_service_upstream studio "$previous_studio_color"
        render_service_upstream website "$previous_website_color"
        render_traefik_api_edge "$previous_api_color" "$previous_studio_color" "$previous_website_color"
        traefik_reload || true
        exit 1
    fi

    LIVE_API_COLOR="$api_color"
    LIVE_STUDIO_COLOR="$studio_color"
    LIVE_WEBSITE_COLOR="$website_color"
    if [[ "$api_revision_state" == "unknown" ]]; then
        API_CURRENT_REVISION=""
        API_PREVIOUS_REVISION="$api_previous_revision"
    elif [[ -n "$api_revision" ]]; then
        API_CURRENT_REVISION="$api_revision"
        API_PREVIOUS_REVISION="$api_previous_revision"
    fi
    if [[ "$studio_revision_state" == "unknown" ]]; then
        STUDIO_CURRENT_REVISION=""
        STUDIO_PREVIOUS_REVISION="$studio_previous_revision"
    elif [[ -n "$studio_revision" ]]; then
        STUDIO_CURRENT_REVISION="$studio_revision"
        STUDIO_PREVIOUS_REVISION="$studio_previous_revision"
    fi
    if [[ "$website_revision_state" == "unknown" ]]; then
        WEBSITE_CURRENT_REVISION=""
        WEBSITE_PREVIOUS_REVISION="$website_previous_revision"
    elif [[ -n "$website_revision" ]]; then
        WEBSITE_CURRENT_REVISION="$website_revision"
        WEBSITE_PREVIOUS_REVISION="$website_previous_revision"
    fi
    save_state

    soft_wait_for_public_origin api || true
    soft_wait_for_public_origin studio || true
    soft_wait_for_public_origin website || true
}

ensure_infrastructure() {
    ensure_blue_green_bootstrap
    echo "[INFO] Ensuring MySQL, RabbitMQ, worker, and traefik are up"
    docker compose -f "$COMPOSE_FILE" up -d mysql rabbitmq worker traefik
    wait_for_service_healthy mysql 120
    wait_for_service_healthy rabbitmq 120
    wait_for_service_healthy worker 120
    wait_for_service_healthy traefik 120
}

reset_blue_green_to_blue() {
    LIVE_API_COLOR="$LIVE_API_COLOR_DEFAULT"
    LIVE_STUDIO_COLOR="$LIVE_STUDIO_COLOR_DEFAULT"
    LIVE_WEBSITE_COLOR="$LIVE_WEBSITE_COLOR_DEFAULT"
    save_state
    render_all_upstreams
}

verify_target_from_traefik_network() {
    local service="$1"
    local color="$2"
    local timeout_seconds="${3:-60}"
    local started_at
    started_at="$(date +%s)"

    echo "[INFO] Verifying Traefik network can reach $service-$color before cutover"

    while true; do
        if docker compose -f "$COMPOSE_FILE" exec -T traefik sh -lc "$(traefik_target_probe_command "$service" "$color")" > /dev/null 2>&1; then
            echo "[INFO] Traefik network reachability check passed for $service-$color"
            return 0
        fi

        if (( "$(date +%s)" - started_at >= timeout_seconds )); then
            echo "[ERROR] Timed out waiting for Traefik network to reach $service-$color" >&2
            docker compose -f "$COMPOSE_FILE" logs --tail 100 "$service-$color" >&2 || true
            exit 1
        fi

        sleep 2
    done
}

traefik_target_probe_command() {
    local service="$1"
    local color="$2"

    case "$service" in
        api) echo "wget -q -O- http://api-$color:8080/healthz > /dev/null" ;;
        studio) echo "wget -q --spider http://studio-$color/" ;;
        website) echo "wget -q --spider http://website-$color/" ;;
        *)
            echo "[ERROR] Unknown service: $service" >&2
            exit 1
            ;;
    esac
}

deploy_color() {
    local service="$1"
    local color="$2"
    local revision="${3:-$PRODUCTION_REVISION}"
    local explicit_revision="${4:-false}"
    local recovery_artifact_reference="${5:-}"
    local compose_service
    compose_service="$(compose_service_name "$service" "$color")"

    local image_name
    image_name="$(service_image_name "$service")"
    if [[ -n "$recovery_artifact_reference" ]]; then
        [[ "$explicit_revision" != "true" ]] || {
            echo "[ERROR] Recovery artifact deployment cannot be combined with --revision." >&2
            return 1
        }
        validate_recovery_artifact_reference "$service" "$recovery_artifact_reference" || return 1
        local recovery_tag="${recovery_artifact_reference#"$image_name:"}"
        echo "[INFO] Reusing verified recovery artifact $recovery_artifact_reference for $compose_service"
        AUTOMATON_PRODUCTION_IMAGE_TAG="$recovery_tag" docker compose -f "$COMPOSE_FILE" up -d --no-build "$compose_service"
    else
        validate_revision "$revision"
    if ! docker image inspect "$image_name:$revision" >/dev/null 2>&1; then
        if [[ "$explicit_revision" == "true" ]]; then
            echo "[ERROR] Immutable image $image_name:$revision does not exist locally; refusing to build or change containers." >&2
            return 1
        fi
        echo "[INFO] Building $compose_service as $image_name:$revision"
        AUTOMATON_PRODUCTION_IMAGE_TAG="$revision" docker compose -f "$COMPOSE_FILE" up -d --build "$compose_service"
        docker tag "$image_name:$revision" "$image_name:latest"
    else
        echo "[INFO] Reusing immutable rollback image $image_name:$revision for $compose_service"
        AUTOMATON_PRODUCTION_IMAGE_TAG="$revision" docker compose -f "$COMPOSE_FILE" up -d --no-build "$compose_service"
        docker tag "$image_name:$revision" "$image_name:latest"
    fi
    fi

    wait_for_service_healthy "$compose_service"
    verify_target_from_traefik_network "$service" "$color"

    if (( CUTOVER_WARMUP_SECONDS > 0 )); then
        echo "[INFO] Warming up $compose_service for ${CUTOVER_WARMUP_SECONDS}s before cutover"
        sleep "$CUTOVER_WARMUP_SECONDS"
    fi
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

wait_before_stopping_previous_color() {
    local service="$1"

    if (( PREVIOUS_COLOR_OVERLAP_SECONDS > 0 )); then
        echo "[INFO] Keeping previous $service color running for ${PREVIOUS_COLOR_OVERLAP_SECONDS}s after cutover"
        sleep "$PREVIOUS_COLOR_OVERLAP_SECONDS"
    fi
}

signal_api_color_runners_reconnect_when_idle() {
    local color="$1"
    local reason="${2:-API deploy cutover complete. Reconnect to the new live API instance when idle.}"
    local allow_partial="${3:-false}"
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
        echo "[INFO] Requesting reconnect-when-idle from API container $container_id via loopback"
        local response
        if ! response="$(docker exec "$container_id" sh -lc \
            "curl -fsS -X POST -H 'Content-Type: application/json' --data '{\"reason\":\"${reason//\"/\\\"}\"}' http://127.0.0.1:8080/internal/runners/reconnect-when-idle")"; then
            echo "[ERROR] Reconnect-when-idle signal was not accepted by API container $container_id." >&2
            return 1
        fi

        echo "[INFO] Reconnect-when-idle response from API container $container_id: $response"
        local reported_runner_ids
        if ! reported_runner_ids="$(json_array_field candidateRunnerIds <<< "$response")"; then
            echo "[ERROR] Reconnect response from API container $container_id was not valid JSON" >&2
            return 1
        fi
        local expected_count=0
        local current_old_count=0
        if [[ -n "$MIGRATION_EXPECTED_RUNNER_IDS" ]]; then
            expected_count="$(printf '%s\n' "$MIGRATION_EXPECTED_RUNNER_IDS" | tr ',' '\n' | wc -l | tr -d ' ')"
        fi
        if [[ -n "$reported_runner_ids" ]]; then
            current_old_count="$(printf '%s\n' "$reported_runner_ids" | tr ',' '\n' | wc -l | tr -d ' ')"
        else
            echo "[INFO] API container $container_id returned no current Runner IDs; using the frozen snapshot for dispatch validation"
        fi
        local signaled_count
        if ! signaled_count="$(json_int_field signaledRunnerCount <<< "$response")"; then
            echo "[ERROR] Reconnect response from API container $container_id omitted signaledRunnerCount" >&2
            return 1
        fi
        if [[ "$allow_partial" == "true" ]]; then
            if [[ -n "$reported_runner_ids" ]]; then
                if [[ -z "$signaled_count" || "$signaled_count" -ne "$current_old_count" ]]; then
                    echo "[ERROR] Retry reconnect dispatch was invalid: current_old=$current_old_count signaled=${signaled_count:-unknown}." >&2
                    return 1
                fi
            elif [[ -z "$signaled_count" || "$signaled_count" -lt 0 || "$signaled_count" -gt "$expected_count" ]]; then
                echo "[ERROR] Retry reconnect dispatch was invalid: expected=$expected_count signaled=${signaled_count:-unknown}." >&2
                return 1
            fi
        elif [[ -n "$reported_runner_ids" && "$signaled_count" -ne "$current_old_count" ]]; then
            echo "[ERROR] Reconnect dispatch was incomplete: current_old=$current_old_count signaled=${signaled_count:-unknown}." >&2
            return 1
        elif [[ -z "$reported_runner_ids" && ( -z "$signaled_count" || "$signaled_count" -ne "$expected_count" ) ]]; then
            echo "[ERROR] Reconnect dispatch was incomplete: expected=$expected_count signaled=${signaled_count:-unknown}." >&2
            return 1
        fi
        if [[ -z "$reported_runner_ids" ]]; then
            echo "[INFO] No current connected Runner IDs were reported by API container $container_id"
        else
            echo "[INFO] Runners currently attached to previous API: $reported_runner_ids"
        fi
    done
}

snapshot_api_color_runner_ids() {
    local color="$1"
    local fallback_color="${2:-}"
    local compose_service
    compose_service="$(compose_service_name api "$color")"

    if ! service_exists "$compose_service"; then
        echo "[ERROR] Cannot snapshot Runner IDs: $compose_service is not running" >&2
        return 1
    fi

    local container_id
    container_id="$(service_container_ids "$compose_service" | head -n 1)"
    if [[ -z "$container_id" ]]; then
        echo "[ERROR] Cannot snapshot Runner IDs: no container found for $compose_service" >&2
        return 1
    fi

    local response
    response="$(docker exec "$container_id" sh -lc \
        "curl -fsS http://127.0.0.1:8080/internal/runners/drain-status" \
        2>/dev/null || true)"
    if [[ -z "$response" ]]; then
        echo "[ERROR] API container $container_id did not return migration candidates" >&2
        return 1
    fi

    if ! MIGRATION_EXPECTED_RUNNER_IDS="$(json_array_field connectedRunnerIds <<< "$response")"; then
        echo "[ERROR] Drain status from API container $container_id was not valid JSON" >&2
        return 1
    fi
    if [[ -z "$MIGRATION_EXPECTED_RUNNER_IDS" ]]; then
        local connected_count
        if ! connected_count="$(json_int_field connectedRunnerCount <<< "$response")"; then
            echo "[ERROR] Drain status from API container $container_id omitted connectedRunnerCount" >&2
            return 1
        fi
        if (( connected_count > 0 )) && [[ -n "$fallback_color" ]]; then
            local fallback_container_id
            fallback_container_id="$(service_container_ids "$(compose_service_name api "$fallback_color")" | head -n 1)"
            local fallback_response
            fallback_response="$(docker exec "$fallback_container_id" sh -lc \
                "curl -fsS http://127.0.0.1:8080/internal/runners/migration-candidates" \
                2>/dev/null || true)"
            if ! MIGRATION_EXPECTED_RUNNER_IDS="$(json_array_field expectedRunnerIds <<< "$fallback_response")"; then
                echo "[ERROR] Target API did not return a valid migration candidate snapshot" >&2
                return 1
            fi
            local fallback_count=0
            if [[ -n "$MIGRATION_EXPECTED_RUNNER_IDS" ]]; then
                fallback_count="$(printf '%s\n' "$MIGRATION_EXPECTED_RUNNER_IDS" | tr ',' '\n' | wc -l | tr -d ' ')"
            fi
            if [[ "$fallback_count" -ne "$connected_count" ]]; then
                echo "[ERROR] Legacy Runner migration snapshot mismatch: old_api_count=$connected_count target_candidate_count=$fallback_count" >&2
                return 1
            fi
            echo "[INFO] Legacy API omitted connected Runner IDs; using pre-cutover target-side compatibility snapshot"
        fi
    fi
    if [[ -n "$MIGRATION_EXPECTED_RUNNER_IDS" ]]; then
        echo "[INFO] Frozen pre-cutover Runner migration candidates: $MIGRATION_EXPECTED_RUNNER_IDS"
    else
        echo "[INFO] No pre-cutover Runner migration candidates"
    fi
}

wait_for_api_color_runner_drain() {
    local color="$1"
    local target_color="$2"
    local timeout_seconds="${3:-60}"
    local compose_service
    compose_service="$(compose_service_name api "$color")"
    local started_at
    started_at="$(date +%s)"
    local last_resignal_at=0

    mapfile -t container_ids < <(service_container_ids "$compose_service")
    if [[ ${#container_ids[@]} -eq 0 ]]; then
        echo "[INFO] No previous API containers remain to drain"
        return 0
    fi

    while true; do
        local all_drained=true
        local migration_complete=true
        for container_id in "${container_ids[@]}"; do
            local response
            response="$(docker exec "$container_id" sh -lc \
                "curl -fsS http://127.0.0.1:8080/internal/runners/drain-status" \
                2>/dev/null || true)"

            if [[ -z "$response" ]]; then
                echo "[WARN] Previous API color does not expose runner drain status"
                return 2
            fi

            if [[ "$response" != *'"drained":true'* ]]; then
                all_drained=false
                echo "[INFO] Previous API color still has connected runners: $response"
            fi

            if [[ -n "$MIGRATION_EXPECTED_RUNNER_IDS" ]]; then
                local migration_response
                migration_response="$(docker exec "$(service_container_ids "$(compose_service_name api "$target_color")" | head -n 1)" sh -lc \
                    "curl -fsS 'http://127.0.0.1:8080/internal/runners/migration-status?runnerIds=$MIGRATION_EXPECTED_RUNNER_IDS'" \
                    2>/dev/null || true)"
                local migration_complete_value
                if ! migration_complete_value="$(json_bool_field allConnected <<< "$migration_response")"; then
                    migration_complete_value=false
                fi
                if [[ "$migration_complete_value" != "true" ]]; then
                    all_drained=false
                    migration_complete=false
                    echo "[INFO] New API color has not confirmed all candidate Runners: ${migration_response:-unavailable}"
                fi
            fi
        done

        local now
        now="$(date +%s)"
        if [[ "$migration_complete" != "true" &&
              "$MIGRATION_EXPECTED_RUNNER_IDS" != "" &&
              $((now - last_resignal_at)) -ge 10 ]]; then
            echo "[INFO] Retrying reconnect for Runners still attached to the previous API color"
            if ! signal_api_color_runners_reconnect_when_idle "$color" \
                "Retry API deploy cutover reconnect for unresolved Runners." true; then
                return 1
            fi
            last_resignal_at="$now"
        fi

        if [[ "$all_drained" == "true" && "$migration_complete" == "true" ]]; then
            echo "[INFO] Previous API color runner drain completed"
            return 0
        fi

        if (( $(date +%s) - started_at >= timeout_seconds )); then
            echo "[WARN] Runner drain timed out after ${timeout_seconds}s; stopping previous API color" >&2
            return 1
        fi

        sleep 2
    done
}

drain_api_color_runners() {
    local color="$1"
    local target_color="$2"
    local timeout_seconds="${3:-60}"
    local drain_status

    if wait_for_api_color_runner_drain "$color" "$target_color" "$timeout_seconds"; then
        return 0
    else
        drain_status=$?
    fi

    case "$drain_status" in
        1)
            echo "[ERROR] Previous API color did not drain its connected Runners within ${timeout_seconds}s; preserving it for diagnosis." >&2
            return 1
            ;;
        2)
            echo "[ERROR] Previous API color does not expose a verifiable Runner drain status; preserving it for diagnosis." >&2
            return 1
            ;;
        *)
            echo "[ERROR] Unexpected runner drain status: $drain_status; preserving the previous API color for diagnosis." >&2
            return 1
            ;;
    esac
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
