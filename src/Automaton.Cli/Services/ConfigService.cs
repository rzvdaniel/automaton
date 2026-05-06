using Automaton.Cli.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Cli.Services;

/// <summary>
/// Reads and writes automaton.json (project-level) and ~/.automaton/config.json (global).
/// Resolution order for server: env vars > project config > global config file > prompt.
/// Resolution order for api-key: env vars > global config file > prompt.
/// </summary>
public class ConfigService
{
    private const string ProjectConfigFileName = "automaton.json";
    private const string ProjectIgnoreFileName = ".automatonignore";
    private const string ProjectConfigDirName = ".automaton";
    private const string ProjectLocalConfigFileName = "config.json";
    private static readonly string GlobalConfigDir  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".automaton");
    private static readonly string GlobalConfigFile = Path.Combine(GlobalConfigDir, "config.json");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented       = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ── Project config ────────────────────────────────────────────────────────

    public AutomatonConfig? TryLoadProjectConfig(string directory)
    {
        var path = Path.Combine(directory, ProjectConfigFileName);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AutomatonConfig>(json, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    public AutomatonConfig LoadProjectConfig(string directory)
    {
        var config = TryLoadProjectConfig(directory);
        if (config == null || string.IsNullOrWhiteSpace(config.AppId))
            throw new InvalidOperationException(
                "automaton.json not found or missing appId. Run 'automaton init' or 'automaton link <app-id>' first.");
        return config;
    }

    public void SaveProjectConfig(string directory, AutomatonConfig config)
    {
        var path = Path.Combine(directory, ProjectConfigFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(config, SerializerOptions));

        EnsureProjectIgnoreFile(directory);
    }

    public GlobalConfig LoadProjectLocalConfig(string directory)
    {
        var path = GetProjectLocalConfigPath(directory);
        if (!File.Exists(path)) return new GlobalConfig();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<GlobalConfig>(json, SerializerOptions) ?? new GlobalConfig();
        }
        catch
        {
            return new GlobalConfig();
        }
    }

    public void SaveProjectLocalConfig(string directory, GlobalConfig config)
    {
        var configDir = Path.Combine(directory, ProjectConfigDirName);
        Directory.CreateDirectory(configDir);
        File.WriteAllText(GetProjectLocalConfigPath(directory), JsonSerializer.Serialize(config, SerializerOptions));

        EnsureProjectIgnoreFile(directory);
    }

    public string GetProjectLocalConfigPath(string directory)
    {
        return Path.Combine(directory, ProjectConfigDirName, ProjectLocalConfigFileName);
    }

    private static void EnsureProjectIgnoreFile(string directory)
    {
        var path = Path.Combine(directory, ProjectIgnoreFileName);
        if (File.Exists(path)) return;

        var lines = new List<string>
        {
            "# Patterns excluded by automaton deploy",
            "# Add one pattern per line. Use a trailing / for directories.",
            "# Blank lines and lines starting with # are ignored.",
            string.Empty
        };

        lines.AddRange(ZipService.DefaultIgnorePatterns);
        File.WriteAllLines(path, lines);
    }

    // ── Global config ─────────────────────────────────────────────────────────

    public GlobalConfig LoadGlobalConfig()
    {
        GlobalConfig file = new();
        if (File.Exists(GlobalConfigFile))
        {
            try
            {
                var json = File.ReadAllText(GlobalConfigFile);
                file = JsonSerializer.Deserialize<GlobalConfig>(json, SerializerOptions) ?? new();
            }
            catch { /* ignore malformed global config */ }
        }

        // Env vars always win
        return new GlobalConfig
        {
            Server = Environment.GetEnvironmentVariable("AUTOMATON_SERVER")  ?? file.Server,
            ApiKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY") ?? file.ApiKey
        };
    }

    public void SaveGlobalConfig(GlobalConfig config)
    {
        Directory.CreateDirectory(GlobalConfigDir);
        File.WriteAllText(GlobalConfigFile, JsonSerializer.Serialize(config, SerializerOptions));
    }

    public (string server, string apiKey) RequireCredentials(AutomatonConfig? projectConfig = null, string? directory = null)
    {
        var local = LoadProjectLocalConfig(directory ?? Directory.GetCurrentDirectory());
        var global = LoadGlobalConfig();

        var server = Environment.GetEnvironmentVariable("AUTOMATON_SERVER")
            ?? projectConfig?.Server
            ?? local.Server
            ?? global.Server;
        var apiKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY")
            ?? local.ApiKey
            ?? global.ApiKey;

        if (string.IsNullOrWhiteSpace(server))
        {
            Console.Write("Automaton server URL (e.g. https://localhost:7091): ");
            server = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(server))
                throw new InvalidOperationException("Server URL is required.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Write("API key (ak_execute_xxx): ");
            apiKey = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("API key is required.");
        }

        return (server.TrimEnd('/'), apiKey);
    }
}
