# Automaton Console Runner Downloads

This directory hosts the latest version of the Automaton Console Runner for all supported platforms.

## Quick Installation

### Windows (PowerShell as Administrator)
```powershell
iwr -useb https://your-server.com/downloads/install.ps1 | iex
```

### Linux / macOS (Terminal)
```bash
curl -sSL https://your-server.com/downloads/install.sh | bash
```

## Manual Downloads

Download the appropriate package for your platform:

- **Windows x64**: `automaton-runner-win-x64.zip`
- **Windows ARM64**: `automaton-runner-win-arm64.zip`
- **Linux x64**: `automaton-runner-linux-x64.tar.gz`
- **Linux ARM64**: `automaton-runner-linux-arm64.tar.gz`
- **macOS Intel**: `automaton-runner-macos-x64.tar.gz`
- **macOS Apple Silicon**: `automaton-runner-macos-arm64.tar.gz`

## Setup Instructions

After installation, simply run:
```bash
automaton-runner
```

The runner will interactively guide you through:
1. Server URL configuration (if needed)
2. API key authentication (if not set)
3. Runner registration (with smart defaults)
4. Automatic startup

## Prerequisites

- **.NET 8.0 Runtime** (automatically installed with installation scripts)
- **API Key** from Automaton Studio (Settings → API Keys)

## Complete Documentation

For comprehensive setup instructions, troubleshooting, Docker deployment, CI/CD integration, and more:

**👉 [View the Complete Runner Setup Guide](../../docs/#/user-guide/runner-setup)**

## Need Help?

- **Setup Guide**: Complete documentation with examples
- **Troubleshooting**: Common issues and solutions
- **API Keys**: How to create and manage authentication keys
- **Service Mode**: Running as a system service

Visit the documentation for detailed guidance!