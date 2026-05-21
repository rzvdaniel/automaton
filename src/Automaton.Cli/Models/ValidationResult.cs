namespace Automaton.Cli.Models;

public static class ValidationCheckNames
{
    public const string AppLink = "appLink";
    public const string FrontendDeployShape = "frontendDeployShape";
    public const string BackendDeployShape = "backendDeployShape";
    public const string BackendRunner = "backendRunner";
    public const string RemoteAppAccess = "remoteAppAccess";
}

public sealed record ValidationCheckResult(string Name, bool Success, string Message, bool IsWarning = false);

public sealed class ValidationResult
{
    public bool Success { get; init; }

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<ValidationCheckResult> Checks { get; init; } = [];

    public IReadOnlyList<string> NextSteps { get; init; } = [];
}

public sealed record RemoteAppValidationInfo(string? ExecutionRunnerId);
