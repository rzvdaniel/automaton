#!/bin/bash

# Test script for the corrected authentication
# This script tests the console runner against localhost:7091 with email authentication

echo "=== Automaton Console Runner Authentication Test (Fixed) ==="
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

# Test with correct server URL and email authentication
echo "Testing authentication against https://localhost:7091..."
echo ""
echo "*** IMPORTANT CHANGES ***"
echo "- The console runner now uses EMAIL-based authentication"
echo "- When prompted, enter your EMAIL ADDRESS (not username)"
echo "- Example: rzvdaniel@gmail.com"
echo ""
echo "Make sure your Automaton Studio Server is running on https://localhost:7091"
echo ""

# Run the login command
dotnet run -- login --server https://localhost:7091

echo ""
echo "=== Test Complete ==="
echo ""
echo "If login was successful, you can now:"
echo "1. Register a runner: dotnet run -- register --name 'Test-Console-Runner' --server https://localhost:7091"
echo "2. Start the runner: dotnet run -- run --name 'Test-Console-Runner'"