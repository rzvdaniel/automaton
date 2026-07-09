namespace Automaton.Worker.Services;

public sealed record HostedRunnerRemoteExecutionResult(
    bool Succeeded,
    int ExitStatus,
    string StandardOutput,
    string StandardError)
{
    public string? ContainerId
    {
        get
        {
            if (string.IsNullOrWhiteSpace(StandardOutput))
            {
                return null;
            }

            var lines = StandardOutput
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return lines.LastOrDefault();
        }
    }
}
