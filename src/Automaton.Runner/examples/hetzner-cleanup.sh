#!/bin/bash
# Cleanup script for Hetzner servers created by auto-deploy

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

HETZNER_API_TOKEN="${HETZNER_API_TOKEN:-}"
SERVER_ID="${1:-}"

if [ -z "$HETZNER_API_TOKEN" ]; then
    echo -e "${RED}[✗]${NC} HETZNER_API_TOKEN environment variable not set"
    exit 1
fi

if [ -z "$SERVER_ID" ]; then
    echo -e "${RED}[✗]${NC} Usage: $0 <server_id>"
    exit 1
fi

echo -e "${YELLOW}[ℹ]${NC} Deleting server ID: $SERVER_ID"

RESPONSE=$(curl -s -X DELETE \
  https://api.hetzner.cloud/v1/servers/$SERVER_ID \
  -H "Authorization: Bearer $HETZNER_API_TOKEN")

if echo "$RESPONSE" | jq -e '.error' > /dev/null 2>&1; then
    ERROR_MSG=$(echo "$RESPONSE" | jq -r '.error.message')
    echo -e "${RED}[✗]${NC} Failed to delete server: $ERROR_MSG"
    exit 1
fi

echo -e "${GREEN}[✓]${NC} Server deleted successfully"

# Remove info file if it exists
if [ -f "/tmp/automaton-server-$SERVER_ID.info" ]; then
    rm "/tmp/automaton-server-$SERVER_ID.info"
    echo -e "${GREEN}[✓]${NC} Server info file removed"
fi
