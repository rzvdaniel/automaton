using System.Text.Json;
using Automaton.Runner.Console.Models;

namespace Automaton.Runner.Console.Services;

public class FileStorageService
{
    private readonly string configDirectory;
    private readonly string runnerConfigFile;
    private readonly string serverConfigFile;
    private readonly string apiKeyFile;
    private readonly string installationProfileFile;
    private readonly string installationIdFile;

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
        apiKeyFile = Path.Combine(configDirectory, "api-key");
        installationProfileFile = Path.Combine(configDirectory, "installation-profile.json");
        installationIdFile = Path.Combine(configDirectory, "installation-id");

        EnsureConfigDirectoryExists();
    }

    // Environment variables remain supported for existing deployments. Enrolled
    // Runners persist their issued credential in the mounted config volume.
    public async Task<string?> LoadApiKeyAsync()
    {
        var apiKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey;
        }

        if (!File.Exists(apiKeyFile))
        {
            return null;
        }

        try
        {
            var storedKey = await File.ReadAllTextAsync(apiKeyFile);
            return string.IsNullOrWhiteSpace(storedKey) ? null : storedKey.Trim();
        }
        catch
        {
            return null;
        }
    }

    public string GetHostedRunnerCommandResultPath(Guid operationId, Guid attemptId)
    {
        var resultDirectory = Path.Combine(configDirectory, "hosted-runner-command-results");
        Directory.CreateDirectory(resultDirectory);
        return Path.Combine(resultDirectory, $"{operationId:N}-{attemptId:N}.json");
    }

    public async Task SaveApiKeyAsync(string apiKey)
    {
        await File.WriteAllTextAsync(apiKeyFile, apiKey.Trim());
        TryRestrictFile(apiKeyFile);
    }

    public async Task<bool> HasApiKeyAsync()
    {
        return !string.IsNullOrEmpty(await LoadApiKeyAsync());
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

    public async Task SaveInstallationProfileAsync(RunnerInstallationProfile profile)
    {
        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(installationProfileFile, json);
        TryRestrictFile(installationProfileFile);
    }

    public async Task<string> GetOrCreateInstallationIdAsync()
    {
        if (File.Exists(installationIdFile))
        {
            var existing = (await File.ReadAllTextAsync(installationIdFile)).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
                return existing;
        }

        var installationId = $"ri_{Guid.NewGuid():N}";
        await File.WriteAllTextAsync(installationIdFile, installationId);
        TryRestrictFile(installationIdFile);
        return installationId;
    }

    public async Task<RunnerInstallationProfile?> LoadInstallationProfileAsync()
    {
        if (!File.Exists(installationProfileFile))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(installationProfileFile);
            return await JsonSerializer.DeserializeAsync<RunnerInstallationProfile>(stream);
        }
        catch
        {
            return null;
        }
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
        // Check environment variable first
        var envServerUrl = Environment.GetEnvironmentVariable("AUTOMATON_SERVER_URL");
        if (!string.IsNullOrEmpty(envServerUrl))
            return envServerUrl;

        // Fall back to file storage
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

    private static void TryRestrictFile(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best effort only; the container/user filesystem may not support chmod.
        }
    }
}
