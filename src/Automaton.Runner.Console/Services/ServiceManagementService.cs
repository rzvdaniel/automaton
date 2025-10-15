using Automaton.Runner.Console.Services.ServiceManagement;
using Serilog;
using System.Runtime.InteropServices;
using static System.Console;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Service management orchestrator that delegates to platform-specific implementations
/// </summary>
public class ServiceManagementService
{
    private readonly IServiceManager serviceManager;
    private readonly ILogger logger;

    public ServiceManagementService(ConfigurationService configService)
    {
        this.logger = Log.ForContext<ServiceManagementService>();
        
        // Create platform-specific service manager
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            serviceManager = new WindowsServiceManager(configService);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            serviceManager = new LinuxServiceManager(configService);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            serviceManager = new MacOSServiceManager(configService);
        }
        else
        {
            throw new PlatformNotSupportedException("Service management is not supported on this platform");
        }
    }

    public async Task<bool> InstallServiceAsync(string? runnerName = null, bool startService = true)
    {
        WriteLine("🔧 Installing runner as system service...");
        WriteLine();

        try
        {
            return await serviceManager.InstallServiceAsync(runnerName, startService);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error installing service");
            WriteLine($"❌ Service installation failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UninstallServiceAsync()
    {
        WriteLine("🗑️ Uninstalling system service...");
        WriteLine();

        try
        {
            return await serviceManager.UninstallServiceAsync();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error uninstalling service");
            WriteLine($"❌ Service uninstallation failed: {ex.Message}");
            return false;
        }
    }

    public async Task ShowServiceStatusAsync()
    {
        WriteLine();
        WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        WriteLine("         📊 SERVICE STATUS REPORT         ");
        WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        WriteLine();

        try
        {
            await serviceManager.ShowServiceStatusAsync();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error checking service status");
            WriteLine($"❌ Service status check failed: {ex.Message}");
        }

        WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        WriteLine();
    }
}
