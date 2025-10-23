# Automaton Studio Desktop Downloads

This directory hosts the latest version of Automaton Studio Desktop for Windows platforms.

## Platform Support

**Windows Only**: Automaton Studio Desktop is currently available for Windows platforms only. It does not support Linux or macOS.

## Available Downloads

Download the appropriate package for your Windows system:

- **Windows x64** (64-bit Intel/AMD - Most Common): `AutomatonStudioDesktop-win-x64.zip`
- **Windows ARM64** (64-bit ARM - Surface Pro X, etc.): `AutomatonStudioDesktop-win-arm64.zip`

## System Requirements

- **Operating System**: Windows 10 version 1809 or later / Windows 11
- **.NET 8 Desktop Runtime**: Included in self-contained packages (no separate installation needed)
- **Memory**: Minimum 4GB RAM
- **Disk Space**: 500MB available space

## Installation Instructions

### 1. Download
Choose the appropriate ZIP file for your system:
- Most users should download the **x64** version
- Surface Pro X and other ARM-based devices need the **ARM64** version

### 2. Extract
Extract the downloaded ZIP file to your preferred location:
- **Recommended**: `C:\Tools\AutomatonStudio`
- **Alternative**: `%USERPROFILE%\Applications\AutomatonStudio`

### 3. Run
Launch `Automaton.Studio.Desktop.exe` from the extracted folder.

## Features

- **Visual Workflow Designer**: Drag-and-drop interface for creating automation workflows
- **Local Execution Engine**: Execute workflows directly on your machine
- **Integrated Management**: Manage all workflows from a single desktop application
- **Self-Contained**: No external dependencies or installations required

## First Run

On first launch:
1. The application opens with the workflow designer interface
2. Create your first automation workflow using the visual editor
3. All workflows execute locally on your Windows machine
4. No server connection or API keys required for local operation

## Deployment Options

### Portable Installation (Default)
- No registry modifications
- No system-wide changes
- Multiple versions can coexist in different folders
- Easy to remove (just delete the folder)

### Enterprise Deployment
System administrators can deploy via:
- Group Policy Objects (GPO)
- Configuration management tools (SCCM, Intune)
- Shared network locations
- Automated installation scripts

## Troubleshooting

| Issue | Solution |
|-------|----------|
| **Application won't start** | Ensure Windows is up to date with latest patches |
| **Permission errors** | Try running from a user-writable location like `%USERPROFILE%\Applications` |
| **Missing dependencies** | All required .NET libraries are included - no separate installation needed |
| **Detailed error logs** | Check Windows Event Viewer → Application logs |

## Uninstallation

Simply delete the extracted folder - no traces are left on the system.

## Version Information

Check the `version.json` file in the download package for:
- Build version number
- Build date and commit hash
- Platform and architecture details

## Need Help?

For support and documentation:
- **Documentation**: Access the help section within the application
- **Issues**: Report bugs via the repository issue tracker
- **Updates**: Check this download page for the latest version
