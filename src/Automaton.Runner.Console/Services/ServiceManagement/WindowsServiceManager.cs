using static System.Console;

namespace Automaton.Runner.Console.Services.ServiceManagement;

/// <summary>
/// Windows-specific service management using NSSM (Non-Sucking Service Manager)
/// </summary>
public class WindowsServiceManager : ServiceManagerBase
{
    public WindowsServiceManager(ConfigurationService configService) 
        : base(configService)
    {
    }

    public override async Task<bool> InstallServiceAsync(string? runnerName, bool startService)
    {
        if (!IsAdministrator())
        {
            WriteLine("❌ Administrator privileges required for Windows service installation");
            WriteLine("💡 Please run as Administrator or use the installation script instead:");
            WriteLine("   iwr -useb https://studio.automaton.monster/downloads/install.ps1 | iex");
            return false;
        }

        var executablePath = Environment.ProcessPath;
        var nssmPath = await EnsureNSSMAsync();
        
        if (string.IsNullOrEmpty(nssmPath))
        {
            WriteLine("❌ NSSM (Non-Sucking Service Manager) is required for Windows service creation");
            WriteLine("💡 Use the installation script for automatic NSSM setup:");
            WriteLine("   iwr -useb https://studio.automaton.monster/downloads/install.ps1 | iex");
            return false;
        }

        // Stop and remove existing service if it exists
        await StopAndRemoveWindowsServiceAsync(nssmPath);

        runnerName ??= $"{Environment.MachineName}-Runner";

        WriteLine($"🔧 Installing Windows service '{ServiceName}'...");

        // Install service with NSSM
        var installResult = await RunProcessAsync(nssmPath, $"install {ServiceName} \"{executablePath}\"");
        if (installResult != 0)
        {
            WriteLine("❌ Failed to install Windows service");
            return false;
        }

        // Configure service
        await RunProcessAsync(nssmPath, $"set {ServiceName} Description \"{ServiceDescription}\"");
        await RunProcessAsync(nssmPath, $"set {ServiceName} Start SERVICE_AUTO_START");
        await RunProcessAsync(nssmPath, $"set {ServiceName} AppStdout \"logs\\service-output.log\"");
        await RunProcessAsync(nssmPath, $"set {ServiceName} AppStderr \"logs\\service-error.log\"");
        await RunProcessAsync(nssmPath, $"set {ServiceName} AppRotateFiles 1");
        await RunProcessAsync(nssmPath, $"set {ServiceName} AppRotateOnline 1");
        await RunProcessAsync(nssmPath, $"set {ServiceName} AppRotateBytes 10485760"); // 10MB

        // Set environment variables
        var baseUrl = GetBaseUrl();
        var envVars = $"AppConfig__BaseUrl={baseUrl}";
        
        var apiKey = GetApiKey();
        if (!string.IsNullOrEmpty(apiKey))
        {
            envVars += $" AUTOMATON_API_KEY={apiKey}";
        }

        await RunProcessAsync(nssmPath, $"set {ServiceName} AppEnvironmentExtra \"{envVars}\"");

        // Create logs directory
        var logsPath = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        Directory.CreateDirectory(logsPath);

        WriteLine($"✅ Windows service '{ServiceName}' installed successfully");
        WriteLine($"📁 Logs directory: {logsPath}");

        if (startService)
        {
            WriteLine("🚀 Starting service...");
            var startResult = await RunProcessAsync("net", $"start {ServiceName}");
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
        WriteLine($"   ▶ Start:   net start {ServiceName}");
        WriteLine($"   ⏸ Stop:    net stop {ServiceName}");
        WriteLine($"   📊 Status:  automaton-runner service status");
        WriteLine($"   🗑 Remove:  automaton-runner service uninstall");

        return true;
    }

    public override async Task<bool> UninstallServiceAsync()
    {
        if (!IsAdministrator())
        {
            WriteLine("❌ Administrator privileges required for Windows service uninstallation");
            return false;
        }

        var nssmPath = await FindNSSMAsync();
        if (string.IsNullOrEmpty(nssmPath))
        {
            WriteLine("⚠️ NSSM not found, attempting direct service removal...");
            var result = await RunProcessAsync("sc", $"delete {ServiceName}");
            return result == 0;
        }

        return await StopAndRemoveWindowsServiceAsync(nssmPath);
    }

    public override async Task ShowServiceStatusAsync()
    {
        WriteLine("🪟 Platform: Windows");
        WriteLine($"📦 Service Name: {ServiceName}");
        WriteLine();

        var result = await RunProcessWithOutputAsync("sc", $"query {ServiceName}", true);
        
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
        WriteLine("   ▶ Start:    net start AutomatonRunner");
        WriteLine("   ⏸ Stop:     net stop AutomatonRunner");
        WriteLine("   🗑 Remove:   automaton-runner service uninstall");
    }

    private async Task<bool> StopAndRemoveWindowsServiceAsync(string nssmPath)
    {
        // Stop service if running
        WriteLine($"⏸ Stopping service '{ServiceName}' if running...");
        await RunProcessAsync("net", $"stop {ServiceName}");
        await Task.Delay(2000); // Wait for service to stop

        // Remove service
        WriteLine($"🗑️ Removing service '{ServiceName}'...");
        var result = await RunProcessAsync(nssmPath, $"remove {ServiceName} confirm");
        
        if (result == 0)
        {
            WriteLine("✅ Windows service removed successfully");
            return true;
        }
        else
        {
            WriteLine("❌ Failed to remove Windows service");
            return false;
        }
    }

    private async Task<string?> EnsureNSSMAsync()
    {
        var nssmPath = await FindNSSMAsync();
        if (!string.IsNullOrEmpty(nssmPath))
        {
            return nssmPath;
        }

        WriteLine("⚠️ NSSM not found. For automatic NSSM installation, use:");
        WriteLine("   iwr -useb https://studio.automaton.monster/downloads/install.ps1 | iex");
        return null;
    }

    private async Task<string?> FindNSSMAsync()
    {
        // Check common locations for NSSM
        var possiblePaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "nssm.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NSSM", "win64", "nssm.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NSSM", "win32", "nssm.exe"),
            "nssm.exe" // Check PATH
        };

        foreach (var path in possiblePaths)
        {
            try
            {
                var result = await RunProcessWithOutputAsync(path, "version", true);
                if (result.exitCode == 0)
                {
                    return path;
                }
            }
            catch
            {
                // Continue checking other paths
            }
        }

        return null;
    }

    private static bool IsAdministrator()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
