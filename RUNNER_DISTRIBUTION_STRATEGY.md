# Automaton Runner Console - Distribution Strategy

## Overview
This document outlines the recommended approach for distributing the Automaton Runner Console across multiple platforms, providing users with easy installation and deployment options.

## Distribution Architecture

### Current Setup
- **Studio Portal**: `studio.automaton.monster` (Blazor web application)
- **API Backend**: `automaton.monster` (ASP.NET Core API)
- **Target Downloads**: `studio.automaton.monster/downloads` (Static file hosting)

### Recommended Distribution Strategy

## Option 1: Static File Hosting (Recommended for MVP)

### Setup
1. **Downloads Directory**: Create `/downloads` folder in your studio web project
2. **File Structure**:
```
studio.automaton.monster/downloads/
??? runner/
?   ??? latest/
?   ?   ??? automaton-runner-win-x64.zip
?   ?   ??? automaton-runner-win-arm64.zip
?   ?   ??? automaton-runner-linux-x64.tar.gz
?   ?   ??? automaton-runner-linux-arm64.tar.gz
?   ?   ??? automaton-runner-macos-x64.tar.gz
?   ?   ??? automaton-runner-macos-arm64.tar.gz
?   ?   ??? checksums.txt
?   ?   ??? version.json
?   ??? v1.2.0/
?   ?   ??? [same structure as latest]
?   ??? install-guides/
?       ??? windows.md
?       ??? linux.md
?       ??? macos.md
```

### Azure DevOps Integration
Add this stage to your pipeline:

```yaml
- stage: Deploy
  displayName: 'Deploy to Downloads'
  dependsOn: Package
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))
  jobs:
    - deployment: DeployDownloads
      displayName: 'Deploy to Studio Downloads'
      environment: 'production'
      strategy:
        runOnce:
          deploy:
            steps:
              # Copy to studio downloads folder
              - task: AzureFileCopy@4
                displayName: 'Copy to Azure Storage'
                inputs:
                  SourcePath: '$(Pipeline.Workspace)/CreatePackages'
                  azureSubscription: 'your-azure-subscription'
                  Destination: 'AzureBlob'
                  storage: 'yourstorageaccount'
                  ContainerName: 'downloads'
                  BlobPrefix: 'runner/latest/'
```

## Option 2: GitHub Releases (Recommended for Open Source)

### Benefits
- ? Automatic versioning and release management
- ? Built-in download statistics
- ? Release notes and changelog integration
- ? Community visibility and trust

### Implementation
```yaml
- task: GitHubRelease@1
  displayName: 'Create GitHub Release'
  inputs:
    gitHubConnection: 'github-connection'
    repositoryName: 'your-org/automaton'
    action: 'create'
    target: '$(Build.SourceVersion)'
    tagSource: 'userSpecifiedTag'
    tag: 'v$(Build.BuildNumber)'
    title: 'Automaton Runner v$(Build.BuildNumber)'
    releaseNotesFilePath: 'RELEASE_NOTES.md'
    assets: |
      $(Build.ArtifactStagingDirectory)/automaton-runner-*.zip
      $(Build.ArtifactStagingDirectory)/automaton-runner-*.tar.gz
      $(Build.ArtifactStagingDirectory)/checksums.txt
    assetUploadMode: 'delete'
    isDraft: false
    isPreRelease: false
```

## Option 3: Package Managers (Long-term Recommendation)

### Windows - Chocolatey
```xml
<!-- automaton-runner.nuspec -->
<package>
  <metadata>
    <id>automaton-runner</id>
    <version>$version$</version>
    <authors>Automaton</authors>
    <description>Cross-platform workflow runner for Automaton</description>
    <projectUrl>https://automaton.monster</projectUrl>
    <tags>automation workflow runner</tags>
  </metadata>
</package>
```

### Linux - APT Repository
```bash
# Create APT repository
dpkg-deb --build automaton-runner_1.0.0_amd64
```

