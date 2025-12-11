#!/bin/bash
# Proof of Concept: Automated Hetzner Deployment for Automaton Runner
# This script demonstrates how to programmatically create a Hetzner server
# and deploy Automaton Runner automatically

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

print_header() {
    echo -e "${BLUE}"
    echo "═══════════════════════════════════════════════════════════"
    echo "  Hetzner Auto-Deploy POC - Automaton Runner"
    echo "═══════════════════════════════════════════════════════════"
    echo -e "${NC}"
}

print_step() {
    echo -e "${GREEN}[✓]${NC} $1"
}

print_info() {
    echo -e "${YELLOW}[ℹ]${NC} $1"
}

print_error() {
    echo -e "${RED}[✗]${NC} $1"
}

# Configuration
HETZNER_API_TOKEN="${HETZNER_API_TOKEN:-}"
RUNNER_NAME="${1:-test-auto-runner}"
API_KEY="${2:-ak_runner_demo_12345}"
SERVER_URL="${3:-https://api.automaton.monster}"
SERVER_TYPE="${SERVER_TYPE:-cx11}"
LOCATION="${LOCATION:-nbg1}"

print_header

# Check for API token
if [ -z "$HETZNER_API_TOKEN" ]; then
    print_error "HETZNER_API_TOKEN environment variable not set"
    echo ""
    echo "Get your token from: https://console.hetzner.cloud/"
    echo "Then run: export HETZNER_API_TOKEN=your_token_here"
    echo ""
    echo "Usage: $0 [runner_name] [api_key] [server_url]"
    exit 1
fi

print_info "Configuration:"
echo "  Runner Name: $RUNNER_NAME"
echo "  API Key: ${API_KEY:0:20}..."
echo "  Server URL: $SERVER_URL"
echo "  Server Type: $SERVER_TYPE"
echo "  Location: $LOCATION"
echo ""

# Generate cloud-init script
print_info "Generating cloud-init script..."

CLOUD_INIT=$(cat <<'CLOUDINIT_EOF'
#cloud-config
package_update: true
package_upgrade: true

packages:
  - docker.io
  - curl

runcmd:
  # Wait for Docker to be ready
  - systemctl start docker
  - systemctl enable docker
  
  # Create deployment directory
  - mkdir -p /opt/automaton-runner
  - cd /opt/automaton-runner
  
  # Download Docker files from GitHub
  - curl -L -o Dockerfile.production https://raw.githubusercontent.com/rzvdaniel/automaton/main/src/Automaton.Runner/Dockerfile.production
  - curl -L -o docker-compose.production.yml https://raw.githubusercontent.com/rzvdaniel/automaton/main/src/Automaton.Runner/docker-compose.production.yml
  - curl -L -o docker-entrypoint.sh https://raw.githubusercontent.com/rzvdaniel/automaton/main/src/Automaton.Runner/docker-entrypoint.sh
  - chmod +x docker-entrypoint.sh
  
  # Create .env configuration
  - |
    cat > .env << 'ENV_EOF'
    AUTOMATON_API_KEY=API_KEY_PLACEHOLDER
    AUTOMATON_SERVER_URL=SERVER_URL_PLACEHOLDER
    AUTOMATON_RUNNER_NAME=RUNNER_NAME_PLACEHOLDER
    DATA_DIR=/opt/automaton-runner/data
    LOGS_DIR=/opt/automaton-runner/logs
    CONFIG_DIR=/opt/automaton-runner/config
    ENV_EOF
  
  # Create data directories
  - mkdir -p data logs config
  
  # Pull or build the image
  - cd ..
  - |
    if docker pull rzvdaniel/automaton-runner:latest 2>/dev/null; then
      echo "Using pre-built image"
    else
      echo "Building image locally"
      docker build -f Automaton.Runner/Dockerfile.production -t automaton-runner:latest Automaton.Runner
    fi
  
  # Start the container
  - cd Automaton.Runner
  - docker-compose -f docker-compose.production.yml up -d
  
  # Enable Docker to start on boot
  - systemctl enable docker
  
  # Set container to restart automatically
  - docker update --restart=unless-stopped automaton-runner || true
  
  # Log completion
  - echo "Automaton Runner deployment completed at $(date)" >> /var/log/automaton-deployment.log

write_files:
  - path: /var/log/automaton-deployment.log
    content: |
      Automaton Runner Auto-Deployment
      Started at: $(date)
    owner: root:root
    permissions: '0644'

final_message: |
  Automaton Runner is being deployed.
  Check logs: tail -f /var/log/cloud-init-output.log
  Check status: docker ps
  View runner logs: docker logs automaton-runner
CLOUDINIT_EOF
)

