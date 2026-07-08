#!/bin/bash
set -e

# Ensure writable workspace directories exist (guards against pre-existing root-owned volumes)
WORKSPACE_ROOT="${AUTOMATON_WORKSPACE_ROOT:-/automaton/workspace}"
DEFAULT_WORKSPACE_ROOT="/automaton/workspace"
DEFAULT_APPS_DIR="$DEFAULT_WORKSPACE_ROOT/apps"
if [ -n "${RUNNER_SCRIPTS_DIR:-}" ]; then
    if [ "$RUNNER_SCRIPTS_DIR" = "$DEFAULT_APPS_DIR" ] && [ "$WORKSPACE_ROOT" != "$DEFAULT_WORKSPACE_ROOT" ]; then
        APPS_DIR="$WORKSPACE_ROOT/apps"
    else
        APPS_DIR="$RUNNER_SCRIPTS_DIR"
    fi
else
    APPS_DIR="$WORKSPACE_ROOT/apps"
fi
mkdir -p "$APPS_DIR" "$WORKSPACE_ROOT/data" "$WORKSPACE_ROOT/cache" "$WORKSPACE_ROOT/runner"

# (Re)install system packages declared by apps via system-requirements.txt.
# apt-get packages are installed to the container filesystem (ephemeral), so they must be
# reinstalled on every container recreate. The files themselves live on the persistent volume.
ALL_PKGS=""
for sysreq in "$APPS_DIR"/*/backend/system-requirements.txt; do
    [ -f "$sysreq" ] || continue
    while IFS= read -r line; do
        line=$(echo "$line" | sed 's/#.*//' | tr -d '[:space:]')
        [ -n "$line" ] && ALL_PKGS="$ALL_PKGS $line"
    done < "$sysreq"
done
if [ -n "$(echo "$ALL_PKGS" | tr -d ' ')" ]; then
    echo "Reinstalling system packages:$ALL_PKGS"
    sudo apt-get update -qq && \
        DEBIAN_FRONTEND=noninteractive sudo -E apt-get install -y --no-install-recommends $ALL_PKGS || \
        echo "Warning: system package reinstall failed — apps using system-requirements.txt may not work correctly"
fi

# Start FastAPI backend on port 8001.
# In the container we bind to 0.0.0.0 so internal reverse proxies such as Traefik can reach it.
export RUNNER_FASTAPI_HOST="${RUNNER_FASTAPI_HOST:-0.0.0.0}"
cd /app/fastapi_server
echo "Starting FastAPI backend..."
python main.py &
FASTAPI_PID=$!

# Verify FastAPI started successfully (give it up to 10s)
for i in $(seq 1 10); do
    if curl -sf http://127.0.0.1:8001/health > /dev/null 2>&1; then
        echo "FastAPI backend ready"
        break
    fi
    if [ "$i" -eq 10 ]; then
        echo "ERROR: FastAPI backend did not start within 10s"
        exit 1
    fi
    sleep 1
done

# Start the .NET Runner in service mode
cd /app
dotnet automaton-runner.dll service

# .NET exited — also stop FastAPI
kill $FASTAPI_PID 2>/dev/null || true
