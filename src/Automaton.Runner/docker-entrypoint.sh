#!/bin/bash
set -e

# Ensure writable data directories exist (guards against pre-existing root-owned volumes)
mkdir -p /app/data/apps

# (Re)install system packages declared by apps via system-requirements.txt.
# apt-get packages are installed to the container filesystem (ephemeral), so they must be
# reinstalled on every container recreate. The files themselves live on the persistent volume.
APPS_DIR="/app/data/apps"
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

# Start FastAPI backend on port 8001 (localhost-only)
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
