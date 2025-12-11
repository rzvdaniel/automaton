#!/bin/bash
set -e

# Automaton Runner Docker Entrypoint
# Configures the runner from environment variables and starts in service mode

echo "================================================"
echo "  Automaton Runner - Docker Container"
echo "================================================"
echo ""

# Verify Python installation
echo "🐍 Verifying Python installation..."
python3 --version
if command -v pip3 &> /dev/null; then
    echo "✓ Python and pip are installed"
    
    # Verify requests library
    if python3 -c "import requests" 2>/dev/null; then
        echo "✓ Python 'requests' library is installed"
    else
        echo "⚠ Warning: Python 'requests' library not found"
    fi
else
    echo "⚠ Warning: pip3 not found"
fi
echo ""

# Check for required environment variables
if [ -z "$AUTOMATON_API_KEY" ]; then
    echo "❌ ERROR: AUTOMATON_API_KEY environment variable is required"
    echo ""
    echo "Usage: docker run -e AUTOMATON_API_KEY=ak_xxx -e AUTOMATON_SERVER_URL=https://... automaton-runner"
    echo ""
    exit 1
fi

if [ -z "$AUTOMATON_SERVER_URL" ]; then
    echo "❌ ERROR: AUTOMATON_SERVER_URL environment variable is required"
    echo ""
    echo "Usage: docker run -e AUTOMATON_API_KEY=ak_xxx -e AUTOMATON_SERVER_URL=https://... automaton-runner"
    echo ""
    exit 1
fi

# Set default runner name if not provided
if [ -z "$AUTOMATON_RUNNER_NAME" ]; then
    AUTOMATON_RUNNER_NAME="docker-runner-$(hostname)"
    echo "ℹ Using default runner name: $AUTOMATON_RUNNER_NAME"
fi

echo "📋 Configuration:"
echo "  • Server URL: $AUTOMATON_SERVER_URL"
echo "  • Runner Name: $AUTOMATON_RUNNER_NAME"
echo "  • API Key: ${AUTOMATON_API_KEY:0:15}..."
echo ""

# Set configuration via environment variables
export AppConfig__BaseUrl="$AUTOMATON_SERVER_URL"

# Create config directory if it doesn't exist
mkdir -p /app/.config/AutomatonRunner

# Configure authentication non-interactively
echo "🔐 Configuring authentication..."
if dotnet automaton-runner.dll auth --api-key "$AUTOMATON_API_KEY" --server "$AUTOMATON_SERVER_URL" >/dev/null 2>&1; then
    echo "✓ Authentication configured successfully"
else
    echo "⚠ Warning: Authentication configuration may have failed"
    echo "   Check that API key and server URL are correct"
fi
echo ""

# Register runner non-interactively
echo "📝 Registering runner..."
if dotnet automaton-runner.dll register --name "$AUTOMATON_RUNNER_NAME" >/dev/null 2>&1; then
    echo "✓ Runner registered successfully"
else
    echo "⚠ Warning: Runner registration may have failed (may already be registered)"
    echo "   This is normal if the runner was previously registered"
fi
echo ""

# Start runner in service mode
echo "🚀 Starting Automaton Runner in service mode..."
echo "   Press Ctrl+C to stop"
echo ""

# Execute the runner command with passed arguments (default: service)
exec dotnet automaton-runner.dll "$@"