### macOS - Homebrew
```ruby
# Formula: automaton-runner.rb
class AutomatonRunner < Formula
  desc "Cross-platform workflow runner for Automaton"
  homepage "https://automaton.monster"
  url "https://github.com/your-org/automaton/releases/download/v1.0.0/automaton-runner-macos-x64.tar.gz"
  sha256 "checksum-here"
  
  def install
    bin.install "automaton-runner"
  end
  
  test do
    system "#{bin}/automaton-runner", "--version"
  end
end
```

## Installation Methods Comparison

| Method | Pros | Cons | Best For |
|--------|------|------|----------|
| **Direct Download** | Simple, immediate access | Manual updates | Quick testing, one-off installs |
| **Package Managers** | Auto-updates, dependency management | Setup complexity | Production deployments |
| **Container Images** | Consistent environment | Docker dependency | Server deployments |
| **Installation Scripts** | Automated setup | Platform-specific | Bulk deployments |

## Recommended User Experience

### 1. Download Page Design
Create a dedicated page at `studio.automaton.monster/downloads`:

```html
<!DOCTYPE html>
<html>
<head>
    <title>Download Automaton Runner</title>
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <link href="https://cdn.jsdelivr.net/npm/tailwindcss@2.2.19/dist/tailwind.min.css" rel="stylesheet">
</head>
<body class="bg-gray-50">
    <div class="max-w-7xl mx-auto py-12 px-4 sm:px-6 lg:px-8">
        <div class="text-center">
            <h1 class="text-4xl font-bold text-gray-900 mb-8">Download Automaton Runner</h1>
            <p class="text-xl text-gray-600 mb-12">Cross-platform console runner for your automation workflows</p>
        </div>

        <!-- Auto-detection -->
        <div id="recommended" class="bg-blue-50 rounded-lg p-6 mb-8">
            <h2 class="text-lg font-semibold mb-4">?? Recommended for your system</h2>
            <div id="detected-platform" class="flex items-center justify-between">
                <!-- JavaScript will populate this -->
            </div>
        </div>

        <!-- All platforms -->
        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
            <!-- Windows -->
            <div class="bg-white rounded-lg shadow-md p-6">
                <div class="flex items-center mb-4">
                    <svg class="w-8 h-8 text-blue-600 mr-3" fill="currentColor" viewBox="0 0 20 20">
                        <!-- Windows icon -->
                    </svg>
                    <h3 class="text-lg font-semibold">Windows</h3>
                </div>
                <div class="space-y-2">
                    <a href="/downloads/runner/latest/automaton-runner-win-x64.zip" 
                       class="block bg-blue-600 text-white px-4 py-2 rounded hover:bg-blue-700">
                        Download x64 (.zip)
                    </a>
                    <a href="/downloads/runner/latest/automaton-runner-win-arm64.zip" 
                       class="block bg-gray-600 text-white px-4 py-2 rounded hover:bg-gray-700">
                        Download ARM64 (.zip)
                    </a>
                </div>
            </div>

            <!-- Linux -->
            <div class="bg-white rounded-lg shadow-md p-6">
                <div class="flex items-center mb-4">
                    <svg class="w-8 h-8 text-orange-600 mr-3" fill="currentColor" viewBox="0 0 20 20">
                        <!-- Linux icon -->
                    </svg>
                    <h3 class="text-lg font-semibold">Linux</h3>
                </div>
                <div class="space-y-2">
                    <a href="/downloads/runner/latest/automaton-runner-linux-x64.tar.gz" 
                       class="block bg-orange-600 text-white px-4 py-2 rounded hover:bg-orange-700">
                        Download x64 (.tar.gz)
                    </a>
                    <a href="/downloads/runner/latest/automaton-runner-linux-arm64.tar.gz" 
                       class="block bg-gray-600 text-white px-4 py-2 rounded hover:bg-gray-700">
                        Download ARM64 (.tar.gz)
                    </a>
                </div>
            </div>

            <!-- macOS -->
            <div class="bg-white rounded-lg shadow-md p-6">
                <div class="flex items-center mb-4">
                    <svg class="w-8 h-8 text-gray-800 mr-3" fill="currentColor" viewBox="0 0 20 20">
                        <!-- Apple icon -->
                    </svg>
                    <h3 class="text-lg font-semibold">macOS</h3>
                </div>
                <div class="space-y-2">
                    <a href="/downloads/runner/latest/automaton-runner-macos-x64.tar.gz" 
                       class="block bg-gray-800 text-white px-4 py-2 rounded hover:bg-gray-900">
                        Download Intel (.tar.gz)
                    </a>
                    <a href="/downloads/runner/latest/automaton-runner-macos-arm64.tar.gz" 
                       class="block bg-gray-600 text-white px-4 py-2 rounded hover:bg-gray-700">
                        Download Apple Silicon (.tar.gz)
                    </a>
                </div>
            </div>
        </div>

        <!-- Quick Install Commands -->
        <div class="mt-12 bg-gray-900 rounded-lg p-6 text-white">
            <h2 class="text-lg font-semibold mb-4">? Quick Install</h2>
            <div class="grid grid-cols-1 lg:grid-cols-3 gap-4">
                <div>
                    <h4 class="font-medium mb-2">Windows (PowerShell)</h4>
                    <code class="text-sm bg-gray-800 p-2 rounded block">
                        iwr -useb https://studio.automaton.monster/install.ps1 | iex
                    </code>
                </div>
                <div>
                    <h4 class="font-medium mb-2">Linux/macOS</h4>
                    <code class="text-sm bg-gray-800 p-2 rounded block">
                        curl -sSL https://studio.automaton.monster/install.sh | bash
                    </code>
                </div>
                <div>
                    <h4 class="font-medium mb-2">Docker</h4>
                    <code class="text-sm bg-gray-800 p-2 rounded block">
                        docker run -e AUTOMATON_API_KEY=your_key automaton/runner
                    </code>
                </div>
            </div>
        </div>
    </div>

    <script>
        // Auto-detect user's platform
        function detectPlatform() {
            const platform = navigator.platform.toLowerCase();
            const userAgent = navigator.userAgent.toLowerCase();
            
            let detectedOS = 'Unknown';
            let detectedArch = 'x64';
            let downloadUrl = '';
            
            if (platform.includes('win')) {
                detectedOS = 'Windows';
                downloadUrl = '/downloads/runner/latest/automaton-runner-win-x64.zip';
            } else if (platform.includes('mac') || platform.includes('darwin')) {
                detectedOS = 'macOS';
                // Detect Apple Silicon
                if (userAgent.includes('arm') || platform.includes('arm')) {
                    detectedArch = 'Apple Silicon';
                    downloadUrl = '/downloads/runner/latest/automaton-runner-macos-arm64.tar.gz';
                } else {
                    detectedArch = 'Intel';
                    downloadUrl = '/downloads/runner/latest/automaton-runner-macos-x64.tar.gz';
                }
            } else if (platform.includes('linux')) {
                detectedOS = 'Linux';
                downloadUrl = '/downloads/runner/latest/automaton-runner-linux-x64.tar.gz';
            }
            
            if (detectedOS !== 'Unknown') {
                document.getElementById('detected-platform').innerHTML = `
                    <div>
                        <span class="text-lg font-medium">${detectedOS} ${detectedArch}</span>
                        <p class="text-gray-600">We detected your operating system</p>
                    </div>
                    <a href="${downloadUrl}" class="bg-blue-600 text-white px-6 py-2 rounded-lg hover:bg-blue-700">
                        Download Now
                    </a>
                `;
            }
        }
        
        detectPlatform();
    </script>
</body>
</html>
```

