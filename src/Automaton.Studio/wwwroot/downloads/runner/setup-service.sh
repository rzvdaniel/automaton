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
    --api-key KEY          API key for authentication (optional - will auto-detect)
    --server-url URL       Server URL (default: https://api.automaton.monster)
    --runner-name NAME     Runner name (default: hostname-runner)
    --uninstall            Remove the service
    --help                 Show this help message

EXAMPLES:
    # Install service with default settings (auto-detects configuration)
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

IMPORTANT NOTES:
    - Linux (systemd): Service runs as specified user and can access environment variables set in service file
    - macOS (launchd): Service runs as root by default and uses plist environment variables
    - This script will auto-detect API key from your user environment and configure it for the service

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
    local server_config_path="$config_dir/server.json"
    local runner_config_path="$config_dir/runner.json"
    
    local server_url=""
    local api_key=""
    local runner_id=""
    local runner_name=""
    
    # Read server URL from server.json (new format)
    if [[ -f "$server_config_path" ]]; then
        if command -v jq &> /dev/null; then
            server_url=$(jq -r '.ServerUrl // empty' "$server_config_path" 2>/dev/null)
        else
            server_url=$(grep -o '"ServerUrl"[[:space:]]*:[[:space:]]*"[^"]*"' "$server_config_path" | sed 's/.*"ServerUrl"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/')
        fi
    fi
    
    # Read runner registration from runner.json
    if [[ -f "$runner_config_path" ]]; then
        if command -v jq &> /dev/null; then
            runner_id=$(jq -r '.RunnerId // empty' "$runner_config_path" 2>/dev/null)
            runner_name=$(jq -r '.RunnerName // empty' "$runner_config_path" 2>/dev/null)
        else
            runner_id=$(grep -o '"RunnerId"[[:space:]]*:[[:space:]]*"[^"]*"' "$runner_config_path" | sed 's/.*"RunnerId"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/')
            runner_name=$(grep -o '"RunnerName"[[:space:]]*:[[:space:]]*"[^"]*"' "$runner_config_path" | sed 's/.*"RunnerName"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/')
        fi
    fi
    
    # IMPORTANT: Check for API key in current user's environment variable
    # We need to read this NOW because the service may run as a different user/context
    if [[ -n "$AUTOMATON_API_KEY" ]]; then
        print_step "Found API key in environment variable (AUTOMATON_API_KEY)"
        api_key="$AUTOMATON_API_KEY"
    fi
    
    if [[ -n "$server_url" ]] || [[ -n "$api_key" ]] || [[ -n "$runner_name" ]]; then
        print_step "Found existing runner configuration"
        echo "${server_url}|${api_key}|${runner_id}|${runner_name}"
    fi
}

