#!/usr/bin/env bash
# Automaton CLI installer
# Usage:
#   curl -fsSL https://your-server/install.sh | bash
#   curl -fsSL https://your-server/install.sh | bash -s latest
#
# Requires the .NET 9 SDK or later. Install from https://dot.net if not already present.

set -e

INSTALL_DIR="${HOME}/.local/bin"
TOOL_NAME="automaton"

# Ensure .NET is available
if ! command -v dotnet &>/dev/null; then
    echo "ERROR: .NET runtime not found."
    echo "Install the .NET SDK (9 or later) from https://dot.net and re-run this script."
    exit 1
fi

echo "Installing Automaton CLI..."
dotnet tool install --global Automaton.Cli 2>/dev/null || \
    dotnet tool update --global Automaton.Cli

# Ensure ~/.dotnet/tools is on PATH
DOTNET_TOOLS="${HOME}/.dotnet/tools"
if [[ ":${PATH}:" != *":${DOTNET_TOOLS}:"* ]]; then
    echo ""
    echo "Add the following to your shell profile (~/.bashrc, ~/.zshrc, etc.):"
    echo "  export PATH=\"\$PATH:${DOTNET_TOOLS}\""
fi

echo ""
echo "Automaton CLI installed. Run 'automaton --help' to get started."
echo ""
echo "Quick start:"
echo "  automaton config --server https://your-automaton-server --key ak_execute_xxx"
echo "  automaton init --name \"My App\""
echo "  automaton deploy"
