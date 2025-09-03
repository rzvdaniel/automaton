@echo off
REM Test script for the corrected authentication
REM This script tests the console runner against localhost:7091 with email authentication

echo === Automaton Console Runner Authentication Test (Fixed) ===
echo.

REM Build the project
echo Building the console runner...
cd /d "%~dp0"
dotnet build -c Release

if %ERRORLEVEL% neq 0 (
    echo ? Build failed
    exit /b 1
)

echo ? Build successful
echo.

REM Test with correct server URL and email authentication
echo Testing authentication against https://localhost:7091...
echo.
echo *** IMPORTANT CHANGES ***
echo - The console runner now uses EMAIL-based authentication
echo - When prompted, enter your EMAIL ADDRESS (not username)
echo - Example: rzvdaniel@gmail.com
echo.
echo Make sure your Automaton Studio Server is running on https://localhost:7091
echo.

REM Run the login command
dotnet run -- login --server https://localhost:7091

echo.
echo === Test Complete ===
echo.
echo If login was successful, you can now:
echo 1. Register a runner: dotnet run -- register --name "Test-Console-Runner" --server https://localhost:7091
echo 2. Start the runner: dotnet run -- run --name "Test-Console-Runner"