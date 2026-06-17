namespace Automaton.Api.Services;

public sealed record HostedRunnerRemoteExecutionResult(
    bool Succeeded,
    int ExitStatus,
    string StandardOutput,
    string StandardError)
{
    public string? ContainerId => StandardOutput
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .LastOrDefault();
}
