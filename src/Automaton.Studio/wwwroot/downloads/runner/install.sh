#!/bin/bash
# Automaton Runner Universal Installation Script
# Supports Linux and macOS with automatic platform detection

set -e

# Configuration
INSTALL_PATH="/usr/local/bin"
CONFIG_DIR="$HOME/.config/AutomatonRunner"
SERVICE_NAME="automaton-runner"
GITHUB_REPO="your-org/automaton"
BASE_URL="https://studio.automaton.monster/downloads/runner/latest"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Command line arguments
API_KEY=""
SERVER_URL="https://api.automaton.monster"
RUNNER_NAME="$(hostname)-runner"
INSTALL_DIR="$INSTALL_PATH"
CREATE_SERVICE=false
FORCE_PLATFORM=""
FORCE_ARCH=""

print_header() {
    echo -e "${BLUE}"
    echo "????????????????????????????????????????????????????????????????????"
    echo "                  Automaton Runner Installer                 "
    echo "            Cross-platform workflow automation               "
    echo "????????????????????????????????????????????????????????????????????"
    echo -e "${NC}"
}

print_step() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

show_help() {
    cat << EOF
Automaton Runner Installation Script

USAGE:
    curl -sSL https://studio.automaton.monster/downloads/install.sh | bash
    # OR
    curl -sSL https://studio.automaton.monster/downloads/install.sh | bash -s -- [OPTIONS]

OPTIONS:
    --api-key KEY           API key for authentication
    --server-url URL        Server URL (default: https://api.automaton.monster)
    --runner-name NAME      Runner name (default: hostname-runner)
    --install-dir DIR       Installation directory (default: /usr/local/bin)
    --create-service        Create system service (systemd/launchd)
    --force-platform OS     Force platform detection (linux/darwin)
    --force-arch ARCH       Force architecture (x64/arm64)
    --help                  Show this help message

NOTE:
    Simplified service setup workflow:
    1. Run the runner interactively first: automaton-runner
       (It will prompt for API key, server URL, runner name, and automatically persist them)
    2. Convert to service: sudo scripts/setup-service.sh
       (No parameters needed - detects everything automatically!)

EXAMPLES:
    # Basic installation
    curl -sSL https://studio.automaton.monster/downloads/install.sh | bash

    # Install with configuration
    curl -sSL https://studio.automaton.monster/downloads/install.sh | bash -s -- \\
        --api-key "ak_runner_your_key_here" \\
        --server-url "https://automaton.company.com" \\
        --runner-name "production-server-01" \\
        --create-service

    # Install to custom directory
    curl -sSL https://studio.automaton.monster/downloads/install.sh | bash -s -- \\
        --install-dir "/opt/automaton" \\
        --create-service

REQUIREMENTS:
    - curl or wget
    - tar
    - sudo (for system-wide installation)
    - systemd (Linux) or launchd (macOS) for service creation

EOF
}

detect_platform() {
    local platform=""
    local arch=""

    # Override detection if forced
    if [[ -n "$FORCE_PLATFORM" ]]; then
        platform="$FORCE_PLATFORM"
    else
        case "$(uname -s)" in
            Linux*)     platform="linux" ;;
            Darwin*)    platform="darwin" ;;
            *)          platform="unknown" ;;
        esac
    fi

    if [[ -n "$FORCE_ARCH" ]]; then
        arch="$FORCE_ARCH"
    else
        case "$(uname -m)" in
            x86_64|amd64)   arch="x64" ;;
            aarch64|arm64)  arch="arm64" ;;
            armv7l)         arch="arm" ;;
            *)              arch="unknown" ;;
        esac
    fi

    echo "${platform}-${arch}"
}

get_download_url() {
    local platform_arch="$1"
    
    case "$platform_arch" in
        linux-x64)      echo "$BASE_URL/automaton-runner-linux-x64.tar.gz" ;;
        linux-arm64)    echo "$BASE_URL/automaton-runner-linux-arm64.tar.gz" ;;
        darwin-x64)     echo "$BASE_URL/automaton-runner-macos-x64.tar.gz" ;;
        darwin-arm64)   echo "$BASE_URL/automaton-runner-macos-arm64.tar.gz" ;;
        *)              return 1 ;;
    esac
}

