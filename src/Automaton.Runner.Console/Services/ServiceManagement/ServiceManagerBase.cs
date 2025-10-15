using Serilog;
using System.Diagnostics;

namespace Automaton.Runner.Console.Services.ServiceManagement;

/// <summary>
/// Base class for platform-specific service managers with common functionality
/// </summary>
public abstract class ServiceManagerBase : IServiceManager
{
    protected readonly ConfigurationService configService;
    protected readonly ILogger logger;

    protected const string ServiceName = "AutomatonRunner";
    protected const string ServiceDisplayName = "Automaton Runner";
    protected const string ServiceDescription = "Automaton workflow runner for cross-platform automation";

    protected ServiceManagerBase(ConfigurationService configService)
    {
        this.configService = configService;
        this.logger = Log.ForContext(GetType());
    }

    public abstract Task<bool> InstallServiceAsync(string? runnerName, bool startService);
    public abstract Task<bool> UninstallServiceAsync();
    public abstract Task ShowServiceStatusAsync();

    /// <summary>
    /// Runs a process and returns its exit code
    /// </summary>
    protected async Task<int> RunProcessAsync(string fileName, string arguments)
    {
        var result = await RunProcessWithOutputAsync(fileName, arguments, false);
        return result.exitCode;
    }

    /// <summary>
    /// Runs a process and optionally captures its output
    /// </summary>
    protected async Task<(int exitCode, string output)> RunProcessWithOutputAsync(
        string fileName, 
        string arguments, 
        bool captureOutput)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = captureOutput,
                RedirectStandardError = captureOutput
            };

            process.Start();

            string output = "";
            if (captureOutput)
            {
                output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                if (!string.IsNullOrEmpty(error))
                {
                    output += "\n" + error;
                }
            }

            await process.WaitForExitAsync();
            return (process.ExitCode, output);
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to run process {FileName} {Arguments}", fileName, arguments);
            return (-1, ex.Message);
        }
    }

    /// <summary>
    /// Gets the base URL from configuration or defaults
    /// </summary>
    protected string GetBaseUrl()
    {
        return configService.IsServerConfigured() 
            ? configService.BaseUrl 
            : "https://localhost:7091";
    }

    /// <summary>
    /// Gets the API key from environment variable
    /// </summary>
    protected string? GetApiKey()
    {
        return Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
    }
}