# Replace placeholders
CLOUD_INIT="${CLOUD_INIT//API_KEY_PLACEHOLDER/$API_KEY}"
CLOUD_INIT="${CLOUD_INIT//SERVER_URL_PLACEHOLDER/$SERVER_URL}"
CLOUD_INIT="${CLOUD_INIT//RUNNER_NAME_PLACEHOLDER/$RUNNER_NAME}"

print_step "Cloud-init script generated"

# Create server via Hetzner API
print_info "Creating Hetzner server..."
echo ""

SERVER_NAME="automaton-$RUNNER_NAME-$(date +%s)"

CREATE_RESPONSE=$(curl -s -X POST \
  https://api.hetzner.cloud/v1/servers \
  -H "Authorization: Bearer $HETZNER_API_TOKEN" \
  -H "Content-Type: application/json" \
  -d @- <<PAYLOAD
{
  "name": "$SERVER_NAME",
  "server_type": "$SERVER_TYPE",
  "image": "ubuntu-22.04",
  "location": "$LOCATION",
  "start_after_create": true,
  "user_data": $(echo "$CLOUD_INIT" | jq -Rs .)
}
PAYLOAD
)

# Check for errors
if echo "$CREATE_RESPONSE" | jq -e '.error' > /dev/null 2>&1; then
    ERROR_MSG=$(echo "$CREATE_RESPONSE" | jq -r '.error.message')
    print_error "Failed to create server: $ERROR_MSG"
    exit 1
fi

# Extract server details
SERVER_ID=$(echo "$CREATE_RESPONSE" | jq -r '.server.id')
SERVER_IP=$(echo "$CREATE_RESPONSE" | jq -r '.server.public_net.ipv4.ip')
ROOT_PASSWORD=$(echo "$CREATE_RESPONSE" | jq -r '.root_password')

print_step "Server created successfully!"
echo ""
echo "═══════════════════════════════════════════════════════════"
echo "  Server Details"
echo "═══════════════════════════════════════════════════════════"
echo "  Server ID: $SERVER_ID"
echo "  Server Name: $SERVER_NAME"
echo "  IP Address: $SERVER_IP"
echo "  Root Password: $ROOT_PASSWORD"
echo "  Server Type: $SERVER_TYPE"
echo "  Location: $LOCATION"
echo "═══════════════════════════════════════════════════════════"
echo ""

print_info "Server is being provisioned..."
print_info "This will take 2-3 minutes for cloud-init to complete"
echo ""

# Monitor server status
print_info "Monitoring server status..."
for i in {1..30}; do
    STATUS_RESPONSE=$(curl -s \
      https://api.hetzner.cloud/v1/servers/$SERVER_ID \
      -H "Authorization: Bearer $HETZNER_API_TOKEN")
    
    STATUS=$(echo "$STATUS_RESPONSE" | jq -r '.server.status')
    
    if [ "$STATUS" = "running" ]; then
        print_step "Server is running!"
        break
    fi
    
    echo -n "."
    sleep 5
done
echo ""

print_info "Waiting for cloud-init to complete (60 seconds)..."
sleep 60

echo ""
echo "═══════════════════════════════════════════════════════════"
echo "  Deployment Complete!"
echo "═══════════════════════════════════════════════════════════"
echo ""
echo "Next steps:"
echo ""
echo "1. Check cloud-init logs:"
echo "   ssh root@$SERVER_IP 'tail -f /var/log/cloud-init-output.log'"
echo ""
echo "2. Check Docker container:"
echo "   ssh root@$SERVER_IP 'docker ps'"
echo ""
echo "3. View runner logs:"
echo "   ssh root@$SERVER_IP 'docker logs automaton-runner'"
echo ""
echo "4. Check Automaton Studio:"
echo "   Go to Runners page - '$RUNNER_NAME' should appear in 1-2 minutes"
echo ""
echo "5. SSH access:"
echo "   ssh root@$SERVER_IP"
echo "   Password: $ROOT_PASSWORD"
echo ""
echo "To delete this server:"
echo "   curl -X DELETE https://api.hetzner.cloud/v1/servers/$SERVER_ID \\"
echo "        -H 'Authorization: Bearer \$HETZNER_API_TOKEN'"
echo ""
echo "Or use the cleanup script:"
echo "   ./hetzner-cleanup.sh $SERVER_ID"
echo ""
echo "═══════════════════════════════════════════════════════════"
echo ""

# Save server info
cat > /tmp/automaton-server-$SERVER_ID.info << EOF
Server ID: $SERVER_ID
Server Name: $SERVER_NAME
IP Address: $SERVER_IP
Root Password: $ROOT_PASSWORD
Runner Name: $RUNNER_NAME
API Key: $API_KEY
Server URL: $SERVER_URL
Created: $(date)
EOF

print_step "Server info saved to: /tmp/automaton-server-$SERVER_ID.info"