create_systemd_service() {
    print_step "Creating systemd service"
    
    local service_file="/etc/systemd/system/${SERVICE_NAME}.service"
    local effective_server_url="$SERVER_URL"
    local effective_api_key="$API_KEY"
    local effective_runner_id=""
    local effective_runner_name=""
    local service_user="${SUDO_USER:-$USER}"
    
    # Detect existing configuration if not provided
    if [[ -z "$API_KEY" ]]; then
        local detected_config=$(get_existing_config)
        
        if [[ -n "$detected_config" ]]; then
            echo ""
            echo -e "${GREEN}✅ Detected existing runner configuration!${NC}"
            
            local detected_url=$(echo "$detected_config" | cut -d'|' -f1)
            local detected_key=$(echo "$detected_config" | cut -d'|' -f2)
            local detected_id=$(echo "$detected_config" | cut -d'|' -f3)
            local detected_name=$(echo "$detected_config" | cut -d'|' -f4)
            
            if [[ -n "$detected_url" ]]; then
                echo -e "   Server URL: ${CYAN}$detected_url${NC}"
                effective_server_url="$detected_url"
            fi
            
            if [[ -n "$detected_key" ]]; then
                local key_preview="${detected_key:0:15}"
                echo -e "   API Key (from environment): ${CYAN}${key_preview}...${NC}"
                effective_api_key="$detected_key"
            fi
            
            if [[ -n "$detected_name" ]]; then
                echo -e "   Runner Name: ${CYAN}$detected_name${NC}"
                echo -e "   Runner ID: ${CYAN}$detected_id${NC}"
                effective_runner_id="$detected_id"
                effective_runner_name="$detected_name"
            fi
            
            echo ""
            read -p "Use this configuration for the service? (Y/n) " -n 1 -r
            echo
            
            if [[ $REPLY =~ ^[Nn]$ ]]; then
                echo -e "${YELLOW}Please provide configuration manually:${NC}"
                read -p "Enter API key: " effective_api_key
                read -p "Enter server URL (press Enter for $SERVER_URL): " custom_url
                if [[ -n "$custom_url" ]]; then
                    effective_server_url="$custom_url"
                fi
            else
                print_step "Using detected configuration for service"
            fi
        else
            echo ""
            echo -e "${YELLOW}⚠️  No API key found!${NC}"
            echo ""
            echo -e "${YELLOW}The service needs an API key to connect to Automaton Studio.${NC}"
            echo -e "${CYAN}💡 Tip: Run 'automaton-runner' first to configure, then run this script${NC}"
            echo ""
            read -p "Enter your API key (or press Enter to skip - service won't work without it): " effective_api_key
        fi
    fi
    
    echo ""
    print_step "Configuring systemd service..."
    
    # Get the runner's directory for WorkingDirectory
    local runner_directory=$(dirname "$RUNNER_PATH")
    
    # Build environment variables section
    local env_section="Environment=\"AppConfig__BaseUrl=${effective_server_url}\""
    print_step "✅ Server URL will be configured in service environment"
    
    if [[ -n "$effective_api_key" ]]; then
        env_section="${env_section}
Environment=\"AUTOMATON_API_KEY=${effective_api_key}\""
        print_step "✅ API key will be configured in service environment"
    else
        print_warning "⚠️  No API key configured! Service will NOT be able to connect."
        echo -e "${YELLOW}   You can add it later by editing: $service_file${NC}"
    fi
    
    # Add runner registration if exists (to avoid auto-registration)
    if [[ -n "$effective_runner_id" ]] && [[ -n "$effective_runner_name" ]]; then
        env_section="${env_section}
Environment=\"AUTOMATON_RUNNER_ID=${effective_runner_id}\"
Environment=\"AUTOMATON_RUNNER_NAME=${effective_runner_name}\""
        print_step "✅ Runner registration will be configured in service environment"
        echo -e "   Runner Name: ${CYAN}$effective_runner_name${NC}"
        echo -e "   Runner ID: ${CYAN}$effective_runner_id${NC}"
    else
        print_warning "⚠️  No runner registration found - service will auto-register with name 'Service-$(hostname)'"
    fi
    
    # Add development environment for SSL bypass (for localhost)
    if [[ "$effective_server_url" == *"localhost"* ]] || [[ "$effective_server_url" == *"127.0.0.1"* ]]; then
        env_section="${env_section}
Environment=\"DOTNET_ENVIRONMENT=Development\""
        print_step "✅ Development environment enabled (for SSL bypass on localhost)"
    fi
    
    print_step "Set working directory: $runner_directory"
    
    cat > /tmp/${SERVICE_NAME}.service << EOF
[Unit]
Description=Automaton Runner - Workflow Automation
After=network.target
Wants=network.target

[Service]
Type=simple
User=${service_user}
WorkingDirectory=${runner_directory}
${env_section}
ExecStart=${RUNNER_PATH} service
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
    echo -e "${CYAN}═══════════════════════════════════════════════════════════${NC}"
    echo ""
    echo -e "${CYAN}📋 Service Configuration Summary:${NC}"
    echo -e "${CYAN}════════════════════════════════════${NC}"
    echo -e "  • Server URL: ${effective_server_url}"
    if [[ -n "$effective_api_key" ]]; then
        local key_preview="${effective_api_key:0:15}"
        echo -e "  • API Key: ${key_preview}***REDACTED***"
    fi
    if [[ -n "$effective_runner_name" ]]; then
        echo -e "  • Runner Name: ${effective_runner_name}"
        echo -e "  • Runner ID: ${effective_runner_id}"
    fi
    echo -e "  • Service User: ${service_user}"
    echo -e "  • Working Directory: ${runner_directory}"
    if [[ "$effective_server_url" == *"localhost"* ]] || [[ "$effective_server_url" == *"127.0.0.1"* ]]; then
        echo -e "  • Environment: Development (SSL bypass enabled)"
    fi
    echo ""
    echo -e "${CYAN}Service Management Commands:${NC}"
    echo "  Start:   sudo systemctl start ${SERVICE_NAME}"
    echo "  Stop:    sudo systemctl stop ${SERVICE_NAME}"
    echo "  Status:  sudo systemctl status ${SERVICE_NAME}"
    echo "  Logs:    sudo journalctl -u ${SERVICE_NAME} -f"
    echo ""
    
    if [[ -n "$effective_api_key" ]]; then
        # Ask if user wants to start the service now
        read -p "Start the service now? (Y/n) " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Nn]$ ]]; then
            print_step "Starting service..."
            sudo systemctl start "$SERVICE_NAME"
            sleep 3
            
            if sudo systemctl is-active --quiet "$SERVICE_NAME"; then
                echo ""
                echo -e "${GREEN}✅ Service started successfully and is running${NC}"
                echo ""
                
                # Show initial log output
                echo -e "${CYAN}📄 Initial service logs (last 10 lines):${NC}"
                echo -e "${CYAN}════════════════════════════════════════${NC}"
                sudo journalctl -u "$SERVICE_NAME" -n 10 --no-pager 2>/dev/null | while IFS= read -r line; do
                    # Redact API keys in log output
                    if [[ "$line" =~ "API Key:" ]]; then
                        line=$(echo "$line" | sed 's/\(API Key:\)[[:space:]]*[^[:space:]]*/\1 ***REDACTED***/')
                    fi
                    echo "  $line"
                done
                echo ""
                echo -e "${CYAN}💡 Monitor logs in real-time with:${NC}"
                echo -e "   ${NC}sudo journalctl -u ${SERVICE_NAME} -f${NC}"
                echo ""
                echo -e "${CYAN}✅ Check the runner status in Automaton Studio to verify connection${NC}"
            else
                print_warning "Service may not have started properly"
                echo ""
                echo -e "${YELLOW}Troubleshooting:${NC}"
                echo -e "  • Check status: ${NC}sudo systemctl status ${SERVICE_NAME}${NC}"
                echo -e "  • View logs: ${NC}sudo journalctl -u ${SERVICE_NAME} -n 50${NC}"
                echo -e "  • Check errors: ${NC}sudo journalctl -u ${SERVICE_NAME} -p err${NC}"
            fi
        fi
    else {
        echo ""
        echo -e "${YELLOW}⚠️  Service created but NOT started (no API key configured)${NC}"
        echo ""
        echo -e "${CYAN}📝 To configure and start the service:${NC}"
        echo -e "   ${NC}1. Edit the service file:${NC}"
        echo -e "      ${NC}sudo systemctl edit --full ${SERVICE_NAME}${NC}"
        echo -e "   ${NC}2. Add to [Service] section:${NC}"
        echo -e "      ${NC}Environment=\"AUTOMATON_API_KEY=your_api_key\"${NC}"
        echo -e "   ${NC}3. Reload and start:${NC}"
        echo -e "      ${NC}sudo systemctl daemon-reload${NC}"
        echo -e "      ${NC}sudo systemctl start ${SERVICE_NAME}${NC}"
    fi
    
    echo ""
    echo -e "${CYAN}═══════════════════════════════════════════════════════════${NC}"
    echo ""
}