### 2. Installation Instructions

Create comprehensive installation guides for each platform:

#### Windows Installation Guide
```markdown
# Windows Installation Guide

## Option 1: Automated Installation (Recommended)
1. Open PowerShell as Administrator
2. Run: `iwr -useb https://studio.automaton.monster/install.ps1 | iex`
3. Follow the prompts to configure your runner

## Option 2: Manual Installation
1. Download the appropriate ZIP file for your architecture
2. Extract to `C:\Program Files\AutomatonRunner\`
3. Add to system PATH or use full path
4. Open Command Prompt or PowerShell
5. Run: `automaton-runner.exe auth --api-key YOUR_KEY`
6. Run: `automaton-runner.exe register --name "My-Windows-Runner"`
7. Run: `automaton-runner.exe run --name "My-Windows-Runner"`

## Running as Windows Service
Use NSSM (Non-Sucking Service Manager) to run as a service:
1. Download NSSM from https://nssm.cc/
2. Run: `nssm install "Automaton Runner"`
3. Set Application Path to `automaton-runner.exe`
4. Set Arguments to `run --name "Service-Runner"`
5. Start the service: `sc start "Automaton Runner"`
```

#### Linux Installation Guide
```markdown
# Linux Installation Guide

## Option 1: Automated Installation (Recommended)
```bash
curl -sSL https://studio.automaton.monster/install.sh | bash
```

## Option 2: Manual Installation
1. Download the appropriate tar.gz file
2. Extract: `tar -xzf automaton-runner-linux-x64.tar.gz`
3. Make executable: `chmod +x automaton-runner`
4. Copy to system: `sudo cp automaton-runner /usr/local/bin/`
5. Configure: `automaton-runner auth --api-key YOUR_KEY`
6. Register: `automaton-runner register --name "My-Linux-Runner"`
7. Run: `automaton-runner run --name "My-Linux-Runner"`

## Running as systemd Service
1. Create service file: `sudo nano /etc/systemd/system/automaton-runner.service`
2. Add configuration (see example in download)
3. Enable: `sudo systemctl enable automaton-runner`
4. Start: `sudo systemctl start automaton-runner`
5. Check status: `sudo systemctl status automaton-runner`
```

