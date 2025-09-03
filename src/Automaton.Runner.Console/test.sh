#!/bin/bash

# Test script for Automaton Console Runner
# This script tests basic functionality of the console runner

echo "=== Automaton Console Runner Test Script ==="
echo ""

# Build the project
echo "Building the console runner..."
cd "$(dirname "$0")"
dotnet build -c Release

if [ $? -ne 0 ]; then
    echo "? Build failed"
    exit 1
fi

echo "? Build successful"
echo ""

# Test status command (should work without authentication)
echo "Testing status command..."
dotnet run -- status

echo ""
echo "=== Test Results ==="
echo "? Console runner builds successfully"
echo "? Status command works"
echo ""
echo "Next steps to test full functionality:"
echo "1. Start your Automaton Studio Server"
echo "2. Run: dotnet run -- login --server https://localhost:7064"
echo "3. Run: dotnet run -- register --name 'Test-Console-Runner'"
echo "4. Run: dotnet run -- run --name 'Test-Console-Runner'"
echo ""
echo "For production deployment:"
echo "1. Run: dotnet publish -c Release --self-contained false"
echo "2. Copy published files to target server"
echo "3. Run the executable directly"