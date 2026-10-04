namespace Automaton.Runner.Console.Services;

// Identity handshake used by retained API revisions during tunnel rollback.
internal sealed class WelcomeRunnerMessage
{
    public string Message { get; set; } = string.Empty;
    public string ApiInstanceId { get; set; } = string.Empty;
    public string? ApiColor { get; set; }
}
