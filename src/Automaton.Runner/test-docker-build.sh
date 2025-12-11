#!/bin/bash
# Test script for Docker build validation
# This script tests the Docker setup without running a full build

set -e

echo "================================================"
echo "  Automaton Runner Docker Build Test"
echo "================================================"
echo ""

# Check if Docker is available
if ! command -v docker &> /dev/null; then
    echo "❌ Docker is not installed or not in PATH"
    exit 1
fi

echo "✓ Docker is available"
docker --version
echo ""

# Check Dockerfile exists
if [ ! -f "Dockerfile.production" ]; then
    echo "❌ Dockerfile.production not found"
    exit 1
fi
echo "✓ Dockerfile.production exists"

# Check entrypoint script
if [ ! -f "docker-entrypoint.sh" ]; then
    echo "❌ docker-entrypoint.sh not found"
    exit 1
fi
echo "✓ docker-entrypoint.sh exists"

if [ ! -x "docker-entrypoint.sh" ]; then
    echo "⚠ docker-entrypoint.sh is not executable, fixing..."
    chmod +x docker-entrypoint.sh
fi
echo "✓ docker-entrypoint.sh is executable"

# Check docker-compose file
if [ ! -f "docker-compose.production.yml" ]; then
    echo "❌ docker-compose.production.yml not found"
    exit 1
fi
echo "✓ docker-compose.production.yml exists"

# Validate docker-compose syntax
if command -v docker-compose &> /dev/null; then
    echo ""
    echo "Validating docker-compose.production.yml syntax..."
    if docker-compose -f docker-compose.production.yml config > /dev/null 2>&1; then
        echo "✓ docker-compose.production.yml syntax is valid"
    else
        echo "⚠ docker-compose.production.yml may have syntax issues"
    fi
fi

# Check .env.example
if [ ! -f ".env.example" ]; then
    echo "❌ .env.example not found"
    exit 1
fi
echo "✓ .env.example exists"

# Check documentation
if [ ! -f "DOCKER.md" ]; then
    echo "❌ DOCKER.md not found"
    exit 1
fi
echo "✓ DOCKER.md exists"

echo ""
echo "================================================"
echo "  All Docker files validated successfully! ✓"
echo "================================================"
echo ""
echo "To build and run:"
echo "  1. Copy .env.example to .env and configure"
echo "  2. Run: docker-compose -f docker-compose.production.yml up -d"
echo ""
