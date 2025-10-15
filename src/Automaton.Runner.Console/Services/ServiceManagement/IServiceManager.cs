namespace Automaton.Runner.Console.Services.ServiceManagement;

/// <summary>
/// Interface for platform-specific service management operations
/// </summary>
public interface IServiceManager
{
    /// <summary>
    /// Installs the runner as a system service
    /// </summary>
    /// <param name="runnerName">Optional runner name for the service</param>
    /// <param name="startService">Whether to start the service after installation</param>
    /// <returns>True if installation was successful</returns>
    Task<bool> InstallServiceAsync(string? runnerName, bool startService);

    /// <summary>
    /// Uninstalls the system service
    /// </summary>
    /// <returns>True if uninstallation was successful</returns>
    Task<bool> UninstallServiceAsync();

    /// <summary>
    /// Shows the current status of the system service
    /// </summary>
    Task ShowServiceStatusAsync();
}
