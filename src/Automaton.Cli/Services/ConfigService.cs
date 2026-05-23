using Automaton.Cli.Models;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Cli.Services;

/// <summary>
/// Reads and writes automaton.json (project-level) and ~/.automaton/config.json (global).
/// Project config and project-local credentials are discovered by walking up from the current directory.
/// Resolution order for server: project config > project-local config > global config file > env vars > prompt.
/// Resolution order for api-key: project-local config > global config file > env vars > prompt.
/// </summary>
public class ConfigService
{
    private const string ResourcePrefix = "Automaton.Cli.AiGuidance.";
    private const string ProjectConfigFileName = "automaton.json";
    private const string ProjectIgnoreFileName = ".automatonignore";
    private const string ProjectGitIgnoreFileName = ".gitignore";
    private const string ProjectConfigDirName = ".automaton";
    private const string ProjectLocalConfigFileName = "config.json";
    private const string GitIgnoreTemplateResourceName = ResourcePrefix + "templates.gitignore";
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
        var projectRoot = FindNearestProjectRoot(directory) ?? directory;
        var path = Path.Combine(projectRoot, ProjectConfigFileName);
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
        var path = FindNearestProjectLocalConfigPath(directory) ?? GetProjectLocalConfigPath(directory);
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

    public bool ClearProjectLocalConfig(string directory)
    {
        var path = GetProjectLocalConfigPath(directory);
        if (!File.Exists(path)) return false;

        File.Delete(path);
        return true;
    }

    public string GetProjectLocalConfigPath(string directory)
    {
        return Path.Combine(directory, ProjectConfigDirName, ProjectLocalConfigFileName);
    }

    public string? FindNearestProjectRoot(string directory)
    {
        foreach (var current in EnumerateDirectoryAndParents(directory))
        {
            if (File.Exists(Path.Combine(current, ProjectConfigFileName)))
            {
                return current;
            }
        }

        return null;
    }

    public string? FindNearestProjectLocalConfigPath(string directory)
    {
        foreach (var current in EnumerateDirectoryAndParents(directory))
        {
            var candidate = GetProjectLocalConfigPath(current);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public void EnsureProjectGitIgnoreFile(string directory)
    {
        var path = Path.Combine(directory, ProjectGitIgnoreFileName);
        var ignoreEntry = ".automaton/";

        if (!File.Exists(path))
        {
            File.WriteAllText(path, LoadEmbeddedText(GitIgnoreTemplateResourceName));
            return;
        }

        var lines = File.ReadAllLines(path).ToList();
        if (lines.Any(line => string.Equals(line.Trim(), ignoreEntry, StringComparison.Ordinal)))
        {
            return;
        }

        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.Add(string.Empty);
        }

        lines.Add(ignoreEntry);
        File.WriteAllLines(path, lines);
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

        return new GlobalConfig
        {
            Server = file.Server,
            ApiKey = file.ApiKey
        };
    }

    public void SaveGlobalConfig(GlobalConfig config)
    {
        Directory.CreateDirectory(GlobalConfigDir);
        File.WriteAllText(GlobalConfigFile, JsonSerializer.Serialize(config, SerializerOptions));
    }

    public bool ClearGlobalConfig()
    {
        if (!File.Exists(GlobalConfigFile)) return false;

        File.Delete(GlobalConfigFile);
        return true;
    }

    public GlobalConfig ResolveExistingConfig(bool global, string? directory = null)
    {
        if (global)
        {
            return LoadGlobalConfig();
        }

        var (server, apiKey) = ResolveCredentials(directory: directory);
        return new GlobalConfig
        {
            Server = server,
            ApiKey = apiKey
        };
    }

    public (string? server, string? apiKey) ResolveCredentials(AutomatonConfig? projectConfig = null, string? directory = null)
    {
        var cwd = directory ?? Directory.GetCurrentDirectory();
        var local = LoadProjectLocalConfig(cwd);
        var global = LoadGlobalConfig();
        var envServer = Environment.GetEnvironmentVariable("AUTOMATON_SERVER");
        var envApiKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");

        var server = projectConfig?.Server
            ?? local.Server
            ?? global.Server
            ?? envServer;
        var apiKey = local.ApiKey
            ?? global.ApiKey
            ?? envApiKey;

        return (server?.TrimEnd('/'), apiKey);
    }

    public (string server, string apiKey) RequireCredentials(AutomatonConfig? projectConfig = null, string? directory = null)
    {
        var (resolvedServer, resolvedApiKey) = ResolveCredentials(projectConfig, directory);
        var server = resolvedServer;
        var apiKey = resolvedApiKey;

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

    private static IEnumerable<string> EnumerateDirectoryAndParents(string directory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(directory));
        while (current != null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private static string LoadEmbeddedText(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
