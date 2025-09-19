using System.Text.Json;

namespace Automaton.Runner.Console.Services;

public class FileStorageService
{
    private readonly string configDirectory;
    private readonly string apiKeyFile;
    private readonly string runnerConfigFile;
    private readonly string serverConfigFile;

    public FileStorageService()
    {
        configDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutomatonRunner");
        apiKeyFile = Path.Combine(configDirectory, "apikey.json");
        runnerConfigFile = Path.Combine(configDirectory, "runner.json");
        serverConfigFile = Path.Combine(configDirectory, "server.json");

        EnsureConfigDirectoryExists();
    }

    // API Key methods
    public async Task SaveApiKeyAsync(string apiKey)
    {
        var apiKeyData = new
        {
            ApiKey = apiKey,
            SavedAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(apiKeyData, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(apiKeyFile, json);
    }

    public async Task<string?> LoadApiKeyAsync()
    {
        if (!File.Exists(apiKeyFile))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(apiKeyFile);
            var apiKeyData = JsonSerializer.Deserialize<JsonElement>(json);

            return apiKeyData.TryGetProperty("ApiKey", out var ak) ? ak.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public Task DeleteApiKeyAsync()
    {
        if (File.Exists(apiKeyFile))
        {
            File.Delete(apiKeyFile);
        }
        return Task.CompletedTask;
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
        // Check API key authentication (both stored and environment variable)
        var apiKey = LoadApiKeyAsync().Result;
        var envApiKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
        
        return !string.IsNullOrEmpty(apiKey) || !string.IsNullOrEmpty(envApiKey);
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