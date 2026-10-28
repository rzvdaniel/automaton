using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Collections;
using System.Text.Json;

namespace Automaton.Core.Models;

public static class AppActionRuntimes
{
    public const string Python = "python";
    public const string Bash = "bash";
}

public static class AppActionCapabilities
{
    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public static class AppActionExecutionScopes
{
    public const string Worker = "Worker";
    public const string AppRunner = "AppRunner";
    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        Worker, AppRunner
    };
}

public static class AppActionConcurrencyPolicies
{
    public const string Allow = "Allow";
    public const string SkipIfRunning = "SkipIfRunning";
    public const string Queue = "Queue";
    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        Allow, SkipIfRunning, Queue
    };
}

public sealed class AppActionManifest
{
    public string ApiVersion { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public AppActionMetadata Metadata { get; set; } = new();
    public AppActionSpec Spec { get; set; } = new();
}

public sealed class AppActionMetadata
{
    public string? Name { get; set; }
    public string? Key { get; set; }
    public string? Description { get; set; }
}

public sealed class AppActionSpec
{
    public string Runtime { get; set; } = string.Empty;
    public string Entrypoint { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 120;
    public Dictionary<string, object?>? Input { get; set; }
    public Dictionary<string, object?>? Output { get; set; }
    public List<string> Capabilities { get; set; } = [];
    public bool Schedulable { get; set; }
    public List<string> AllowedExecutionScopes { get; set; } = [];
    public string DefaultConcurrencyPolicy { get; set; } = "SkipIfRunning";
    public int MinimumIntervalSeconds { get; set; } = 300;
}

public sealed record DiscoveredAppAction(
    string Key,
    string DirectoryPath,
    string ManifestPath,
    AppActionManifest Manifest);

public static class AppActionManifestParser
{
    public const int MinimumTimeoutSeconds = 1;
    public const int MaximumTimeoutSeconds = 3600;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static AppActionManifest Parse(string yaml, string actionKey)
    {
        AppActionManifest manifest;
        try
        {
            manifest = Deserializer.Deserialize<AppActionManifest>(yaml)
                ?? throw new InvalidOperationException("Action manifest is empty.");
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new InvalidOperationException($"Action manifest YAML is invalid: {ex.Message}", ex);
        }

        Validate(manifest, actionKey);
        manifest.Metadata.Key ??= actionKey;
        return manifest;
    }

    public static AppActionManifest ParseFile(string manifestPath, string actionKey)
        => Parse(File.ReadAllText(manifestPath), actionKey);

    public static IReadOnlyList<DiscoveredAppAction> Discover(string appSourceRoot)
    {
        var actionsRoot = Path.Combine(appSourceRoot, "actions");
        if (!Directory.Exists(actionsRoot))
        {
            return [];
        }

        var actions = new List<DiscoveredAppAction>();
        foreach (var directory in Directory.GetDirectories(actionsRoot).OrderBy(path => path, StringComparer.Ordinal))
        {
            var key = Path.GetFileName(directory);
            var manifestPath = Path.Combine(directory, "action.yaml");
            if (!File.Exists(manifestPath))
            {
                continue;
            }
            ValidateKey(key);

            actions.Add(new DiscoveredAppAction(
                key,
                directory,
                manifestPath,
                ParseFile(manifestPath, key)));
        }

        return actions;
    }

    private static void Validate(AppActionManifest manifest, string actionKey)
    {
        ValidateKey(actionKey);
        ValidateIdentity(manifest, actionKey);
        ValidateExecutionSettings(manifest.Spec);
        ValidateCapabilities(manifest.Spec);
        ValidateScheduling(manifest.Spec);
    }

    private static void ValidateIdentity(AppActionManifest manifest, string actionKey)
    {
        if (!string.Equals(manifest.ApiVersion, "automaton.dev/v1", StringComparison.Ordinal))
            throw new InvalidOperationException("Action apiVersion must be 'automaton.dev/v1'.");
        if (!string.Equals(manifest.Kind, "Action", StringComparison.Ordinal))
            throw new InvalidOperationException("Action kind must be 'Action'.");
        if (!string.IsNullOrWhiteSpace(manifest.Metadata.Key)
            && !string.Equals(manifest.Metadata.Key, actionKey, StringComparison.Ordinal))
            throw new InvalidOperationException($"Action metadata.key must match its folder name '{actionKey}'.");
    }

