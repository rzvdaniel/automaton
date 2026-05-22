namespace Automaton.Cli.Models;

public static class DeployCheckNames
{
    public const string AppLink = "appLink";
    public const string Credentials = "credentials";
    public const string RemoteAppAccess = "remoteAppAccess";
    public const string SourceUpload = "sourceUpload";
    public const string FrontendPackage = "frontendPackage";
    public const string Build = "build";
    public const string RunnerLookup = "runnerLookup";
    public const string RunnerAssignment = "runnerAssignment";
    public const string Upload = "upload";
    public const string BackendPackage = "backendPackage";
    public const string BackendDeploy = "backendDeploy";
}

public sealed record DeployCheckResult(string Name, bool Success, string Message);

public sealed record DeploySkippedEntry(string Path, string Reason, string RuleSource);

public sealed record DeployUploadResult(
    int Written,
    int Skipped,
    double SizeMb,
    IReadOnlyList<DeploySkippedEntry>? SkippedEntries = null);

public sealed record DeployBackendResult(bool Success, int RunnerCount, string? Message);

public sealed class DeployResult
{
    public bool Success { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string? AppId { get; init; }

    public string? AppName { get; init; }

    public string? DeployDirectory { get; init; }

    public bool Clean { get; init; }

    public bool SkipBackend { get; init; }

    public IReadOnlyList<DeployCheckResult> Checks { get; init; } = [];

    public IReadOnlyList<string> NextSteps { get; init; } = [];

    public DeployUploadResult? SourceUploaded { get; init; }

    public DeployUploadResult? Uploaded { get; init; }

    public DeployBackendResult? Backend { get; init; }
}
