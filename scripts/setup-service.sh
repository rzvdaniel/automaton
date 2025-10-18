#!/bin/bash
# Automaton Runner - Linux/macOS Service Setup Script
# Run this script to configure the runner as a system service

set -e

# Configuration defaults
RUNNER_PATH="/usr/local/bin/automaton-runner"
SERVICE_NAME="automaton-runner"
API_KEY=""
SERVER_URL="https://api.automaton.monster"
RUNNER_NAME="$(hostname)-runner"
UNINSTALL=false
SHOW_HELP=false

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

print_header() {
    echo -e "${BLUE}"
    echo "═══════════════════════════════════════════════════════════"
    echo "      Automaton Runner - Service Setup                     "
    echo "═══════════════════════════════════════════════════════════"
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
Automaton Runner - Service Setup Script (Linux/macOS)

USAGE:
    ./setup-service.sh [OPTIONS]

OPTIONS:
    --runner-path PATH      Path to automaton-runner (default: /usr/local/bin/automaton-runner)
    --service-name NAME     Service name (default: automaton-runner)
    --api-key KEY          API key for authentication (optional)
    --server-url URL       Server URL (default: https://api.automaton.monster)
    --runner-name NAME     Runner name (default: hostname-runner)
    --uninstall            Remove the service
    --help                 Show this help message

EXAMPLES:
    # Install service with default settings
    sudo ./setup-service.sh

    # Install with custom configuration
    sudo ./setup-service.sh --api-key "ak_runner_xxx" --server-url "https://automaton.company.com" --runner-name "prod-01"

    # Install with custom runner location
    sudo ./setup-service.sh --runner-path "/opt/automaton/automaton-runner"

    # Uninstall service
    sudo ./setup-service.sh --uninstall

REQUIREMENTS:
    - sudo privileges
    - systemd (Linux) or launchd (macOS)
    - Automaton Runner already installed

EOF
}

detect_platform() {
    case "$(uname -s)" in
        Linux*)     echo "linux" ;;
        Darwin*)    echo "darwin" ;;
        *)          echo "unknown" ;;
    esac
}

get_existing_config() {
    # Try to find existing runner configuration
    local config_dir="$HOME/.config/AutomatonRunner"
    local config_path="$config_dir/appsettings.json"
    
    local server_url=""
    
    # Read server URL from appsettings.json
    if [[ -f "$config_path" ]]; then
        if command -v jq &> /dev/null; then
            server_url=$(jq -r '.AppConfig.BaseUrl // empty' "$config_path" 2>/dev/null)
        else
            server_url=$(grep -o '"BaseUrl"[[:space:]]*:[[:space:]]*"[^"]*"' "$config_path" | sed 's/.*"BaseUrl"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/')
        fi
    fi
    
    # Check for API key in environment variable
    if [[ -n "$AUTOMATON_API_KEY" ]]; then
        print_step "Found API key in environment variable"
    fi
    
    if [[ -n "$server_url" ]]; then
        print_step "Found existing runner configuration in: $config_dir"
        echo "$server_url"
    fi
}

