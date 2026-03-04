#!/bin/bash
set -e

# Ensure writable data directories exist (guards against pre-existing root-owned volumes)
mkdir -p /app/data/apps

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
