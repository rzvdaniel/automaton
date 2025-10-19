using System.Text.Json;

namespace Automaton.Runner.Console.Services;

public class FileStorageService
{
    private readonly string configDirectory;
    private readonly string runnerConfigFile;
    private readonly string serverConfigFile;

    public FileStorageService()
    {
        // Check if running as a Windows service (SYSTEM account won't have normal APPDATA)
        var isService = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USERPROFILE")) ||
                       Environment.UserName.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase);
        
        if (isService)
        {
            // When running as service, use config folder in working directory
            configDirectory = Path.Combine(Directory.GetCurrentDirectory(), "config");
        }
        else
        {
            // When running interactively, use user's AppData
            configDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutomatonRunner");
        }
        
        runnerConfigFile = Path.Combine(configDirectory, "runner.json");
        serverConfigFile = Path.Combine(configDirectory, "server.json");

        EnsureConfigDirectoryExists();
    }

    // API Key methods - Environment variable only (no file storage)
    public Task<string?> LoadApiKeyAsync()
    {
        // API keys are now stored in environment variables only for better security
        var apiKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
        return Task.FromResult(apiKey);
    }

    public Task<bool> HasApiKeyAsync()
    {
        var apiKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
        return Task.FromResult(!string.IsNullOrEmpty(apiKey));
    }

    // Runner configuration methods
    public async Task SaveRunnerConfigAsync(string runnerId, string runnerName)
    {
        var config = new
        {
            RunnerId = runnerId,
            RunnerName = runnerName,
            RegisteredAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(runnerConfigFile, json);
    }

    public string? GetRunnerId()
    {
        // Check environment variable first (for service mode)
        var envRunnerId = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_ID");
        if (!string.IsNullOrEmpty(envRunnerId))
            return envRunnerId;
        
        // Fall back to file storage
        if (!File.Exists(runnerConfigFile))
            return null;

        try
        {
            var json = File.ReadAllText(runnerConfigFile);
            var config = JsonSerializer.Deserialize<JsonElement>(json);
            return config.TryGetProperty("RunnerId", out var id) ? id.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public string? GetRunnerName()
    {
        // Check environment variable first (for service mode)
        var envRunnerName = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_NAME");
        if (!string.IsNullOrEmpty(envRunnerName))
            return envRunnerName;
        
        // Fall back to file storage
        if (!File.Exists(runnerConfigFile))
            return null;

        try
        {
            var json = File.ReadAllText(runnerConfigFile);
            var config = JsonSerializer.Deserialize<JsonElement>(json);
            return config.TryGetProperty("RunnerName", out var name) ? name.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public bool IsRunnerRegistered()
    {
        return !string.IsNullOrEmpty(GetRunnerId()) && !string.IsNullOrEmpty(GetRunnerName());
    }

    // Server configuration methods
    public async Task SaveServerConfigAsync(string serverUrl)
    {
        var config = new
        {
            ServerUrl = serverUrl,
            SavedAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(serverConfigFile, json);
    }

    public async Task<string?> LoadServerUrlAsync()
    {
        if (!File.Exists(serverConfigFile))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(serverConfigFile);
            var config = JsonSerializer.Deserialize<JsonElement>(json);
            return config.TryGetProperty("ServerUrl", out var url) ? url.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public Task DeleteServerConfigAsync()
    {
        if (File.Exists(serverConfigFile))
        {
            File.Delete(serverConfigFile);
        }
        return Task.CompletedTask;
    }

    public bool IsLoggedIn()
    {
        // Check API key authentication from environment variable only
        var apiKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
        
        return !string.IsNullOrEmpty(apiKey);
    }

    public Task DeleteRunnerConfigAsync()
    {
        if (File.Exists(runnerConfigFile))
        {
            File.Delete(runnerConfigFile);
        }
        return Task.CompletedTask;
    }

    private void EnsureConfigDirectoryExists()
    {
        if (!Directory.Exists(configDirectory))
        {
            Directory.CreateDirectory(configDirectory);
        }
    }
}