    private static void ValidateExecutionSettings(AppActionSpec spec)
    {
        if (spec.Runtime is not (AppActionRuntimes.Python or AppActionRuntimes.Bash))
            throw new InvalidOperationException("Action runtime must be 'python' or 'bash'.");
        if (spec.TimeoutSeconds is < MinimumTimeoutSeconds or > MaximumTimeoutSeconds)
            throw new InvalidOperationException($"Action timeoutSeconds must be between {MinimumTimeoutSeconds} and {MaximumTimeoutSeconds}.");

        ValidateRelativePath(spec.Entrypoint, "entrypoint");
    }

    private static void ValidateCapabilities(AppActionSpec spec)
    {
        var unsupportedCapability = spec.Capabilities.FirstOrDefault(capability => !AppActionCapabilities.Supported.Contains(capability));
        if (unsupportedCapability != null)
            throw new InvalidOperationException($"Action capability '{unsupportedCapability}' is not supported.");
    }

    private static void ValidateScheduling(AppActionSpec spec)
    {
        if (spec.MinimumIntervalSeconds < 1)
            throw new InvalidOperationException("Action minimumIntervalSeconds must be positive.");
        if (!AppActionConcurrencyPolicies.Supported.Contains(spec.DefaultConcurrencyPolicy))
            throw new InvalidOperationException("Action defaultConcurrencyPolicy is invalid.");
        if (spec.AllowedExecutionScopes.Any(scope => !AppActionExecutionScopes.Supported.Contains(scope)))
            throw new InvalidOperationException("Action allowedExecutionScopes contains an invalid scope.");
        if (spec.Schedulable && spec.AllowedExecutionScopes.Count == 0)
            throw new InvalidOperationException("Schedulable Actions must declare allowedExecutionScopes.");
    }

    public static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)
            || key.Length > 64
            || key[0] is < 'a' or > 'z'
            || key.Any(character => !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            || key.EndsWith('-'))
        {
            throw new InvalidOperationException("Action keys must use 1-64 lowercase letters, digits, and internal hyphens.");
        }
    }

    public static void ValidateRelativePath(string path, string name)
    {
        var normalized = path?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized)
            || Path.IsPathRooted(path!)
            || normalized.StartsWith('/')
            || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidOperationException($"Action {name} must be a safe relative path inside the Action folder.");
        }
    }
}

public static class AppActionSchemaValidator
{
    public static string? Validate(JsonElement value, Dictionary<string, object?>? schema, string path = "$")
    {
        if (schema == null)
            return null;

        var type = GetString(schema, "type");
        if (type != null && !MatchesType(value, type))
            return $"{path} must be of type {type}.";

        return type switch
        {
            "object" when value.ValueKind == JsonValueKind.Object => ValidateObject(value, schema, path),
            "array" when value.ValueKind == JsonValueKind.Array => ValidateArray(value, schema, path),
            _ => null
        };
    }

    private static string? ValidateObject(JsonElement value, Dictionary<string, object?> schema, string path)
    {
        foreach (var name in GetList(schema, "required")
                     .Select(item => item?.ToString())
                     .Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (!value.TryGetProperty(name!, out _))
                return $"{path}.{name} is required.";
        }

        var properties = GetMap(schema, "properties");
        if (properties == null)
            return null;

        foreach (var property in properties)
        {
            if (!value.TryGetProperty(property.Key, out var propertyValue))
                continue;

            var error = Validate(propertyValue, ToStringMap(property.Value), $"{path}.{property.Key}");
            if (error != null)
                return error;
        }

        return null;
    }

    private static string? ValidateArray(JsonElement value, Dictionary<string, object?> schema, string path)
    {
        var itemSchema = ToStringMap(GetValue(schema, "items"));
        if (itemSchema == null)
            return null;

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var error = Validate(item, itemSchema, $"{path}[{index++}]");
            if (error != null)
                return error;
        }

        return null;
    }

    private static bool MatchesType(JsonElement value, string type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => true
    };

    private static string? GetString(Dictionary<string, object?> map, string key) => GetValue(map, key)?.ToString();
    private static object? GetValue(Dictionary<string, object?> map, string key)
        => map.TryGetValue(key, out var value) ? value : null;
    private static Dictionary<string, object?>? GetMap(Dictionary<string, object?> map, string key) => ToStringMap(GetValue(map, key));
    private static IEnumerable<object?> GetList(Dictionary<string, object?> map, string key)
        => GetValue(map, key) is IEnumerable values and not string ? values.Cast<object?>() : [];

    private static Dictionary<string, object?>? ToStringMap(object? value)
    {
        if (value is Dictionary<string, object?> typed) return typed;
        if (value is not IDictionary dictionary) return null;
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
            if (entry.Key?.ToString() is { } key) result[key] = entry.Value;
        return result;
    }
}
