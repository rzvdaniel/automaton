using Automaton.Cli.Models;
using Automaton.Core.Models;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Cli.Services;

/// <summary>
/// Reads and writes automaton.json (project-level) and ~/.automaton/config.json (global).
/// Project config and project-local credentials are discovered by walking up from the current directory.
/// Resolution order for server: AUTOMATON_SERVER > named environment > project-local config > global config file > prompt.
/// Resolution order for api-key: AUTOMATON_API_KEY > named environment > project-local config > global config file > prompt.
/// Resolution order for app-id: named environment.
/// </summary>
public class ConfigService
{
    public const string DefaultHostedServer = "https://api.automaton.monster";
    public const string DefaultEnvironmentName = "live";

    private const string ResourcePrefix = "Automaton.Cli.AiGuidance.";
    private const string ProjectConfigFileName = "automaton.json";
    private const string ProjectIgnoreFileName = ".automatonignore";
    private const string ProjectGitIgnoreFileName = ".gitignore";
    private const string ProjectConfigDirName = ".automaton";
    private const string ProjectLocalConfigFileName = "config.json";
    private const string ProjectEnvironmentDirName = "environments";
    private const string ProjectEnvironmentSelectionFileName = "environment.json";
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
            using var document = JsonDocument.Parse(json);
            ApplicationContractSerialization.RejectLegacyFields(document.RootElement);
            var config = JsonSerializer.Deserialize<AutomatonConfig>(json, SerializerOptions);
            if (config is null)
                return null;

