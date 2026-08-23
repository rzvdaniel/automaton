using System.Text.Json;

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
}

public sealed class ApplicationBuildSpec
{
    public ApplicationCommandSpec? Command { get; set; }
    public string? OutputDirectory { get; set; }
    public int TimeoutSeconds { get; set; } = 900;
}

public sealed class ApplicationRuntimeSpec
{
    public ApplicationCommandSpec? Command { get; set; }
    public int Port { get; set; } = 8080;
}

public sealed class ApplicationHealthSpec
{
    public string? Path { get; set; }
    public int? Port { get; set; }
    public int StartupTimeoutSeconds { get; set; } = 60;
}

public sealed class ApplicationStorageSpec
{
    public string DataDirectory { get; set; } = "data";
    public List<ApplicationStorageMount> Mounts { get; set; } = [];
}

public sealed class ApplicationStorageMount
{
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool ReadOnly { get; set; }
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

        if (contract.Build is null)
            errors.Add("build is required");
        else
        {
            ValidateRelativePath(contract.Build.OutputDirectory, "build.outputDirectory", errors, allowEmpty: true);
            ValidatePositiveRange(contract.Build.TimeoutSeconds, "build.timeoutSeconds", 1, 86_400, errors);
        }

        if (contract.Runtime is null)
            errors.Add("runtime is required");
        else
            ValidatePort(contract.Runtime.Port, "runtime.port", errors);

        if (contract.Health is null)
            errors.Add("health is required");
        else
        {
            if (contract.Health.Path is not null
                && (string.IsNullOrWhiteSpace(contract.Health.Path)
                    || !contract.Health.Path.StartsWith("/", StringComparison.Ordinal)
                    || contract.Health.Path.Contains("..", StringComparison.Ordinal)
                    || contract.Health.Path.Contains('\0')))
            {
                errors.Add("health.path must be an absolute application URL path without traversal");
            }

            if (contract.Health.Port.HasValue)
                ValidatePort(contract.Health.Port.Value, "health.port", errors);
            ValidatePositiveRange(contract.Health.StartupTimeoutSeconds, "health.startupTimeoutSeconds", 1, 86_400, errors);
        }

        if (contract.Storage is null)
            errors.Add("storage is required");
        else
        {
            ValidateRelativePath(contract.Storage.DataDirectory, "storage.dataDirectory", errors, allowEmpty: false);
            foreach (var mount in contract.Storage.Mounts ?? [])
            {
                if (string.IsNullOrWhiteSpace(mount.Name) || mount.Name.Contains('\0'))
                    errors.Add("storage.mounts.name is required and must not contain NUL");
                ValidateRelativePath(mount.RelativePath, "storage.mounts.relativePath", errors, allowEmpty: false);
            }
        }

        return errors;
    }

    public static void EnsureValid(ApplicationContract contract)
    {
        var errors = Validate(contract);
        if (errors.Count > 0)
            throw new InvalidOperationException($"Invalid application contract: {string.Join("; ", errors)}");
    }

    private static void ValidateCommand(ApplicationCommandSpec? command, string name, ICollection<string> errors)
    {
        if (command is null)
            return;

        if (string.IsNullOrWhiteSpace(command.Executable) || command.Executable.Contains('\0'))
            errors.Add($"{name}.executable is required and must not contain NUL");
        else if (Path.IsPathRooted(command.Executable) || LooksLikeWindowsRootedPath(command.Executable))
            errors.Add($"{name}.executable must not point outside the runner-managed workspace");

        if (command.Arguments.Any(argument => argument is null || argument.Contains('\0')))
            errors.Add($"{name}.arguments must not contain NUL");

        ValidateRelativePath(command.WorkingDirectory, $"{name}.workingDirectory", errors, allowEmpty: false);
        foreach (var pair in command.Environment ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Contains('=') || pair.Key.Contains('\0'))
                errors.Add($"{name}.environment contains an invalid variable name");
            if (pair.Value is null || pair.Value.Contains('\0'))
                errors.Add($"{name}.environment contains a value with an invalid NUL character");
        }
    }

    private static void ValidateRelativePath(string? value, string name, ICollection<string> errors, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!allowEmpty)
                errors.Add($"{name} is required");
            return;
        }

        var normalized = value.Replace('\\', '/');
        if (normalized.Contains('\0')
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || LooksLikeWindowsRootedPath(normalized)
            || normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
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
}
