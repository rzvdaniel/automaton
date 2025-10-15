using static System.Console;

namespace Automaton.Runner.Console.Services.ServiceManagement;

/// <summary>
/// macOS-specific service management using launchd
/// </summary>
public class MacOSServiceManager : ServiceManagerBase
{
    public MacOSServiceManager(ConfigurationService configService) 
        : base(configService)
    {
    }

    public override async Task<bool> InstallServiceAsync(string? runnerName, bool startService)
    {
        var executablePath = Environment.ProcessPath;
        runnerName ??= $"{Environment.MachineName}-runner";

        var plistContent = GenerateLaunchdPlistContent(executablePath, runnerName);
        var plistFilePath = $"/Library/LaunchDaemons/com.automaton.runner.plist";

        WriteLine($"🔧 Installing LaunchDaemon '{ServiceName}'...");

        try
        {
            // Write plist file (requires sudo)
            await File.WriteAllTextAsync(plistFilePath, plistContent);
        }
        catch (UnauthorizedAccessException)
        {
            WriteLine("❌ sudo privileges required for macOS service installation");
            WriteLine("💡 Use the installation script for proper setup:");
            WriteLine("   curl -sSL https://studio.automaton.monster/downloads/install.sh | bash");
            return false;
        }

        // Load plist as daemon
        var loadResult = await RunProcessAsync("launchctl", $"load {plistFilePath}");
        if (loadResult != 0)
        {
            WriteLine("❌ Failed to install LaunchDaemon");
            return false;
        }

        WriteLine($"✅ LaunchDaemon '{ServiceName}' installed successfully");

        if (startService)
        {
            WriteLine("🚀 Starting service...");
            var startResult = await RunProcessAsync("launchctl", "start com.automaton.runner");
            if (startResult == 0)
            {
                WriteLine("✅ Service started successfully");
            }
            else
            {
                WriteLine("⚠️ Service installed but failed to start");
            }
        }

        WriteLine();
        WriteLine("📝 Service Management Commands:");
        WriteLine($"   ▶ Start:   launchctl start com.automaton.runner");
        WriteLine($"   ⏸ Stop:    launchctl stop com.automaton.runner");
        WriteLine($"   📊 Status:  automaton-runner service status");
        WriteLine($"   🗑 Remove:  automaton-runner service uninstall");

        return true;
    }

    public override async Task<bool> UninstallServiceAsync()
    {
        WriteLine($"⏸ Stopping service '{ServiceName}'...");
        var stopResult = await RunProcessAsync("launchctl", $"stop com.automaton.runner");

        WriteLine($"🗑️ Removing LaunchDaemon '{ServiceName}'...");
        var unloadResult = await RunProcessAsync("launchctl", $"unload /Library/LaunchDaemons/com.automaton.runner.plist");

        if (stopResult != 0 && unloadResult != 0)
        {
            WriteLine("❌ Failed to uninstall macOS service");
            return false;
        }

        WriteLine("✅ macOS service uninstalled successfully");
        return true;
    }

    public override async Task ShowServiceStatusAsync()
    {
        WriteLine("🍎 Platform: macOS");
        WriteLine($"📦 Service Name: {ServiceName}");
        WriteLine();

        var result = await RunProcessWithOutputAsync("launchctl", "list com.automaton.runner", true);
        
        if (result.exitCode == 0)
        {
            WriteLine("📊 Service Status:");
            WriteLine(result.output);
        }
        else
        {
            WriteLine("❌ Service not installed or not accessible");
        }

        WriteLine();
        WriteLine("📝 Available Commands:");
        WriteLine("   📦 Install:  automaton-runner service install");
        WriteLine($"   ▶ Start:    launchctl start com.automaton.runner");
        WriteLine($"   ⏸ Stop:     launchctl stop com.automaton.runner");
        WriteLine("   🗑 Remove:   automaton-runner service uninstall");
    }

    private string GenerateLaunchdPlistContent(string executablePath, string runnerName)
    {
        var baseUrl = GetBaseUrl();
        var apiKey = GetApiKey();

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>com.automaton.runner</string>
    <key>ProgramArguments</key>
    <array>
        <string>{executablePath}</string>
    </array>
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
        <string>{baseUrl}</string>
        <key>AUTOMATON_API_KEY</key>
        <string>{apiKey ?? ""}</string>
    </dict>
</dict>
</plist>
";
    }
}