check_dependencies() {
    local missing_deps=()
    
    # Check for required tools
    for dep in curl tar; do
        if ! command -v "$dep" &> /dev/null; then
            missing_deps+=("$dep")
        fi
    done
    
    if [[ ${#missing_deps[@]} -gt 0 ]]; then
        print_error "Missing required dependencies: ${missing_deps[*]}"
        echo "Please install the missing dependencies and try again."
        exit 1
    fi
}

download_and_extract() {
    local url="$1"
    local temp_dir="$(mktemp -d)"
    local archive_name="automaton-runner.tar.gz"
    
    print_step "Downloading from $url"
    
    if ! curl -sSL -o "$temp_dir/$archive_name" "$url"; then
        print_error "Failed to download from $url"
        rm -rf "$temp_dir"
        exit 1
    fi
    
    print_step "Extracting archive"
    if ! tar -xzf "$temp_dir/$archive_name" -C "$temp_dir"; then
        print_error "Failed to extract archive"
        rm -rf "$temp_dir"
        exit 1
    fi
    
    # Find the executable
    local executable="$temp_dir/automaton-runner"
    if [[ ! -f "$executable" ]]; then
        print_error "Executable not found in archive"
        rm -rf "$temp_dir"
        exit 1
    fi
    
    echo "$temp_dir"
}

install_binary() {
    local temp_dir="$1"
    local executable="$temp_dir/automaton-runner"
    
    print_step "Installing to $INSTALL_DIR"
    
    # Create installation directory if it doesn't exist
    if [[ "$INSTALL_DIR" != "/usr/local/bin" ]] && [[ ! -d "$INSTALL_DIR" ]]; then
        sudo mkdir -p "$INSTALL_DIR"
    fi
    
    # Copy executable
    sudo cp "$executable" "$INSTALL_DIR/"
    sudo chmod +x "$INSTALL_DIR/automaton-runner"
    
    # Verify installation
    if "$INSTALL_DIR/automaton-runner" --version &>/dev/null; then
        print_step "Installation successful"
    else
        print_error "Installation verification failed"
        exit 1
    fi
}

configure_runner() {
    if [[ -n "$API_KEY" ]]; then
        print_step "Configuring runner with provided API key"
        
        if ! "$INSTALL_DIR/automaton-runner" auth --api-key "$API_KEY" --server "$SERVER_URL"; then
            print_error "Failed to authenticate with API key"
            return 1
        fi
        
        if ! "$INSTALL_DIR/automaton-runner" register --name "$RUNNER_NAME" --server "$SERVER_URL"; then
            print_error "Failed to register runner"
            return 1
        fi
        
        print_step "Runner configured and registered as '$RUNNER_NAME'"
    else
        print_warning "No API key provided. You'll need to configure manually:"
        echo "  $INSTALL_DIR/automaton-runner auth --api-key YOUR_KEY --server $SERVER_URL"
        echo "  $INSTALL_DIR/automaton-runner register --name YOUR_RUNNER_NAME"
    fi
}

create_systemd_service() {
    if [[ ! -f "/usr/bin/systemctl" ]]; then
        print_warning "systemd not found, skipping service creation"
        return 1
    fi
    
    print_step "Creating systemd service"
    
    local service_file="/etc/systemd/system/$SERVICE_NAME.service"
    local env_vars=""
    
    if [[ -n "$API_KEY" ]]; then
        env_vars="Environment=AUTOMATON_API_KEY=$API_KEY"
    fi
    
    sudo tee "$service_file" > /dev/null << EOF
[Unit]
Description=Automaton Runner
After=network.target
Wants=network.target

[Service]
Type=simple
User=$(whoami)
WorkingDirectory=$HOME
$env_vars
Environment=AppConfig__BaseUrl=$SERVER_URL
ExecStart=$INSTALL_DIR/automaton-runner run --name "$RUNNER_NAME"
Restart=always
RestartSec=10
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
EOF
    
    sudo systemctl daemon-reload
    sudo systemctl enable "$SERVICE_NAME"
    
    print_step "Systemd service created and enabled"
    print_step "Start with: sudo systemctl start $SERVICE_NAME"
    print_step "Check status: sudo systemctl status $SERVICE_NAME"
    print_step "View logs: sudo journalctl -u $SERVICE_NAME -f"
}

create_launchd_service() {
    local plist_dir="$HOME/Library/LaunchAgents"
    local plist_file="$plist_dir/com.automaton.runner.plist"
    
    print_step "Creating launchd service"
    
    mkdir -p "$plist_dir"
    
    cat > "$plist_file" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.automaton.runner</string>
    <key>ProgramArguments</key>
    <array>
        <string>$INSTALL_DIR/automaton-runner</string>
        <string>run</string>
        <string>--name</string>
        <string>$RUNNER_NAME</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>$HOME/Library/Logs/automaton-runner.log</string>
    <key>StandardErrorPath</key>
    <string>$HOME/Library/Logs/automaton-runner-error.log</string>
    <key>EnvironmentVariables</key>
    <dict>
        <key>AppConfig__BaseUrl</key>
        <string>$SERVER_URL</string>
EOF

    if [[ -n "$API_KEY" ]]; then
        cat >> "$plist_file" << EOF
        <key>AUTOMATON_API_KEY</key>
        <string>$API_KEY</string>
EOF
    fi

    cat >> "$plist_file" << EOF
    </dict>
</dict>
</plist>
EOF
    
    launchctl load "$plist_file" 2>/dev/null || true
    
    print_step "Launchd service created"
    print_step "Start with: launchctl start com.automaton.runner"
    print_step "Stop with: launchctl stop com.automaton.runner"
    print_step "View logs: tail -f ~/Library/Logs/automaton-runner.log"
}

cleanup() {
    if [[ -n "$temp_dir" ]] && [[ -d "$temp_dir" ]]; then
        rm -rf "$temp_dir"
    fi
}

main() {
    # Parse command line arguments
    while [[ $# -gt 0 ]]; do
        case $1 in
            --api-key)
                API_KEY="$2"
                shift 2
                ;;
            --server-url)
                SERVER_URL="$2"
                shift 2
                ;;
            --runner-name)
                RUNNER_NAME="$2"
                shift 2
                ;;
            --install-dir)
                INSTALL_DIR="$2"
                INSTALL_PATH="$2"
                shift 2
                ;;
            --create-service)
                CREATE_SERVICE=true
                shift
                ;;
            --force-platform)
                FORCE_PLATFORM="$2"
                shift 2
                ;;
            --force-arch)
                FORCE_ARCH="$2"
                shift 2
                ;;
            --help)
                show_help
                exit 0
                ;;
            *)
                print_error "Unknown parameter: $1"
                echo "Use --help for usage information."
                exit 1
                ;;
        esac
    done

    # Set up cleanup trap
    trap cleanup EXIT

    print_header
    
    # Check system requirements
    check_dependencies
    
    # Detect platform and architecture
    local platform_arch
    platform_arch=$(detect_platform)
    print_step "Detected platform: $platform_arch"
    
    # Get download URL
    local download_url
    if ! download_url=$(get_download_url "$platform_arch"); then
        print_error "Unsupported platform: $platform_arch"
        echo "Supported platforms: linux-x64, linux-arm64, darwin-x64, darwin-arm64"
        exit 1
    fi
    
    # Download and extract
    local temp_dir
    temp_dir=$(download_and_extract "$download_url")
    
    # Install binary
    install_binary "$temp_dir"
    
    # Configure runner if API key provided
    configure_runner
    
    # Create service if requested
    if [[ "$CREATE_SERVICE" == true ]]; then
        case "$(uname -s)" in
            Linux*)
                create_systemd_service
                ;;
            Darwin*)
                create_launchd_service
                ;;
            *)
                print_warning "Service creation not supported on this platform"
                ;;
        esac
    fi
    
    print_step "Installation completed successfully!"
    echo
    echo -e "${GREEN}?? Next Steps:${NC}"
    if [[ -z "$API_KEY" ]]; then
        echo
        echo -e "${BLUE}EASY START (Recommended):${NC}"
        echo "1. Get an API key from your Automaton Studio (Settings ? API Keys)"
        echo "2. Run: $INSTALL_DIR/automaton-runner"
        echo "   ? The runner will interactively guide you through setup!"
        echo
        echo -e "${YELLOW}ADVANCED (For Scripting):${NC}"
        echo "1. Configure: $INSTALL_DIR/automaton-runner auth --api-key YOUR_KEY --server $SERVER_URL"
        echo "2. Register: $INSTALL_DIR/automaton-runner register --name YOUR_RUNNER_NAME"
        echo "3. Run: $INSTALL_DIR/automaton-runner"
    else
        echo "1. Start the runner: $INSTALL_DIR/automaton-runner"
        if [[ "$CREATE_SERVICE" == true ]]; then
            case "$(uname -s)" in
                Linux*)  echo "2. Or start as service: sudo systemctl start $SERVICE_NAME" ;;
                Darwin*) echo "2. Or start as service: launchctl start com.automaton.runner" ;;
            esac
        fi
    fi
    echo
    echo -e "${BLUE}Documentation:${NC} https://docs.automaton.monster"
    echo -e "${BLUE}Support:${NC} https://github.com/$GITHUB_REPO/issues"
}

# Run main function with all arguments
main "$@"