create_systemd_service() {
    print_step "Creating systemd service"
    
    local service_file="/etc/systemd/system/${SERVICE_NAME}.service"
    local env_vars=""
    local effective_server_url="$SERVER_URL"
    local effective_api_key="$API_KEY"
    
    # Detect existing configuration if not provided
    if [[ -z "$API_KEY" ]]; then
        local detected_url=$(get_existing_config)
        
        # Check for API key in environment variable
        local env_api_key="$AUTOMATON_API_KEY"
        
        if [[ -n "$detected_url" ]] || [[ -n "$env_api_key" ]]; then
            echo ""
            print_step "✅ Detected existing runner configuration!"
            
            if [[ -n "$detected_url" ]]; then
                echo -e "   Server URL: ${CYAN}$detected_url${NC}"
                effective_server_url="$detected_url"
            fi
            
            if [[ -n "$env_api_key" ]]; then
                local key_preview="${env_api_key:0:15}"
                echo -e "   API Key (from environment): ${CYAN}${key_preview}...${NC}"
                effective_api_key="$env_api_key"
            fi
            
            echo ""
            read -p "Use this configuration for the service? (Y/n) " -n 1 -r
            echo
            
            if [[ $REPLY =~ ^[Nn]$ ]]; then
                print_warning "Please provide configuration manually:"
                read -p "Enter API key: " effective_api_key
                read -p "Enter server URL (press Enter for $SERVER_URL): " custom_url
                if [[ -n "$custom_url" ]]; then
                    effective_server_url="$custom_url"
                fi
            else
                print_step "Using detected configuration for service"
            fi
        else
            print_warning "No API key found in environment (AUTOMATON_API_KEY)."
            echo ""
            echo -e "${CYAN}💡 Tip: Set AUTOMATON_API_KEY environment variable before running this script${NC}"
            echo ""
            read -p "Enter your API key (or press Enter to configure later): " effective_api_key
        fi
    fi
    
    if [[ -n "$effective_api_key" ]]; then
        env_vars="Environment=AUTOMATON_API_KEY=${effective_api_key}"
    else
        print_warning "No API key configured. The service will need manual configuration."
    fi
    
    cat > /tmp/${SERVICE_NAME}.service << EOF
[Unit]
Description=Automaton Runner
After=network.target
Wants=network.target

[Service]
Type=simple
User=${SUDO_USER:-$USER}
WorkingDirectory=${HOME}
${env_vars}
Environment=AppConfig__BaseUrl=${effective_server_url}
ExecStart=${RUNNER_PATH}
Restart=always
RestartSec=10
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
EOF
    
    sudo mv /tmp/${SERVICE_NAME}.service "$service_file"
    sudo systemctl daemon-reload
    sudo systemctl enable "$SERVICE_NAME"
    
    print_step "Systemd service created and enabled"
    echo ""
    echo -e "${CYAN}Service Management Commands:${NC}"
    echo "  Start:   sudo systemctl start ${SERVICE_NAME}"
    echo "  Stop:    sudo systemctl stop ${SERVICE_NAME}"
    echo "  Status:  sudo systemctl status ${SERVICE_NAME}"
    echo "  Logs:    sudo journalctl -u ${SERVICE_NAME} -f"
    echo ""
    
    # Ask if user wants to start the service now
    read -p "Start the service now? (Y/n) " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Nn]$ ]]; then
        print_step "Starting service..."
        sudo systemctl start "$SERVICE_NAME"
        sleep 2
        
        if sudo systemctl is-active --quiet "$SERVICE_NAME"; then
            print_step "Service started successfully and is running"
        else
            print_warning "Service may not have started properly"
            echo "Check status with: sudo systemctl status ${SERVICE_NAME}"
        fi
    fi
}

create_launchd_service() {
    print_step "Creating launchd service"
    
    local plist_dir="/Library/LaunchDaemons"
    local plist_file="${plist_dir}/com.automaton.runner.plist"
    local effective_server_url="$SERVER_URL"
    local effective_api_key="$API_KEY"
    
    # Detect existing configuration if not provided
    if [[ -z "$API_KEY" ]]; then
        local detected_url=$(get_existing_config)
        
        # Check for API key in environment variable
        local env_api_key="$AUTOMATON_API_KEY"
        
        if [[ -n "$detected_url" ]] || [[ -n "$env_api_key" ]]; then
            echo ""
            print_step "✅ Detected existing runner configuration!"
            
            if [[ -n "$detected_url" ]]; then
                echo -e "   Server URL: ${CYAN}$detected_url${NC}"
                effective_server_url="$detected_url"
            fi
            
            if [[ -n "$env_api_key" ]]; then
                local key_preview="${env_api_key:0:15}"
                echo -e "   API Key (from environment): ${CYAN}${key_preview}...${NC}"
                effective_api_key="$env_api_key"
            fi
            
            echo ""
            read -p "Use this configuration for the service? (Y/n) " -n 1 -r
            echo
            
            if [[ $REPLY =~ ^[Nn]$ ]]; then
                print_warning "Please provide configuration manually:"
                read -p "Enter API key: " effective_api_key
                read -p "Enter server URL (press Enter for $SERVER_URL): " custom_url
                if [[ -n "$custom_url" ]]; then
                    effective_server_url="$custom_url"
                fi
            else
                print_step "Using detected configuration for service"
            fi
        else
            print_warning "No API key found in environment (AUTOMATON_API_KEY)."
            echo ""
            echo -e "${CYAN}💡 Tip: Set AUTOMATON_API_KEY environment variable before running this script${NC}"
            echo ""
            read -p "Enter your API key (or press Enter to configure later): " effective_api_key
        fi
    fi
    
    cat > /tmp/com.automaton.runner.plist << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.automaton.runner</string>
    <key>ProgramArguments</key>
    <array>
        <string>${RUNNER_PATH}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>/var/log/automaton-runner.log</string>
    <key>StandardErrorPath</key>
    <string>/var/log/automaton-runner-error.log</string>
    <key>EnvironmentVariables</key>
    <dict>
        <key>AppConfig__BaseUrl</key>
        <string>${effective_server_url}</string>
EOF

    if [[ -n "$effective_api_key" ]]; then
        cat >> /tmp/com.automaton.runner.plist << EOF
        <key>AUTOMATON_API_KEY</key>
        <string>${effective_api_key}</string>
EOF
    fi

    cat >> /tmp/com.automaton.runner.plist << EOF
    </dict>
</dict>
</plist>
EOF
    
    sudo mv /tmp/com.automaton.runner.plist "$plist_file"
    sudo chown root:wheel "$plist_file"
    sudo chmod 644 "$plist_file"
    
    print_step "Launchd service created"
    echo ""
    echo -e "${CYAN}Service Management Commands:${NC}"
    echo "  Start:   sudo launchctl load ${plist_file}"
    echo "  Stop:    sudo launchctl unload ${plist_file}"
    echo "  Status:  sudo launchctl list | grep automaton"
    echo "  Logs:    tail -f /var/log/automaton-runner.log"
    echo ""
    
    # Ask if user wants to start the service now
    read -p "Start the service now? (Y/n) " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Nn]$ ]]; then
        print_step "Starting service..."
        sudo launchctl load "$plist_file"
        sleep 2
        
        if sudo launchctl list | grep -q "com.automaton.runner"; then
            print_step "Service started successfully and is running"
        else
            print_warning "Service may not have started properly"
            echo "Check status with: sudo launchctl list | grep automaton"
        fi
    fi
}

