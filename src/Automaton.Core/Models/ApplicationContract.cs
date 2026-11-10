using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Core.Models;

/// <summary>
/// The stack-neutral application contract shared by the control plane, CLI, and
/// runner boundaries. It describes what an application needs without naming a
/// language, framework, package manager, or source layout.
/// </summary>
public sealed class ApplicationContract
{
    public ApplicationBuildSpec Build { get; set; } = new();
    public ApplicationRuntimeSpec Runtime { get; set; } = new();
    public ApplicationHealthSpec Health { get; set; } = new();
    public ApplicationStorageSpec Storage { get; set; } = new();
    public ApplicationComposeSpec? Compose { get; set; }

    /// <summary>Optional: <c>web</c> or <c>api</c> for a running app. Missing means web.</summary>
    public string? Kind { get; set; }

    /// <summary>Optional one-line description of the app.</summary>
    public string? Description { get; set; }

    /// <summary>Optional key paths of a web app, each with a label.</summary>
    public List<ApplicationPageDeclaration> Pages { get; set; } = [];

    /// <summary>Optional path of the app's OpenAPI document (an API app), relative to the app's address.</summary>
    public string? OpenApi { get; set; }

    /// <summary>
    /// Secrets the app needs by name. An app secret of that name wins; otherwise a declared name is
    /// filled from the project vault, then the owner's vault. Undeclared vault secrets never reach the app.
    /// </summary>
    public List<ApplicationSecretDeclaration> Secrets { get; set; } = [];

    /// <summary>Platform capabilities the app requests for administrator approval.</summary>
    public List<string> Permissions { get; set; } = [];

    /// <summary>
    /// Paths of the app's private address that answer without Automaton sign-in, for webhooks and callbacks:
    /// <c>/callback</c> (exactly that path) or <c>/hooks/*</c> (that folder and everything below it).
    /// </summary>
    public List<string> OpenPaths { get; set; } = [];

    /// <summary>Plain (non-secret) values an installer chooses, delivered as environment variables like secrets.</summary>
    public List<ApplicationSettingDeclaration> Settings { get; set; } = [];

    /// <summary>Action schedules the installer is offered; each is created only when the installer accepts it.</summary>
    public List<ApplicationScheduleDeclaration> Schedules { get; set; } = [];

    /// <summary>Fields this version does not know; kept so older tools never drop a newer contract field.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public bool IsStaticSite => Runtime.Command is null
        && Compose is null
        && Build.Command is not null
        && !string.IsNullOrWhiteSpace(Build.OutputDirectory);
}

/// <summary>A key path of a web app with a short label.</summary>
public sealed class ApplicationPageDeclaration
{
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A secret the app expects as an environment variable.</summary>
public sealed class ApplicationSecretDeclaration
{
    /// <summary>The environment variable name the app reads.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional one-line explanation, shown to people and AI agents filling the secret.</summary>
    public string? Description { get; set; }

    /// <summary>Optional source in a vault under another name: <c>project:NAME</c> or <c>user:NAME</c>.</summary>
    public string? From { get; set; }