create_launchd_service() {
    print_step "Creating launchd service"
    
    local plist_dir="/Library/LaunchDaemons"
    local plist_file="${plist_dir}/com.automaton.runner.plist"
    local effective_server_url="$SERVER_URL"
    local effective_api_key="$API_KEY"
    local effective_runner_id=""
    local effective_runner_name=""
    
    # Detect existing configuration if not provided
    if [[ -z "$API_KEY" ]]; then
        local detected_config=$(get_existing_config)
        
        if [[ -n "$detected_config" ]]; then
            echo ""
            echo -e "${GREEN}✅ Detected existing runner configuration!${NC}"
            
            local detected_url=$(echo "$detected_config" | cut -d'|' -f1)
            local detected_key=$(echo "$detected_config" | cut -d'|' -f2)
            local detected_id=$(echo "$detected_config" | cut -d'|' -f3)
            local detected_name=$(echo "$detected_config" | cut -d'|' -f4)
            
            if [[ -n "$detected_url" ]]; then
                echo -e "   Server URL: ${CYAN}$detected_url${NC}"
                effective_server_url="$detected_url"
            fi
            
            if [[ -n "$detected_key" ]]; then
                local key_preview="${detected_key:0:15}"
                echo -e "   API Key (from environment): ${CYAN}${key_preview}...${NC}"
                effective_api_key="$detected_key"
            fi
            
            if [[ -n "$detected_name" ]]; then
                echo -e "   Runner Name: ${CYAN}$detected_name${NC}"
                echo -e "   Runner ID: ${CYAN}$detected_id${NC}"
                effective_runner_id="$detected_id"
                effective_runner_name="$detected_name"
            fi
            
            echo ""
            read -p "Use this configuration for the service? (Y/n) " -n 1 -r
            echo
            
            if [[ $REPLY =~ ^[Nn]$ ]]; then
                echo -e "${YELLOW}Please provide configuration manually:${NC}"
                read -p "Enter API key: " effective_api_key
                read -p "Enter server URL (press Enter for $SERVER_URL): " custom_url
                if [[ -n "$custom_url" ]]; then
                    effective_server_url="$custom_url"
                fi
            else
                print_step "Using detected configuration for service"
            fi
        else
            echo ""
            echo -e "${YELLOW}⚠️  No API key found!${NC}"
            echo ""
            echo -e "${YELLOW}The service needs an API key to connect to Automaton Studio.${NC}"
            echo -e "${CYAN}💡 Tip: Run 'automaton-runner' first to configure, then run this script${NC}"
            echo ""
            read -p "Enter your API key (or press Enter to skip - service won't work without it): " effective_api_key
        fi
    fi
    
    echo ""
    print_step "Configuring launchd service..."
    
    # Get the runner's directory for WorkingDirectory
    local runner_directory=$(dirname "$RUNNER_PATH")
    print_step "Set working directory: $runner_directory"
    
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
        <string>service</string>
    </array>
    <key>WorkingDirectory</key>
    <string>${runner_directory}</string>
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
        print_step "✅ API key will be configured in service environment"
    else
        print_warning "⚠️  No API key configured! Service will NOT be able to connect."
    fi

    # Add runner registration if exists
    if [[ -n "$effective_runner_id" ]] && [[ -n "$effective_runner_name" ]]; then
        cat >> /tmp/com.automaton.runner.plist << EOF
        <key>AUTOMATON_RUNNER_ID</key>
        <string>${effective_runner_id}</string>
        <key>AUTOMATON_RUNNER_NAME</key>
        <string>${effective_runner_name}</string>
EOF
        print_step "✅ Runner registration will be configured in service environment"
        echo -e "   Runner Name: ${CYAN}$effective_runner_name${NC}"
        echo -e "   Runner ID: ${CYAN}$effective_runner_id${NC}"
    else
        print_warning "⚠️  No runner registration found - service will auto-register with name 'Service-$(hostname)'"
    fi
    
    # Add development environment for SSL bypass (for localhost)
    if [[ "$effective_server_url" == *"localhost"* ]] || [[ "$effective_server_url" == *"127.0.0.1"* ]]; then
        cat >> /tmp/com.automaton.runner.plist << EOF
        <key>DOTNET_ENVIRONMENT</key>
        <string>Development</string>
EOF
        print_step "✅ Development environment enabled (for SSL bypass on localhost)"
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
    echo -e "${CYAN}═══════════════════════════════════════════════════════════${NC}"
    echo ""
    echo -e "${CYAN}📋 Service Configuration Summary:${NC}"
    echo -e "${CYAN}════════════════════════════════════${NC}"
    echo -e "  • Server URL: ${effective_server_url}"
    if [[ -n "$effective_api_key" ]]; then
        local key_preview="${effective_api_key:0:15}"
        echo -e "  • API Key: ${key_preview}***REDACTED***"
    fi
    if [[ -n "$effective_runner_name" ]]; then
        echo -e "  • Runner Name: ${effective_runner_name}"
        echo -e "  • Runner ID: ${effective_runner_id}"
    fi
    echo -e "  • Working Directory: ${runner_directory}"
    echo -e "  • Logs: /var/log/automaton-runner*.log"
    if [[ "$effective_server_url" == *"localhost"* ]] || [[ "$effective_server_url" == *"127.0.0.1"* ]]; then
        echo -e "  • Environment: Development (SSL bypass enabled)"
    fi
    echo ""
    echo -e "${CYAN}Service Management Commands:${NC}"
    echo "  Start:   sudo launchctl load ${plist_file}"
    echo "  Stop:    sudo launchctl unload ${plist_file}"
    echo "  Status:  sudo launchctl list | grep automaton"
    echo "  Logs:    tail -f /var/log/automaton-runner.log"
    echo ""
    
    if [[ -n "$effective_api_key" ]]; then
        # Ask if user wants to start the service now
        read -p "Start the service now? (Y/n) " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Nn]$ ]]; then
            print_step "Starting service..."
            sudo launchctl load "$plist_file"
            sleep 3
            
            if sudo launchctl list | grep -q "com.automaton.runner"; then
                echo ""
                echo -e "${GREEN}✅ Service started successfully and is running${NC}"
                echo ""
                
                # Show initial log output
                echo -e "${CYAN}📄 Initial service logs (last 10 lines):${NC}"
                echo -e "${CYAN}════════════════════════════════════════${NC}"
                if [[ -f "/var/log/automaton-runner.log" ]]; then
                    tail -n 10 /var/log/automaton-runner.log 2>/dev/null | while IFS= read -r line; do
                        # Redact API keys in log output
                        if [[ "$line" =~ "API Key:" ]]; then
                            line=$(echo "$line" | sed 's/\(API Key:\)[[:space:]]*[^[:space:]]*/\1 ***REDACTED***/')
                        fi
                        echo "  $line"
                    done
                else
                    echo "  (Log file not yet created - service is starting up)"
                fi
                echo ""
                echo -e "${CYAN}💡 Monitor logs in real-time with:${NC}"
                echo -e "   ${NC}tail -f /var/log/automaton-runner.log${NC}"
                echo ""
                echo -e "${CYAN}✅ Check the runner status in Automaton Studio to verify connection${NC}"
            else
                print_warning "Service may not have started properly"
                echo ""
                echo -e "${YELLOW}Troubleshooting:${NC}"
                echo -e "  • Check status: ${NC}sudo launchctl list | grep automaton${NC}"
                echo -e "  • View logs: ${NC}tail -f /var/log/automaton-runner.log${NC}"
                echo -e "  • Check errors: ${NC}tail -f /var/log/automaton-runner-error.log${NC}"
            fi
        fi
    else
        echo ""
        echo -e "${YELLOW}⚠️  Service created but NOT started (no API key configured)${NC}"
        echo ""
        echo -e "${CYAN}📝 To configure and start the service:${NC}"
        echo -e "   ${NC}1. Edit the plist file:${NC}"
        echo -e "      ${NC}sudo nano ${plist_file}${NC}"
        echo -e "   ${NC}2. Add to EnvironmentVariables section:${NC}"
        echo -e "      ${NC}<key>AUTOMATON_API_KEY</key>${NC}"
        echo -e "      ${NC}<string>your_api_key</string>${NC}"
        echo -e "   ${NC}3. Load the service:${NC}"
        echo -e "      ${NC}sudo launchctl load ${plist_file}${NC}"
    fi
    
    echo ""
    echo -e "${CYAN}═══════════════════════════════════════════════════════════${NC}"
    echo ""
}
