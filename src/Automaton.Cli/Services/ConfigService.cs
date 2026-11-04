using Automaton.Cli.Models;
using Automaton.Core.Models;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Cli.Services;

/// <summary>
/// Reads and writes automaton.json (project-level) and ~/.automaton/config.json (global).
/// Project config and project-local credentials are discovered by walking up from the current directory.
/// Shared credentials live in named profiles. A linked app selects a matching server profile.
/// Explicit CLI and AUTOMATON_PROFILE selections take precedence over the app binding.
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
    private readonly string globalConfigFile;
    public string? ProfileOverride { get; set; }

    public ConfigService(string? globalConfigPath = null)
    {
        globalConfigFile = globalConfigPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".automaton", "config.json");
    }

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

    public void LoadComposeConfiguration(ApplicationContract application, string projectDirectory)
        => application.Compose = ApplicationComposeParser.LoadFromDirectory(projectDirectory);

    public ApplicationContract ResolveDeploymentApplication(
        ApplicationContract application,
        string projectDirectory,
        string? directoryOverride)
    {
        var copy = JsonSerializer.Deserialize<ApplicationContract>(JsonSerializer.Serialize(application))
            ?? throw new InvalidOperationException("The application contract could not be copied for deployment.");
        LoadComposeConfiguration(copy, projectDirectory);
        if (!string.IsNullOrWhiteSpace(directoryOverride)) copy.Build.OutputDirectory = directoryOverride;
        ApplicationContractValidator.EnsureValid(copy);
        return copy;
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
        => SaveProjectConfig(directory, config, overwrite: true);

    internal void SaveProjectConfig(string directory, AutomatonConfig config, bool overwrite)
    {
        var path = Path.Combine(directory, ProjectConfigFileName);
        if (!overwrite) EnsureProjectIgnoreFile(directory);
        WriteJsonAtomically(path, config, overwrite);
        if (overwrite) EnsureProjectIgnoreFile(directory);
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
        var profiles = LoadProfilesConfig();
        var name = SelectedProfileName(profiles);
        return name != null && profiles.Profiles.TryGetValue(name, out var profile)
            ? profile : new GlobalConfig();
    }

    public void SaveGlobalConfig(GlobalConfig config)
    {
        var profiles = LoadProfilesConfig();
        var name = ProfileOverride ?? Environment.GetEnvironmentVariable("AUTOMATON_PROFILE")
            ?? profiles.Current ?? ProfileNameFromServer(config.Server);
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("A profile name or server is required.");
        profiles.Profiles[name] = config;
        profiles.Current ??= name;
        SaveProfilesConfig(profiles);
    }

    public CliProfilesConfig LoadProfilesConfig()
    {
        if (!File.Exists(globalConfigFile)) return new CliProfilesConfig();
        var json = File.ReadAllText(globalConfigFile);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("version", out var version) && version.GetInt32() == 2)
        {
            var profiles = JsonSerializer.Deserialize<CliProfilesConfig>(json, SerializerOptions) ?? new();
            profiles.Profiles = new Dictionary<string, GlobalConfig>(profiles.Profiles ?? [], StringComparer.OrdinalIgnoreCase);
            return profiles;
        }
        if (document.RootElement.TryGetProperty("version", out _))
            throw new InvalidOperationException("Unsupported CLI config version; no credentials were changed.");
        var legacy = JsonSerializer.Deserialize<GlobalConfig>(json, SerializerOptions) ?? new();
        var migrated = new CliProfilesConfig();
        if (string.IsNullOrWhiteSpace(legacy.Server) && !string.IsNullOrWhiteSpace(legacy.ApiKey))
            throw new InvalidOperationException("Legacy CLI config has a key but no server; no credentials were changed.");
        if (!string.IsNullOrWhiteSpace(legacy.Server))
        {
            var name = ProfileNameFromServer(legacy.Server);
            migrated.Profiles[name] = legacy;
            migrated.Current = name;
        }
        SaveProfilesConfig(migrated);
        return migrated;
    }

    public void SaveProfilesConfig(CliProfilesConfig profiles)
        => WriteJsonAtomically(globalConfigFile, profiles);

    public void SaveProfile(string name, GlobalConfig profile)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Profile name is required.");
        var profiles = LoadProfilesConfig();
        profiles.Profiles[name] = profile;
        profiles.Current ??= name;
        SaveProfilesConfig(profiles);
    }

    public bool RemoveProfile(string name)
    {
        var profiles = LoadProfilesConfig();
        if (!profiles.Profiles.Remove(name)) return false;
        if (string.Equals(profiles.Current, name, StringComparison.OrdinalIgnoreCase))
            profiles.Current = profiles.Profiles.Keys.Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        SaveProfilesConfig(profiles);
        return true;
    }

    public bool RemoveSelectedProfile()
    {
        var profiles = LoadProfilesConfig();
        var name = SelectedProfileName(profiles);
        return name != null && RemoveProfile(name);
    }

    public void UseProfile(string name)
    {
        var profiles = LoadProfilesConfig();
        if (!profiles.Profiles.ContainsKey(name)) throw new InvalidOperationException($"Profile '{name}' was not found.");
        profiles.Current = name;
        SaveProfilesConfig(profiles);
    }

    public static string ProfileNameFromServer(string? server)
    {
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri)) throw new ArgumentException("A valid server URL is required.");
        var host = uri.Host;
        return host.StartsWith("api.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    private string? SelectedProfileName(CliProfilesConfig profiles)
        => ProfileOverride ?? Environment.GetEnvironmentVariable("AUTOMATON_PROFILE") ?? profiles.Current;

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
                var profiles = LoadProfilesConfig();
                var name = resolved.ProfileName ?? SelectedProfileName(profiles)
                    ?? throw new InvalidOperationException("No global profile is selected.");
                if (!profiles.Profiles.TryGetValue(name, out var profile))
                    throw new InvalidOperationException($"Profile '{name}' was not found.");
                profile.PendingAuthorizationCompletion = null;
                SaveProfilesConfig(profiles);
                break;
            }
        }
    }

    public bool ClearGlobalConfig()
    {
        if (!File.Exists(globalConfigFile)) return false;

        File.Delete(globalConfigFile);
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
        var resolvedEnvironmentName = ResolveEnvironmentName(cwd, environmentName);
        var namedEnvironment = LoadResolvedEnvironment(cwd, resolvedEnvironmentName, allowMissingEnvironment);
        var linkedServer = FindNearestProjectRoot(cwd) == null ? null : namedEnvironment.Server ?? local.Server;
        var credentials = ResolveStoredCredentials(linkedServer, namedEnvironment, local);

        return new ResolvedCliContext
        {
            EnvironmentName = resolvedEnvironmentName,
            ProfileName = credentials.ProfileName,
            Server = credentials.Server?.TrimEnd('/'),
            ApiKey = credentials.ApiKey,
            AppId = linkedServer != null && !SameServer(credentials.Server, linkedServer)
                ? null : namedEnvironment.AppId?.Trim(),
            CredentialSource = credentials.Source,
            PendingAuthorizationCompletion = credentials.Config.PendingAuthorizationCompletion
        };
    }

    private CredentialSelection ResolveStoredCredentials(string? linkedServer, GlobalConfig namedEnvironment, GlobalConfig local)
    {
        var explicitProfile = ProfileOverride ?? Environment.GetEnvironmentVariable("AUTOMATON_PROFILE");
        var envServer = Environment.GetEnvironmentVariable("AUTOMATON_SERVER");
        var envApiKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
        var (profileName, global) = ResolveGlobalProfile(linkedServer);
        if (explicitProfile != null)
            return new CredentialSelection(profileName, global.Server, global.ApiKey, CliCredentialSource.Global, global);
        if (linkedServer != null && profileName != null && envApiKey == null && envServer == null)
            return new CredentialSelection(profileName, global.Server, global.ApiKey, CliCredentialSource.Global, global);

        var (source, selected) = PickCredentialSource(namedEnvironment, local, global, envApiKey);
        var server = envServer ?? namedEnvironment.Server ?? local.Server ?? global.Server;
        var apiKey = envApiKey ?? selected.ApiKey;
        ValidateCredentialPair(linkedServer, profileName, selected, server, envApiKey);
        return new CredentialSelection(profileName, server, apiKey, source, selected);
    }

    private static void ValidateCredentialPair(string? linkedServer, string? profileName,
        GlobalConfig selected, string? server, string? envApiKey)
    {
        // Existing project-local logins remain usable when they already provide a key for this server.
        if (linkedServer != null && profileName == null && selected.ApiKey == null && envApiKey == null)
            throw new InvalidOperationException($"This app is on {linkedServer}; run `automaton login --server {linkedServer}`.");
        if (selected.Server != null && server != null && !SameServer(selected.Server, server) && envApiKey == null)
            throw new InvalidOperationException($"No credential matches {server}. Run `automaton login --server {server}`.");
    }

    private static (CliCredentialSource Source, GlobalConfig Config) PickCredentialSource(
        GlobalConfig namedEnvironment, GlobalConfig local, GlobalConfig global, string? envApiKey)
    {
        if (envApiKey != null) return (CliCredentialSource.EnvironmentVariable, namedEnvironment);
        if (namedEnvironment.ApiKey != null) return (CliCredentialSource.NamedEnvironment, namedEnvironment);
        if (local.ApiKey != null) return (CliCredentialSource.ProjectLocal, local);
        if (global.ApiKey != null) return (CliCredentialSource.Global, global);
        return (CliCredentialSource.None, global);
    }

    private sealed record CredentialSelection(string? ProfileName, string? Server, string? ApiKey,
        CliCredentialSource Source, GlobalConfig Config);

    private GlobalConfig LoadResolvedEnvironment(string cwd, string? name, bool allowMissing)
    {
        if (name == null) return new GlobalConfig();
        var environment = LoadNamedEnvironmentConfig(cwd, name);
        if (environment != null) return environment;
        if (allowMissing) return new GlobalConfig();
        throw new InvalidOperationException(CreateMissingEnvironmentMessage(cwd, name));
    }

    private (string? Name, GlobalConfig Config) ResolveGlobalProfile(string? linkedServer)
    {
        var profiles = LoadProfilesConfig();
        var requested = ProfileOverride ?? Environment.GetEnvironmentVariable("AUTOMATON_PROFILE");
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (!profiles.Profiles.TryGetValue(requested, out var selected))
                throw new InvalidOperationException($"Profile '{requested}' was not found. Run `automaton profiles`.");
            return (requested, selected);
        }
        if (linkedServer != null)
        {
            var match = profiles.Profiles.FirstOrDefault(pair => SameServer(pair.Value.Server, linkedServer));
            return match.Key == null ? (null, new GlobalConfig()) : (match.Key, match.Value);
        }
        var current = profiles.Current;
        return current != null && profiles.Profiles.TryGetValue(current, out var profile)
            ? (current, profile) : (null, new GlobalConfig());
    }

    private static bool SameServer(string? first, string? second)
        => first != null && second != null && string.Equals(first.TrimEnd('/'), second.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

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

    private static void WriteJsonAtomically<T>(string path, T value, bool overwrite = true)
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

            if (overwrite && File.Exists(path))
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
