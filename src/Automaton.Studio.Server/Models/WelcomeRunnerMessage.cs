namespace Automaton.Api.Models;

public class WelcomeRunnerMessage
{
    public string Message { get; set; } = string.Empty;
    public string ApiInstanceId { get; set; } = string.Empty;
    public string? ApiColor { get; set; }
}