remove_systemd_service() {
    print_step "Removing systemd service"
    
    local service_file="/etc/systemd/system/${SERVICE_NAME}.service"
    
    if [[ ! -f "$service_file" ]]; then
        print_warning "Service file not found: $service_file"
        return 0
    fi
    
    # Stop the service
    if sudo systemctl is-active --quiet "$SERVICE_NAME"; then
        print_step "Stopping service..."
        sudo systemctl stop "$SERVICE_NAME"
    fi
    
    # Disable and remove
    sudo systemctl disable "$SERVICE_NAME" 2>/dev/null || true
    sudo rm "$service_file"
    sudo systemctl daemon-reload
    
    print_step "Systemd service removed successfully"
}

remove_launchd_service() {
    print_step "Removing launchd service"
    
    local plist_file="/Library/LaunchDaemons/com.automaton.runner.plist"
    
    if [[ ! -f "$plist_file" ]]; then
        print_warning "Service file not found: $plist_file"
        return 0
    fi
    
    # Unload the service
    sudo launchctl unload "$plist_file" 2>/dev/null || true
    sudo rm "$plist_file"
    
    print_step "Launchd service removed successfully"
}

install_service() {
    # Verify runner exists
    if [[ ! -f "$RUNNER_PATH" ]]; then
        print_error "Runner not found at: $RUNNER_PATH"
        echo "Please specify the correct path with --runner-path parameter"
        exit 1
    fi
    
    local platform
    platform=$(detect_platform)
    
    case "$platform" in
        linux)
            if [[ ! -f "/usr/bin/systemctl" ]]; then
                print_error "systemd not found. This script requires systemd for Linux systems."
                exit 1
            fi
            create_systemd_service
            ;;
        darwin)
            create_launchd_service
            ;;
        *)
            print_error "Unsupported platform: $platform"
            echo "This script supports Linux (systemd) and macOS (launchd) only"
            exit 1
            ;;
    esac
    
    print_step "Service installation completed"
}

uninstall_service() {
    local platform
    platform=$(detect_platform)
    
    case "$platform" in
        linux)
            remove_systemd_service
            ;;
        darwin)
            remove_launchd_service
            ;;
        *)
            print_error "Unsupported platform: $platform"
            exit 1
            ;;
    esac
    
    print_step "Service uninstallation completed"
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --runner-path)
            RUNNER_PATH="$2"
            shift 2
            ;;
        --service-name)
            SERVICE_NAME="$2"
            shift 2
            ;;
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
        --uninstall)
            UNINSTALL=true
            shift
            ;;
        --help)
            SHOW_HELP=true
            shift
            ;;
        *)
            print_error "Unknown parameter: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# Main logic
main() {
    if [[ "$SHOW_HELP" == true ]]; then
        show_help
        exit 0
    fi

    print_header
    
    # Check if running with sudo
    if [[ $EUID -ne 0 ]]; then
        print_error "This script must be run with sudo privileges"
        echo "Please run: sudo $0 $*"
        exit 1
    fi
    
    if [[ "$UNINSTALL" == true ]]; then
        uninstall_service
    else
        install_service
    fi
}

# Run main function
main