            ApplicationContractValidator.EnsureValid(config.Application);
            return config;
        }
        catch
        {
            return null;
        }
    }

    public AutomatonConfig LoadProjectConfig(string directory)
    {
        var config = TryLoadProjectConfig(directory);
        if (config == null)
        {
            var projectRoot = FindNearestProjectRoot(directory);
            if (projectRoot is not null)
            {
                throw new InvalidOperationException(
                    $"'{Path.Combine(projectRoot, ProjectConfigFileName)}' is invalid or unreadable. Fix the local application contract and retry.");
            }

            throw new InvalidOperationException(
                "automaton.json not found. Run 'automaton new' first.");
        }
        return config;
    }

    public void SaveProjectConfig(string directory, AutomatonConfig config)
    {
        var path = Path.Combine(directory, ProjectConfigFileName);
        WriteJsonAtomically(path, config);

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
        WriteJsonAtomically(GetProjectLocalConfigPath(directory), config);

        EnsureProjectIgnoreFile(directory);
    }

    public void SaveNamedEnvironmentConfig(string directory, string environmentName, GlobalConfig config)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            throw new InvalidOperationException("Environment name is required.");
        }

        var projectRoot = FindNearestProjectRoot(directory) ?? directory;
        var configPath = GetProjectEnvironmentConfigPath(projectRoot, environmentName.Trim());
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        WriteJsonAtomically(configPath, config);

        EnsureProjectIgnoreFile(projectRoot);
    }

    public void SaveProjectEnvironmentSelection(string directory, ProjectEnvironmentSelection selection)
    {
        var projectRoot = FindNearestProjectRoot(directory) ?? directory;
        var path = GetProjectEnvironmentSelectionPath(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteJsonAtomically(path, selection);

        EnsureProjectIgnoreFile(projectRoot);
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

    public string GetProjectEnvironmentConfigPath(string directory, string environmentName)
    {
        return Path.Combine(directory, ProjectConfigDirName, ProjectEnvironmentDirName, $"{environmentName}.json");
    }

    public string GetProjectEnvironmentSelectionPath(string directory)
    {
        return Path.Combine(directory, ProjectConfigDirName, ProjectEnvironmentSelectionFileName);
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

    public string? FindNearestProjectEnvironmentConfigPath(string directory, string environmentName)
    {
        foreach (var current in EnumerateDirectoryAndParents(directory))
        {
            var candidate = GetProjectEnvironmentConfigPath(current, environmentName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public string? FindNearestProjectEnvironmentSelectionPath(string directory)
    {
        foreach (var current in EnumerateDirectoryAndParents(directory))
        {
            var candidate = GetProjectEnvironmentSelectionPath(current);
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
            "# Add one pattern per line. Use /pattern for project-root paths,",
            "# a trailing / for directories, and !pattern to re-include files.",
            "# Blank lines and lines starting with # are ignored.",
            string.Empty
        };

        lines.AddRange(ZipService.ScaffoldIgnorePatterns);
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
            ApiKey = file.ApiKey,
            AppId = file.AppId,
            PendingAuthorizationCompletion = file.PendingAuthorizationCompletion
        };
    }

    public void SaveGlobalConfig(GlobalConfig config)
    {
        Directory.CreateDirectory(GlobalConfigDir);
        WriteJsonAtomically(GlobalConfigFile, config);
    }

    public void ClearPendingAuthorizationCompletion(string directory, ResolvedCliContext resolved)
    {
        switch (resolved.CredentialSource)
        {
            case CliCredentialSource.ProjectLocal:
            {
                var config = LoadProjectLocalConfig(directory);
                config.PendingAuthorizationCompletion = null;
                SaveProjectLocalConfig(directory, config);
                break;
            }
            case CliCredentialSource.NamedEnvironment when !string.IsNullOrWhiteSpace(resolved.EnvironmentName):
            {
                var config = LoadNamedEnvironmentConfig(directory, resolved.EnvironmentName!)
                    ?? throw new InvalidOperationException($"Environment '{resolved.EnvironmentName}' was not found.");
                config.PendingAuthorizationCompletion = null;
                SaveNamedEnvironmentConfig(directory, resolved.EnvironmentName!, config);
                break;
            }
            case CliCredentialSource.Global:
            {
                var config = LoadGlobalConfig();
                config.PendingAuthorizationCompletion = null;
                SaveGlobalConfig(config);
                break;
            }
        }
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

        return LoadProjectLocalConfig(directory ?? Directory.GetCurrentDirectory());
    }

    public ResolvedCliContext ResolveContext(
        AutomatonConfig? projectConfig = null,
        string? directory = null,
        string? environmentName = null,
        bool allowMissingEnvironment = false)
    {
        var cwd = directory ?? Directory.GetCurrentDirectory();
        var local = LoadProjectLocalConfig(cwd);
        var global = LoadGlobalConfig();
        var resolvedEnvironmentName = ResolveEnvironmentName(cwd, environmentName);
        var namedEnvironment = resolvedEnvironmentName == null
            ? new GlobalConfig()
            : LoadNamedEnvironmentConfig(cwd, resolvedEnvironmentName)
                ?? (allowMissingEnvironment
                    ? new GlobalConfig()
                    : throw new InvalidOperationException(CreateMissingEnvironmentMessage(cwd, resolvedEnvironmentName)));
        var envServer = Environment.GetEnvironmentVariable("AUTOMATON_SERVER");
        var envApiKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");

        var credentialSource = !string.IsNullOrWhiteSpace(envApiKey)
            ? CliCredentialSource.EnvironmentVariable
            : !string.IsNullOrWhiteSpace(namedEnvironment.ApiKey)
                ? CliCredentialSource.NamedEnvironment
                : !string.IsNullOrWhiteSpace(local.ApiKey)
                    ? CliCredentialSource.ProjectLocal
                    : !string.IsNullOrWhiteSpace(global.ApiKey)
                        ? CliCredentialSource.Global
                        : CliCredentialSource.None;

        var selectedConfig = credentialSource switch
        {
            CliCredentialSource.NamedEnvironment => namedEnvironment,
            CliCredentialSource.ProjectLocal => local,
            CliCredentialSource.Global => global,
            _ => null
        };

        var server = envServer
            ?? namedEnvironment.Server
            ?? local.Server
            ?? global.Server;
        var apiKey = envApiKey
            ?? namedEnvironment.ApiKey
            ?? local.ApiKey
            ?? global.ApiKey;

        return new ResolvedCliContext
        {
            EnvironmentName = resolvedEnvironmentName,
            Server = server?.TrimEnd('/'),
            ApiKey = apiKey,
            AppId = namedEnvironment.AppId?.Trim(),
            CredentialSource = credentialSource,
            PendingAuthorizationCompletion = selectedConfig?.PendingAuthorizationCompletion
        };
    }

    public (string? server, string? apiKey) ResolveCredentials(
        AutomatonConfig? projectConfig = null,
        string? directory = null,
        string? environmentName = null,
        bool allowMissingEnvironment = false)
    {
        var resolved = ResolveContext(projectConfig, directory, environmentName, allowMissingEnvironment);
        return (resolved.Server, resolved.ApiKey);
    }

    public (string server, string apiKey) RequireCredentials(
        AutomatonConfig? projectConfig = null,
        string? directory = null,
        string? environmentName = null,
        bool allowMissingEnvironment = false)
    {
        var resolved = ResolveContext(projectConfig, directory, environmentName, allowMissingEnvironment);
        var server = resolved.Server;
        var apiKey = resolved.ApiKey;

        if (string.IsNullOrWhiteSpace(server))
        {
            Console.Write("Automaton server URL (e.g. https://localhost:7091): ");
            server = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(server))
                throw new InvalidOperationException("Server URL is required.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Write("Developer API key (ak_developer_xxx): ");
            apiKey = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("API key is required.");
        }

        return (server.TrimEnd('/'), apiKey);
    }

    public string ResolveAppBindingEnvironmentName(string directory, string? requestedEnvironmentName = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedEnvironmentName))
        {
            return requestedEnvironmentName.Trim();
        }

        var selection = LoadProjectEnvironmentSelection(directory);
        return string.IsNullOrWhiteSpace(selection.Default)
            ? DefaultEnvironmentName
            : selection.Default.Trim();
    }

    public GlobalConfig? LoadNamedEnvironmentConfig(string directory, string environmentName)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            return null;
        }

        var path = FindNearestProjectEnvironmentConfigPath(directory, environmentName.Trim())
                   ?? GetProjectEnvironmentConfigPath(FindNearestProjectRoot(directory) ?? directory, environmentName.Trim());
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<GlobalConfig>(json, SerializerOptions) ?? new GlobalConfig();
        }
        catch
        {
            return null;
        }
    }

    public ProjectEnvironmentSelection LoadProjectEnvironmentSelection(string directory)
    {
        var path = FindNearestProjectEnvironmentSelectionPath(directory)
                   ?? GetProjectEnvironmentSelectionPath(FindNearestProjectRoot(directory) ?? directory);
        if (!File.Exists(path))
        {
            return new ProjectEnvironmentSelection();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProjectEnvironmentSelection>(json, SerializerOptions) ?? new ProjectEnvironmentSelection();
        }
        catch
        {
            return new ProjectEnvironmentSelection();
        }
    }

    private string? ResolveEnvironmentName(string directory, string? explicitEnvironmentName)
    {
        if (!string.IsNullOrWhiteSpace(explicitEnvironmentName))
        {
            return explicitEnvironmentName.Trim();
        }

        var selection = LoadProjectEnvironmentSelection(directory);
        if (!string.IsNullOrWhiteSpace(selection.Default))
        {
            return selection.Default.Trim();
        }

        return FindNearestProjectEnvironmentConfigPath(directory, DefaultEnvironmentName) != null
            ? DefaultEnvironmentName
            : null;
    }

    private string CreateMissingEnvironmentMessage(string directory, string environmentName)
    {
        var projectRoot = FindNearestProjectRoot(directory) ?? directory;
        var environmentPath = GetProjectEnvironmentConfigPath(projectRoot, environmentName);
        return $"Environment '{environmentName}' was not found. Create '{environmentPath}' or choose a different environment.";
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

    private static void WriteJsonAtomically<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Cannot determine the configuration directory for '{path}'.");
        }

        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var contents = JsonSerializer.Serialize(value, SerializerOptions);

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                if (OperatingSystem.IsWindows())
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, path);
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
