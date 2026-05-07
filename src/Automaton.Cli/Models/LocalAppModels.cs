namespace Automaton.Cli.Models;

public sealed class LocalAppContext
{
    public required string RootPath { get; init; }
    public required string FrontendPath { get; init; }
    public required string IndexHtmlPath { get; init; }
    public required string BackendPath { get; init; }
    public required string MocksPath { get; init; }
    public required string DataPath { get; init; }
    public required string DisplayName { get; init; }
    public AutomatonConfig? Config { get; init; }

    public bool HasBackendDirectory => Directory.Exists(BackendPath);
    public bool HasMocksDirectory => Directory.Exists(MocksPath);
}

public sealed class LocalAppValidationCheck
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public required string Message { get; init; }
    public bool Success => Status != LocalAppValidationStatuses.Fail;
}

public sealed class LocalAppValidationResult
{
    public required LocalAppContext Context { get; init; }
    public required IReadOnlyList<LocalAppValidationCheck> Checks { get; init; }

    public bool Success => Checks.All(check => check.Status != LocalAppValidationStatuses.Fail);
}

public sealed class PythonRuntimeInfo
{
    public required string ExecutablePath { get; init; }
    public required string Source { get; init; }
    public string? Version { get; init; }
}

public sealed class LocalAppExecutionOptions
{
    public required string ControllerName { get; init; }
    public required string ActionName { get; init; }
    public required string InputsJson { get; init; }
    public required string Mode { get; init; }
    public required TimeSpan Timeout { get; init; }
    public bool Verbose { get; init; }
}

public sealed class LocalAppDevServerOptions
{
    public required string Host { get; init; }
    public int? Port { get; init; }
    public required string Mode { get; init; }
    public bool Verbose { get; init; }
    public bool OpenBrowser { get; init; }
}

public sealed class LocalAppExecutionResult
{
    public required string ControllerName { get; set; }
    public required string ActionName { get; set; }
    public required string Mode { get; set; }
    public required TimeSpan Duration { get; set; }
    public required bool Success { get; set; }
    public required object? Data { get; set; }
    public required string Error { get; set; }
    public string? FailureReason { get; set; }
    public string? Traceback { get; set; }
    public string? StdOut { get; set; }
    public string? StdErr { get; set; }
    public string? MockPath { get; set; }
    public string? ControllerPath { get; set; }
    public PythonRuntimeInfo? PythonRuntime { get; set; }
}

public static class LocalAppExecutionModes
{
    public const string Native = "native";
    public const string MockOnly = "mock-only";
    public const string MockFallback = "mock-fallback";
}

public static class LocalAppValidationStatuses
{
    public const string Pass = "pass";
    public const string Warning = "warning";
    public const string Fail = "fail";
}

public static class LocalAppFailureReasons
{
    public const string ControllerNotFound = "controller_not_found";
    public const string ImportError = "import_error";
    public const string MissingHandle = "missing_handle";
    public const string RuntimeException = "runtime_exception";
    public const string MissingDependency = "missing_dependency";
    public const string MalformedResponse = "malformed_response";
    public const string Timeout = "timeout";
    public const string MockNotFound = "mock_not_found";
    public const string InvalidInput = "invalid_input";
    public const string PythonNotAvailable = "python_not_available";
}