#### macOS Installation Guide
```markdown
# macOS Installation Guide

## Option 1: Automated Installation (Recommended)
```bash
curl -sSL https://studio.automaton.monster/install.sh | bash
```

## Option 2: Manual Installation
1. Download the appropriate tar.gz file (Intel or Apple Silicon)
2. Extract: `tar -xzf automaton-runner-macos-*.tar.gz`
3. Make executable: `chmod +x automaton-runner`
4. Copy to system: `sudo cp automaton-runner /usr/local/bin/`
5. Configure: `automaton-runner auth --api-key YOUR_KEY`
6. Register: `automaton-runner register --name "My-Mac-Runner"`
7. Run: `automaton-runner run --name "My-Mac-Runner"`

## Running as Launch Agent
1. Create plist file in `~/Library/LaunchAgents/`
2. Load agent: `launchctl load ~/Library/LaunchAgents/com.automaton.runner.plist`
3. Start: `launchctl start com.automaton.runner`
```

## Security Considerations

### Code Signing
- **Windows**: Sign executables with Authenticode certificate
- **macOS**: Sign with Apple Developer ID and notarize
- **Linux**: Use GPG signatures for packages

### Distribution Security
- Use HTTPS for all downloads
- Provide SHA256 checksums
- Sign release artifacts
- Implement download verification

## Monitoring and Analytics

### Download Tracking
```javascript
// Add to download page
function trackDownload(platform, architecture) {
    gtag('event', 'download', {
        'event_category': 'Runner',
        'event_label': `${platform}-${architecture}`,
        'value': 1
    });
}
```

### Update Mechanism
Implement auto-update checking:
```csharp
// In runner console
public async Task CheckForUpdates()
{
    var response = await httpClient.GetAsync("https://studio.automaton.monster/api/runner/version");
    var latestVersion = await response.Content.ReadFromJsonAsync<VersionInfo>();
    
    if (IsNewerVersion(latestVersion.Version))
    {
        Console.WriteLine($"New version available: {latestVersion.Version}");
        Console.WriteLine($"Download: {latestVersion.DownloadUrl}");
    }
}
```

## Recommended Implementation Order

1. **Phase 1**: Implement Azure DevOps pipeline and static file hosting
2. **Phase 2**: Create download page with auto-detection
3. **Phase 3**: Add installation scripts and service templates
4. **Phase 4**: Implement package managers (Chocolatey, Homebrew)
5. **Phase 5**: Add auto-update mechanism and telemetry

This approach provides immediate value while building toward a comprehensive distribution system.