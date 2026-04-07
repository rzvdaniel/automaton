using System.Threading;

namespace Automaton.Mcp.Tools;

public interface IUiAutomationActionAckService
{
    Task<string?> WaitForResultAsync(string toolCallId, TimeSpan timeout, CancellationToken cancellationToken = default);
    void SubmitResult(string toolCallId, string resultJson);
}