    /// <summary>The app works without a value (it reads an empty or missing variable), so the installer may leave it empty.</summary>
    public bool Optional { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A non-secret value the installer sets, read by the app as an environment variable.</summary>
public sealed class ApplicationSettingDeclaration
{
    /// <summary>The environment variable name the app reads.</summary>
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Value used when the installer leaves the setting empty.</summary>
    public string? Default { get; set; }
    /// <summary>The installer must provide a value (or the default must exist).</summary>
    public bool Required { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A default schedule for one of the app's Actions: a cron expression or an interval.</summary>
public sealed class ApplicationScheduleDeclaration
{
    public string Name { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Five-field cron expression; use this or <see cref="IntervalSeconds"/>.</summary>
    public string? Cron { get; set; }
    public int? IntervalSeconds { get; set; }
    public string TimeZone { get; set; } = "Etc/UTC";
    /// <summary>Input passed to the Action on each run.</summary>
    public JsonElement? Input { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Names and sources of declared secrets.</summary>
public static class ApplicationSecretNames
{
    public const string ProjectScope = "project";
    public const string UserScope = "user";
    public const int MaxDescriptionLength = 200;

    /// <summary>Environment variable names: capital letters, digits and underscores, not starting with a digit or AUTOMATON_.</summary>
    public static bool IsValidName(string? name)
        => name is { Length: > 0 and <= 100 }
            && (name[0] is '_' or (>= 'A' and <= 'Z'))
            && name.All(c => c is '_' or (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            && !name.StartsWith("AUTOMATON_", StringComparison.Ordinal)
            && name != "APP_AUTOMATON_API_KEY";

    /// <summary>Parses <c>project:NAME</c> or <c>user:NAME</c>.</summary>
    public static bool TryParseFrom(string? from, out string scope, out string name)
    {
        scope = name = string.Empty;
        var separator = from?.IndexOf(':') ?? -1;
        if (separator <= 0) return false;
        scope = from![..separator];
        name = from[(separator + 1)..];
        return scope is ProjectScope or UserScope && IsValidName(name);
    }
}

/// <summary>A normalized, deliberately small Docker Compose deployment contract.</summary>
public sealed class ApplicationComposeSpec
{
    public string PublicService { get; set; } = string.Empty;
    public List<ApplicationComposeService> Services { get; set; } = [];
    public List<string> Volumes { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ApplicationComposeService
{
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public int? Port { get; set; }
    public List<string> Command { get; set; } = [];
    public List<string> Entrypoint { get; set; } = [];
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.Ordinal);
    public List<string> DependsOn { get; set; } = [];
    public List<ApplicationComposeVolumeMount> Mounts { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ApplicationComposeVolumeMount
{
    public string Name { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public bool ReadOnly { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ApplicationBuildSpec
{
    /// <summary>
    /// OCI image used to execute the build command. When omitted, the runtime
    /// image is used for applications that declare a runtime image.
    /// </summary>
    public string? Image { get; set; }
    public ApplicationCommandSpec? Command { get; set; }
    public string? OutputDirectory { get; set; }

    /// <summary>True when the static site is a single-page app: unknown paths get index.html instead of 404.</summary>
    public bool SinglePageApp { get; set; }
    public int TimeoutSeconds { get; set; } = 900;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ApplicationRuntimeSpec
{
    /// <summary>OCI image that owns the application runtime process.</summary>
    public string? Image { get; set; }
    /// <summary>Optional numeric Linux UID or UID:GID for the runtime process.</summary>
    public string? User { get; set; }
    public ApplicationCommandSpec? Command { get; set; }
    public int Port { get; set; } = 8080;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ApplicationHealthSpec
{
    public string? Path { get; set; }
    public int? Port { get; set; }
    public int StartupTimeoutSeconds { get; set; } = 60;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ApplicationStorageSpec
{
    public string DataDirectory { get; set; } = "data";
    public List<ApplicationStorageMount> Mounts { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ApplicationStorageMount
{
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool ReadOnly { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ApplicationCommandSpec
{
    public string Executable { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = [];
    public string WorkingDirectory { get; set; } = ".";
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Explicitly requests shell semantics for commands that genuinely require
    /// shell expansion. Structured execution remains the default.
    /// </summary>
    public bool UseShell { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public static class ApplicationContractDefaults
{
    public static ApplicationContract Create() => new();
}

public static class ApplicationContractSerialization
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Serialize(ApplicationContract contract)
    {
        ApplicationContractValidator.EnsureValid(contract);
        return JsonSerializer.Serialize(contract, JsonOptions);
    }

    public static ApplicationContract Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Application contract JSON is required.");

        using var document = JsonDocument.Parse(json);
        RejectLegacyFields(document.RootElement);

        var contract = JsonSerializer.Deserialize<ApplicationContract>(json, JsonOptions)
            ?? throw new InvalidOperationException("Application contract JSON is invalid.");
        ApplicationContractValidator.EnsureValid(contract);
        return contract;
    }

    public static void RejectLegacyFields(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Application contract JSON must be an object.");

        if (root.EnumerateObject().Any(property =>
                string.Equals(property.Name, "frontend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(property.Name, "backend", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "The legacy frontend/backend application contract is not supported.");
        }
    }
}

public static class ApplicationContractValidator
{
    public static void EnsureProcessRuntime(ApplicationContract contract)
    {
        if (contract.Runtime.Command is null && contract.Compose is not null)
            throw new InvalidOperationException("Compose deployments require the typed managed-container runtime.");
    }

    public static IReadOnlyList<string> Validate(ApplicationContract? contract)
    {
        var errors = new List<string>();
        if (contract is null)
        {
            errors.Add("application contract is required");
            return errors;
        }

        ValidateCommand(contract.Build?.Command, "build.command", errors);
        ValidateCommand(contract.Runtime?.Command, "runtime.command", errors);
        ValidateImage(contract.Build?.Image, "build.image", errors);
        ValidateImage(contract.Runtime?.Image, "runtime.image", errors);

        ValidateBuild(contract.Build, errors);
        ValidateRuntime(contract.Runtime, errors);
        ValidateHealth(contract.Health, errors);
        ValidateStorage(contract.Storage, errors);
        ValidateCompose(contract, errors);
        ValidateSecrets(contract.Secrets, errors);
        ValidatePermissions(contract.Permissions, errors);
        ValidateOpenPaths(contract.OpenPaths, errors);
        ValidateSettings(contract.Settings, contract.Secrets, errors);
        ValidateSchedules(contract.Schedules, errors);
        ValidateDescriptors(contract, errors);

        return errors;
    }

    /// <summary>The first supported app permission, read access to the server's backups.</summary>
    public const string SupportedPermission = "backups:read";

    /// <summary>Every app permission an app can request.</summary>
    public static readonly IReadOnlyList<string> SupportedPermissions = [SupportedPermission, "server:read", "notify:email"];

    public const int MaxOpenPaths = 20;
    public const int MaxDescriptionLength = 200;
    public const int MaxPages = 10;
    public const int MaxPageLabelLength = 40;

    /// <summary>Why <paramref name="text"/> is not a valid one-line description, or null.</summary>
    public static string? DescriptionError(string? text, string name)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (string.IsNullOrWhiteSpace(text)) return $"{name} cannot be empty";
        if (text.Any(char.IsControl)) return $"{name} must be one line of plain text";
        if (text.Length > MaxDescriptionLength) return $"{name} can be at most {MaxDescriptionLength} characters";
        if (text.Contains('\n') || text.Contains('\r')) return $"{name} must be one line";
        return null;
    }

    private static void ValidateDescriptors(ApplicationContract contract, ICollection<string> errors)
    {
        if (contract.Kind is not null && contract.Kind is not ("web" or "api"))
            errors.Add("kind must be 'web' or 'api'");
        if (DescriptionError(contract.Description, "description") is { } descriptionError) errors.Add(descriptionError);
        ValidatePages(contract.Pages ?? [], errors);
        if (contract.OpenApi is not null && !IsPlainPath(contract.OpenApi))
            errors.Add("openApi must be a path that starts with / (for example /openapi.json)");
    }

    private static bool IsPlainPath(string? path)
        => !string.IsNullOrEmpty(path) && path.Length is >= 2 and <= 200 && path[0] == '/'
            && !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains("..", StringComparison.Ordinal)
            && path.All(c => c is not ('?' or '#' or '\\') && !char.IsWhiteSpace(c) && !char.IsControl(c));

    private static void ValidatePages(List<ApplicationPageDeclaration> pages, ICollection<string> errors)
    {
        if (pages.Count > MaxPages) errors.Add($"pages can contain at most {MaxPages} entries");
        var index = 0;
        foreach (var page in pages)
        {
            index++;
            if (page is null || !(page.Path == "/" || IsPlainPath(page.Path)))
                errors.Add($"pages entry {index} ('{page?.Path}') needs a path that starts with / and has no spaces or ..");
            if (page is null || string.IsNullOrWhiteSpace(page.Label) || page.Label.Length > MaxPageLabelLength)
                errors.Add($"pages entry {index} needs a label of 1-{MaxPageLabelLength} characters");
        }
    }

    /// <summary>True for <c>/exact/path</c> or <c>/folder/*</c>, using only URL path characters.</summary>
    public static bool IsValidOpenPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 200 || path[0] != '/' || path.Contains("..", StringComparison.Ordinal)
            || path.Contains("//", StringComparison.Ordinal)) return false;
        var body = path.EndsWith("/*", StringComparison.Ordinal) ? path[..^2] : path;
        // "/" and "/*" would open the whole app; a declared path names at least one folder or file.
        return body.Length > 1
            && body.All(c => c is '/' or '-' or '_' or '.' or '~' or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9'));
    }

    private static void ValidateOpenPaths(List<string>? paths, ICollection<string> errors)
    {
        if (paths is null) return;
        if (paths.Count > MaxOpenPaths) errors.Add($"openPaths can contain at most {MaxOpenPaths} paths");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (!IsValidOpenPath(path)) errors.Add($"openPaths entry '{path}' must start with / and be a plain path, optionally ending in /*");
            else if (!seen.Add(path)) errors.Add($"openPaths declares '{path}' more than once");
        }
    }

    private static void ValidatePermissions(List<string>? permissions, ICollection<string> errors)
    {
        if (permissions is null) return;
        if (permissions.Count > 10) errors.Add("permissions can contain at most 10 names");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in permissions)
        {
            if (!SupportedPermissions.Contains(permission)) errors.Add($"permissions contains unsupported permission '{permission}'");
            if (!seen.Add(permission)) errors.Add($"permissions declares '{permission}' more than once");
        }
    }

    private static void ValidateSettings(
        List<ApplicationSettingDeclaration>? settings, List<ApplicationSecretDeclaration>? secrets, ICollection<string> errors)
    {
        if (settings is null) return;
        if (settings.Count > 50) errors.Add("settings can declare at most 50 names");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setting in settings)
        {
            if (setting is null || !ApplicationSecretNames.IsValidName(setting.Name))
            {
                errors.Add("settings[].name must be capital letters, digits and underscores, not starting with a digit or AUTOMATON_, and not the reserved APP_AUTOMATON_API_KEY");
                continue;
            }
            if (!names.Add(setting.Name)) errors.Add($"settings declares {setting.Name} more than once");
            if (secrets?.Any(secret => secret?.Name == setting.Name) == true)
                errors.Add($"settings.{setting.Name} is also declared as a secret");
            if (setting.Description is { } description
                && (description.Length > ApplicationSecretNames.MaxDescriptionLength || description.Any(char.IsControl)))
                errors.Add($"settings.{setting.Name}.description must be one line of at most {ApplicationSecretNames.MaxDescriptionLength} characters");
            if (setting.Default is { Length: > 1000 }) errors.Add($"settings.{setting.Name}.default can be at most 1000 characters");
        }
    }

    private static void ValidateSchedules(List<ApplicationScheduleDeclaration>? schedules, ICollection<string> errors)
    {
        if (schedules is null) return;
        if (schedules.Count > 10) errors.Add("schedules can declare at most 10 entries");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var schedule in schedules)
        {
            if (schedule is null || string.IsNullOrWhiteSpace(schedule.Name) || schedule.Name.Length > 160)
            {
                errors.Add("schedules[].name is required and can be at most 160 characters");
                continue;
            }
            if (!names.Add(schedule.Name)) errors.Add($"schedules declares '{schedule.Name}' more than once");
            try { AppActionManifestParser.ValidateKey(schedule.Action); }
            catch (Exception) { errors.Add($"schedules.{schedule.Name}.action must be a valid Action key"); }
            var hasCron = !string.IsNullOrWhiteSpace(schedule.Cron);
            if (hasCron == schedule.IntervalSeconds.HasValue)
                errors.Add($"schedules.{schedule.Name} needs exactly one of cron or intervalSeconds");
            if (hasCron && schedule.Cron!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 5)
                errors.Add($"schedules.{schedule.Name}.cron must have five fields: minute hour day-of-month month day-of-week");
            if (schedule.IntervalSeconds is < 1)
                errors.Add($"schedules.{schedule.Name}.intervalSeconds must be positive");
            if (schedule.Input is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null) })
                errors.Add($"schedules.{schedule.Name}.input must be an object");
        }
    }

    private static void ValidateSecrets(List<ApplicationSecretDeclaration>? secrets, ICollection<string> errors)
    {
        if (secrets is null) return;
        if (secrets.Count > 50) errors.Add("secrets can declare at most 50 names");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var secret in secrets)
        {
            if (secret is null || !ApplicationSecretNames.IsValidName(secret.Name))
            {
                errors.Add("secrets[].name must be capital letters, digits and underscores, not starting with a digit or AUTOMATON_, and not the reserved APP_AUTOMATON_API_KEY");
                continue;
            }
            if (!names.Add(secret.Name))
                errors.Add($"secrets declares {secret.Name} more than once");
            if (secret.Description is { } description
                && (description.Length > ApplicationSecretNames.MaxDescriptionLength || description.Any(char.IsControl)))
                errors.Add($"secrets.{secret.Name}.description must be one line of at most {ApplicationSecretNames.MaxDescriptionLength} characters");
            if (secret.From is not null && !ApplicationSecretNames.TryParseFrom(secret.From, out _, out _))
                errors.Add($"secrets.{secret.Name}.from must be project:NAME or user:NAME");
        }
    }

    private static void ValidateBuild(ApplicationBuildSpec? build, ICollection<string> errors)
    {
        if (build is null)
        {
            errors.Add("build is required");
            return;
        }

        ValidateRelativePath(build.OutputDirectory, "build.outputDirectory", errors, allowEmpty: true);
        ValidatePositiveRange(build.TimeoutSeconds, "build.timeoutSeconds", 1, 86_400, errors);
    }

    private static void ValidateRuntime(ApplicationRuntimeSpec? runtime, ICollection<string> errors)
    {
        if (runtime is null)
        {
            errors.Add("runtime is required");
            return;
        }

        ValidatePort(runtime.Port, "runtime.port", errors);
        if (runtime.User is not null && !IsValidRuntimeUser(runtime.User))
            errors.Add("runtime.user must be a numeric UID or UID:GID");
    }

    private static bool IsValidRuntimeUser(string value)
    {
        var identities = value.Split(':');
        return identities.Length is 1 or 2
            && identities.All(identity => uint.TryParse(
                identity,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out _));
    }

    private static void ValidateHealth(ApplicationHealthSpec? health, ICollection<string> errors)
    {
        if (health is null)
        {
            errors.Add("health is required");
            return;
        }

        ValidateHealthPath(health.Path, errors);
        if (health.Port.HasValue)
            ValidatePort(health.Port.Value, "health.port", errors);
        ValidatePositiveRange(health.StartupTimeoutSeconds, "health.startupTimeoutSeconds", 1, 86_400, errors);
    }

    private static void ValidateHealthPath(string? path, ICollection<string> errors)
    {
        if (path is not null
            && (string.IsNullOrWhiteSpace(path)
                || !path.StartsWith("/", StringComparison.Ordinal)
                || path.Contains("..", StringComparison.Ordinal)
                || path.Contains('\0')))
        {
            errors.Add("health.path must be an absolute application URL path without traversal");
        }
    }

    private static void ValidateStorage(ApplicationStorageSpec? storage, ICollection<string> errors)
    {
        if (storage is null)
        {
            errors.Add("storage is required");
            return;
        }

        ValidateRelativePath(storage.DataDirectory, "storage.dataDirectory", errors, allowEmpty: false);
        if (storage.Mounts is null)
        {
            errors.Add("storage.mounts must be an array");
            return;
        }

        var environmentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mount in storage.Mounts)
        {
            ValidateMount(mount, environmentNames, errors);
        }
    }

    private static void ValidateCompose(ApplicationContract contract, ICollection<string> errors)
    {
        var compose = contract.Compose;
        if (compose is null) return;
        if (contract.Build?.Command is not null)
            errors.Add("compose deployments cannot declare a build command; Compose builds are deferred");
        if (contract.Runtime?.Command is not null)
            errors.Add("compose deployments cannot also declare runtime.command");
        if (compose.Services is null || compose.Services.Count is < 1 or > 16)
        { errors.Add("compose.services must contain between 1 and 16 services"); return; }
        var services = ValidateComposeServices(compose.Services, errors);
        ValidatePublicComposeService(compose.PublicService, services, errors);
        var volumes = ValidateComposeVolumeNames(compose.Volumes, errors);
        ValidateComposeRelations(services, volumes, errors);
    }

    private static Dictionary<string, ApplicationComposeService> ValidateComposeServices(
        IEnumerable<ApplicationComposeService> source,
        ICollection<string> errors)
    {
        var services = new Dictionary<string, ApplicationComposeService>(StringComparer.Ordinal);
        foreach (var service in source)
        {
            if (service is null || !IsComposeIdentifier(service.Name) || !services.TryAdd(service.Name, service))
            { errors.Add("compose service names must be unique valid identifiers"); continue; }
            ValidateComposeService(service, errors);
        }
        return services;
    }

    private static void ValidateComposeService(ApplicationComposeService service, ICollection<string> errors)
    {
        ValidateImage(service.Image, $"compose.services.{service.Name}.image", errors);
        if (service.Port.HasValue) ValidatePort(service.Port.Value, $"compose.services.{service.Name}.port", errors);
        if (service.Environment is null || service.Environment.Any(pair => HasInvalidEnvironment(pair.Key, pair.Value)))
            errors.Add($"compose.services.{service.Name}.environment contains an invalid name or value");
        if (service.Command is null || service.Entrypoint is null || service.DependsOn is null || service.Mounts is null)
        { errors.Add($"compose.services.{service.Name} contains an invalid collection"); return; }
        if (service.Command.Concat(service.Entrypoint).Any(value => value is null || value.Contains('\0')))
            errors.Add($"compose.services.{service.Name} command contains an invalid value");
        if (service.WorkingDirectory is not null && !IsValidComposeWorkingDirectory(service.WorkingDirectory))
            errors.Add($"compose.services.{service.Name}.workingDirectory must be a valid container path without traversal");
    }

    private static bool HasInvalidEnvironment(string name, string value)
        => string.IsNullOrWhiteSpace(name) || name.Contains('=') || name.Contains('\0')
            || value is null || value.Contains('\0');

    private static void ValidatePublicComposeService(
        string? publicName,
        IReadOnlyDictionary<string, ApplicationComposeService> services,
        ICollection<string> errors)
    {
        if (!IsComposeIdentifier(publicName) || !services.TryGetValue(publicName!, out var service))
            errors.Add("compose.publicService must identify a declared service");
        else if (service.Port is null)
            errors.Add("compose.publicService must declare exactly one container port");
        if (services.Values.Count(item => item.Port.HasValue) != 1)
            errors.Add("compose must declare exactly one externally routed service and container port");
    }

    private static HashSet<string> ValidateComposeVolumeNames(IEnumerable<string>? source, ICollection<string> errors)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (source is null || source.Any(name => !IsComposeIdentifier(name) || !result.Add(name)))
            errors.Add("compose.volumes must contain unique valid managed named volumes");
        return result;
    }

    private static void ValidateComposeRelations(
        IReadOnlyDictionary<string, ApplicationComposeService> services,
        ISet<string> volumes,
        ICollection<string> errors)
    {
        foreach (var service in services.Values)
        {
            ValidateComposeDependencies(service, services, errors);
            ValidateComposeMounts(service, volumes, errors);
        }
    }

    private static void ValidateComposeDependencies(
        ApplicationComposeService service,
        IReadOnlyDictionary<string, ApplicationComposeService> services,
        ICollection<string> errors)
    {
        if (service.DependsOn is null) return;
        foreach (var dependency in service.DependsOn)
            if (!services.ContainsKey(dependency))
                errors.Add($"compose.services.{service.Name}.dependsOn references an undeclared service");
    }

    private static void ValidateComposeMounts(
        ApplicationComposeService service,
        ISet<string> volumes,
        ICollection<string> errors)
    {
        if (service.Mounts is null) return;
        foreach (var mount in service.Mounts)
            if (mount is null || !volumes.Contains(mount.Name) || !IsValidContainerPath(mount.Target))
                errors.Add($"compose.services.{service.Name}.volumes must use declared managed volumes and absolute container targets");
    }

    private static bool IsValidContainerPath(string? target)
        => !string.IsNullOrWhiteSpace(target) && target.StartsWith("/", StringComparison.Ordinal)
            && !target.Contains("..", StringComparison.Ordinal) && !target.Contains('\0');

    private static bool IsValidComposeWorkingDirectory(string value)
        => !string.IsNullOrWhiteSpace(value) && !value.Contains('\0')
            && !LooksLikeWindowsRootedPath(value) && !ContainsTraversalSegment(value);

    private static bool IsComposeIdentifier(string? value) => value is not null
        && value.Length is >= 1 and <= 63
        && (char.IsLetterOrDigit(value[0]) || value[0] == '_')
        && value.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.');

    private static void ValidateMount(
        ApplicationStorageMount? mount,
        ISet<string> environmentNames,
        ICollection<string> errors)
    {
        if (mount is null)
        {
            errors.Add("storage.mounts entries must be objects");
            return;
        }

        if (!IsValidMountName(mount.Name))
            errors.Add("storage.mounts.name must be a valid environment-safe identifier");
        else if (!environmentNames.Add(mount.Name.ToUpperInvariant()))
            errors.Add("storage.mounts.name must be unique after environment-variable normalization");

        if (mount.ReadOnly)
            errors.Add("storage.mounts.readOnly is not supported by this runner");
        ValidateRelativePath(mount.RelativePath, "storage.mounts.relativePath", errors, allowEmpty: false);
    }

    public static void EnsureValid(ApplicationContract contract)
    {
        var errors = Validate(contract);
        if (errors.Count > 0)
            throw new InvalidOperationException($"Invalid application contract: {string.Join("; ", errors)}");
    }

    public static IReadOnlyList<string> ValidateForDeployment(ApplicationContract? contract)
    {
        var errors = Validate(contract).ToList();
        if (contract?.Compose is not null)
            return errors;
        if (contract?.Runtime?.Command is not null && string.IsNullOrWhiteSpace(contract.Runtime.Image))
            errors.Add("runtime.image is required when runtime.command is declared for container deployment");
        if (contract?.Build?.Command is not null
            && string.IsNullOrWhiteSpace(contract.Build.Image)
            && string.IsNullOrWhiteSpace(contract.Runtime?.Image))
            errors.Add("build.image or runtime.image is required when build.command is declared for container deployment");
        return errors;
    }

    public static void EnsureDeployable(ApplicationContract contract)
    {
        var errors = ValidateForDeployment(contract);
        if (errors.Count > 0)
            throw new InvalidOperationException($"Application contract is not deployable: {string.Join("; ", errors)}");
    }

    private static void ValidateCommand(ApplicationCommandSpec? command, string name, ICollection<string> errors)
    {
        if (command is null)
            return;

        ValidateExecutable(command.Executable, name, errors);
        ValidateArguments(command.Arguments, name, errors);
        ValidateRelativePath(command.WorkingDirectory, $"{name}.workingDirectory", errors, allowEmpty: false);
        ValidateEnvironment(command.Environment, name, errors);
    }

    private static void ValidateExecutable(string executable, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(executable) || executable.Contains('\0'))
        {
            errors.Add($"{name}.executable is required and must not contain NUL");
            return;
        }

        if (Path.IsPathRooted(executable)
            || LooksLikeWindowsRootedPath(executable)
            || ContainsTraversalSegment(executable))
        {
            errors.Add($"{name}.executable must not point outside the runner-managed workspace");
        }
    }

    private static void ValidateArguments(ICollection<string>? arguments, string name, ICollection<string> errors)
    {
        if (arguments is null)
        {
            errors.Add($"{name}.arguments must be an array");
            return;
        }

        if (arguments.Any(argument => argument is null || argument.Contains('\0')))
            errors.Add($"{name}.arguments must not contain NUL");
    }

    private static void ValidateEnvironment(IDictionary<string, string>? environment, string name, ICollection<string> errors)
    {
        if (environment is null)
        {
            errors.Add($"{name}.environment must be an object");
        }
        else foreach (var pair in environment)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Contains('=') || pair.Key.Contains('\0'))
                errors.Add($"{name}.environment contains an invalid variable name");
            if (pair.Value is null || pair.Value.Contains('\0'))
                errors.Add($"{name}.environment contains a value with an invalid NUL character");
        }
    }

    private static void ValidateImage(string? image, string name, ICollection<string> errors)
    {
        if (image is null)
            return;

        if (string.IsNullOrWhiteSpace(image) || image.Contains('\0') || image.Any(char.IsWhiteSpace))
            errors.Add($"{name} must be a non-empty OCI image reference without whitespace or NUL");
        else if (image.StartsWith("-", StringComparison.Ordinal)
            || image.Contains('"')
            || image.Contains('\'')
            || image.Contains(';')
            || image.Contains('|')
            || image.Contains('&'))
            errors.Add($"{name} is not a valid OCI image reference");
    }

    private static void ValidateRelativePath(string? value, string name, ICollection<string> errors, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!allowEmpty)
                errors.Add($"{name} is required");
            return;
        }

        if (!ManagedPathBoundary.IsSafeRelative(value, allowEmpty))
        {
            errors.Add($"{name} must remain inside the application workspace");
        }
    }

    private static void ValidatePort(int port, string name, ICollection<string> errors)
    {
        if (port is < 1 or > 65_535)
            errors.Add($"{name} must be between 1 and 65535");
    }

    private static void ValidatePositiveRange(int value, string name, int minimum, int maximum, ICollection<string> errors)
    {
        if (value < minimum || value > maximum)
            errors.Add($"{name} must be between {minimum} and {maximum}");
    }

    private static bool LooksLikeWindowsRootedPath(string value) =>
        value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':';

    private static bool IsValidMountName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && (char.IsLetter(value[0]) || value[0] == '_')
        && value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');

    private static bool ContainsTraversalSegment(string value) =>
        value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == "..");
}
