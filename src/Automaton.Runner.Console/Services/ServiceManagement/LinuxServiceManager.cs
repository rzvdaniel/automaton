using System.Diagnostics;
using System.Text;
using static System.Console;

namespace Automaton.Runner.Console.Services.ServiceManagement;

/// <summary>
/// Linux-specific service management using systemd
/// </summary>
public class LinuxServiceManager : ServiceManagerBase
{
    public LinuxServiceManager(ConfigurationService configService) 
        : base(configService)
    {
    }

    public override async Task<bool> InstallServiceAsync(string? runnerName, bool startService)
    {
        if (!HasSudo())
        {
            WriteLine("❌ sudo privileges required for Linux service installation");
            WriteLine("💡 Use the installation script for proper setup:");
            WriteLine("   curl -sSL https://studio.automaton.monster/downloads/install.sh | bash");
            return false;
        }

        var executablePath = Environment.ProcessPath;
        runnerName ??= $"{Environment.MachineName}-runner";

        var serviceContent = GenerateSystemdServiceContent(executablePath, runnerName);
        var serviceFilePath = $"/etc/systemd/system/{ServiceName}.service";

        WriteLine($"🔧 Installing systemd service '{ServiceName}'...");

        // Write service file
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, serviceContent);

        var copyResult = await RunProcessAsync("sudo", $"cp {tempFile} {serviceFilePath}");
        File.Delete(tempFile);

        if (copyResult != 0)
        {
            WriteLine("❌ Failed to create service file");
            return false;
        }

        // Reload systemd and enable service
        await RunProcessAsync("sudo", "systemctl daemon-reload");
        await RunProcessAsync("sudo", $"systemctl enable {ServiceName}");

        WriteLine($"✅ Systemd service '{ServiceName}' installed and enabled");

        if (startService)
        {
            WriteLine("🚀 Starting service...");
            var startResult = await RunProcessAsync("sudo", $"systemctl start {ServiceName}");
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
        WriteLine($"   ▶ Start:   sudo systemctl start {ServiceName}");
        WriteLine($"   ⏸ Stop:    sudo systemctl stop {ServiceName}");
        WriteLine($"   📊 Status:  systemctl status {ServiceName}");
        WriteLine($"   📋 Logs:    journalctl -u {ServiceName} -f");
        WriteLine($"   🗑 Remove:  automaton-runner service uninstall");

        return true;
    }

    public override async Task<bool> UninstallServiceAsync()
    {
        if (!HasSudo())
        {
            WriteLine("❌ sudo privileges required for Linux service uninstallation");
            return false;
        }

        WriteLine($"⏸ Stopping and disabling service '{ServiceName}'...");
        
        await RunProcessAsync("sudo", $"systemctl stop {ServiceName}");
        await RunProcessAsync("sudo", $"systemctl disable {ServiceName}");

        var serviceFilePath = $"/etc/systemd/system/{ServiceName}.service";
        var removeResult = await RunProcessAsync("sudo", $"rm -f {serviceFilePath}");
        
        await RunProcessAsync("sudo", "systemctl daemon-reload");

        if (removeResult == 0)
        {
            WriteLine("✅ Linux service removed successfully");
            return true;
        }
        else
        {
            WriteLine("❌ Failed to remove Linux service");
            return false;
        }
    }

    public override async Task ShowServiceStatusAsync()
    {
        WriteLine("🐧 Platform: Linux");
        WriteLine($"📦 Service Name: {ServiceName}");
        WriteLine();

        var result = await RunProcessWithOutputAsync("systemctl", $"status {ServiceName}", true);
        
        if (result.exitCode == 0 || result.exitCode == 3) // 3 = inactive but exists
        {
            WriteLine("📊 Service Status:");
            WriteLine(result.output);
        }
        else
        {
            WriteLine("❌ Service not installed");
        }

        WriteLine();
        WriteLine("📝 Available Commands:");
        WriteLine("   📦 Install:  automaton-runner service install");
        WriteLine($"   ▶ Start:    sudo systemctl start {ServiceName}");
        WriteLine($"   ⏸ Stop:     sudo systemctl stop {ServiceName}");
        WriteLine($"   📋 Logs:     journalctl -u {ServiceName} -f");
        WriteLine("   🗑 Remove:   automaton-runner service uninstall");
    }

    private string GenerateSystemdServiceContent(string executablePath, string runnerName)
    {
        var baseUrl = GetBaseUrl();
        var apiKey = GetApiKey();
        
        var envLines = new StringBuilder();
        envLines.AppendLine($"Environment=AppConfig__BaseUrl={baseUrl}");
        if (!string.IsNullOrEmpty(apiKey))
        {
            envLines.AppendLine($"Environment=AUTOMATON_API_KEY={apiKey}");
        }

        return $@"[Unit]
Description={ServiceDescription}
After=network.target
Wants=network.target

[Service]
Type=simple
User={Environment.UserName}
WorkingDirectory={Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}
{envLines}ExecStart={executablePath}
Restart=always
RestartSec=10
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
";
    }

    private static bool HasSudo()
    {
        try
        {
            var result = Process.Start(new ProcessStartInfo
            {
                FileName = "sudo",
                Arguments = "-n true",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            result?.WaitForExit();
            return result?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
