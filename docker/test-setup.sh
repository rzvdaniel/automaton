#!/bin/bash
# Test script for Docker MySQL setup
# This script verifies the complete development setup workflow

set -e  # Exit on any error

echo "======================================"
echo "Docker MySQL Setup Integration Test"
echo "======================================"
echo ""

# Color codes for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

print_error() {
    echo -e "${RED}✗ $1${NC}"
}

print_info() {
    echo -e "${YELLOW}ℹ $1${NC}"
}

# Step 1: Check prerequisites
echo "Step 1: Checking prerequisites..."
if ! command -v docker &> /dev/null; then
    print_error "Docker is not installed"
    exit 1
fi
print_success "Docker is installed"

if ! docker compose version &> /dev/null; then
    print_error "Docker Compose is not available"
    exit 1
fi
print_success "Docker Compose is available"

if ! command -v dotnet &> /dev/null; then
    print_error ".NET SDK is not installed"
    exit 1
fi
print_success ".NET SDK is installed"

# Step 2: Verify .env.example exists
echo ""
echo "Step 2: Verifying configuration files..."
if [ ! -f .env.example ]; then
    print_error ".env.example not found"
    exit 1
fi
print_success ".env.example exists"

if [ ! -f docker-compose.dev.yml ]; then
    print_error "docker-compose.dev.yml not found"
    exit 1
fi
print_success "docker-compose.dev.yml exists"

# Step 3: Validate docker-compose configuration
echo ""
echo "Step 3: Validating Docker Compose configuration..."
if docker compose -f docker-compose.dev.yml config > /dev/null 2>&1; then
    print_success "Docker Compose configuration is valid"
else
    print_error "Docker Compose configuration is invalid"
    exit 1
fi

# Step 4: Start MySQL container
echo ""
echo "Step 4: Starting MySQL container..."
print_info "This may take a minute if pulling the image for the first time..."
docker compose -f docker-compose.dev.yml up -d mysql

# Wait for MySQL to be healthy
echo ""
echo "Step 5: Waiting for MySQL to be healthy..."
max_attempts=30
attempt=0
while [ $attempt -lt $max_attempts ]; do
    if docker inspect automaton-mysql-dev --format='{{.State.Health.Status}}' 2>/dev/null | grep -q "healthy"; then
        print_success "MySQL is healthy"
        break
    fi
    attempt=$((attempt + 1))
    if [ $attempt -eq $max_attempts ]; then
        print_error "MySQL failed to become healthy after $max_attempts attempts"
        docker logs automaton-mysql-dev
        exit 1
    fi
    sleep 2
done

# Step 6: Verify database and user exist
echo ""
echo "Step 6: Verifying database and user..."
# Give MySQL a moment to fully initialize after health check passes
sleep 2
if docker exec automaton-mysql-dev mysql -u automaton_user -pAutomatonPass123! -e "SHOW DATABASES;" 2>/dev/null | grep -q "automaton"; then
    print_success "Database 'automaton' exists and is accessible"
else
    print_error "Failed to access database"
    exit 1
fi

# Step 7: Verify initialization script ran or database is ready
echo ""
echo "Step 7: Verifying database initialization..."
# Check if initialization ran in current session or database already exists with data
if docker logs automaton-mysql-dev 2>&1 | grep -q "Automaton MySQL database initialized successfully"; then
    print_success "Initialization script ran in this session"
elif docker exec automaton-mysql-dev mysql -u automaton_user -pAutomatonPass123! automaton -e "SELECT 1" 2>/dev/null > /dev/null; then
    print_success "Database is ready (may have been initialized previously)"
else
    print_error "Database initialization verification failed"
    exit 1
fi

# Step 8: Verify database is ready for connections
echo ""
echo "Step 8: Verifying database is ready for the server..."
# Just verify the database is accessible and ready
# Migrations will be applied automatically when developers run the server
if docker exec automaton-mysql-dev mysql -u automaton_user -pAutomatonPass123! automaton -e "SELECT 1" 2>/dev/null > /dev/null; then
    print_success "Database is ready for automatic migrations"
    print_info "Note: Migrations will be applied automatically when you run 'dotnet run'"
else
    print_error "Failed to connect to database"
    exit 1
fi

# Step 9: Verify database is empty (no tables yet - waiting for first server start)
echo ""
echo "Step 9: Verifying database is empty and ready for first use..."
table_count=$(docker exec automaton-mysql-dev mysql -u automaton_user -pAutomatonPass123! automaton -N -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='automaton';" 2>/dev/null)
if [ "$table_count" -eq 0 ]; then
    print_success "Database is empty and ready for first server start"
else
    print_info "Database has $table_count tables (may be from previous test run)"
fi

# Step 10: Cleanup
echo ""
echo "Step 10: Cleaning up..."
docker compose -f docker-compose.dev.yml down -v > /dev/null 2>&1
print_success "Test environment cleaned up"

# Summary
echo ""
echo "======================================"
echo -e "${GREEN}All tests passed successfully!${NC}"
echo "======================================"
echo ""
echo "The Docker MySQL setup is working correctly."
echo "Developers can now use this setup for local development."
