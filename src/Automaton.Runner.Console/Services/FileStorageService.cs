using System.Text.Json;

namespace Automaton.Runner.Console.Services;

public class FileStorageService
{
    private readonly string configDirectory;
    private readonly string credentialsFile;
    private readonly string runnerConfigFile;

    public FileStorageService()
    {
        configDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutomatonRunner");
        credentialsFile = Path.Combine(configDirectory, "credentials.json");
        runnerConfigFile = Path.Combine(configDirectory, "runner.json");

        EnsureConfigDirectoryExists();
    }

    public async Task SaveCredentialsAsync(string accessToken, string refreshToken)
    {
        var credentials = new
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            SavedAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(credentialsFile, json);
    }

    public async Task<(string? AccessToken, string? RefreshToken)> LoadCredentialsAsync()
    {
        if (!File.Exists(credentialsFile))
            return (null, null);

        try
        {
            var json = await File.ReadAllTextAsync(credentialsFile);
            var credentials = JsonSerializer.Deserialize<JsonElement>(json);

            var accessToken = credentials.TryGetProperty("AccessToken", out var at) ? at.GetString() : null;
            var refreshToken = credentials.TryGetProperty("RefreshToken", out var rt) ? rt.GetString() : null;

            return (accessToken, refreshToken);
        }
        catch
        {
            return (null, null);
        }
    }

    public async Task DeleteCredentialsAsync()
    {
        if (File.Exists(credentialsFile))
        {
            File.Delete(credentialsFile);
        }
    }

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

    public async Task DeleteRunnerConfigAsync()
    {
        if (File.Exists(runnerConfigFile))
        {
            File.Delete(runnerConfigFile);
        }
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

    public bool IsLoggedIn()
    {
        var (accessToken, _) = LoadCredentialsAsync().Result;
        return !string.IsNullOrEmpty(accessToken);
    }

    private void EnsureConfigDirectoryExists()
    {
        if (!Directory.Exists(configDirectory))
        {
            Directory.CreateDirectory(configDirectory);
        }
    }
}