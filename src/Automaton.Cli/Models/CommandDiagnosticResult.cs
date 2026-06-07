namespace Automaton.Cli.Models;

public static class CommandDiagnosticCheckNames
{
    public const string AppLink = "appLink";
    public const string LocalScaffold = "localScaffold";
    public const string AiGuidance = "aiGuidance";
    public const string Credentials = "credentials";
    public const string RemoteAccess = "remoteAccess";
    public const string Authentication = "authentication";
    public const string UserProfile = "userProfile";
    public const string Logout = "logout";
    public const string CreateApp = "createApp";
    public const string LinkApp = "linkApp";
    public const string Publish = "publish";
    public const string PublishApproval = "publishApproval";
    public const string RunnerLookup = "runnerLookup";
    public const string RunnerAssignment = "runnerAssignment";
    public const string Runners = "runners";
    public const string Docker = "docker";
    public const string RunnerApiKey = "runnerApiKey";
    public const string RunnerStart = "runnerStart";
    public const string RunnerOnline = "runnerOnline";
}

public sealed record CommandDiagnosticCheck(string Name, bool Success, string Message, bool IsWarning = false);

public sealed class CommandDiagnosticResult
{
    public bool Success { get; init; }

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<CommandDiagnosticCheck> Checks { get; init; } = [];

    public IReadOnlyList<string> NextSteps { get; init; } = [];
}
