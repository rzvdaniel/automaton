using Automaton.Ai.Models;

namespace Automaton.Ai.Providers;

/// <summary>
/// Public Anthropic provider boundary; delegates conversation behavior to its workflow.
/// </summary>
public sealed class AnthropicProvider : IAiProvider
{
    private readonly AnthropicConversationWorkflow _workflow;

    public AnthropicProvider(Microsoft.Extensions.Logging.ILogger logger)
    {
        _workflow = new AnthropicConversationWorkflow(logger, updated => ConversationUpdated?.Invoke(this, updated));
    }

    public string ProviderName => "Anthropic";

    public string DefaultModel => _workflow.DefaultModel;

    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    public Task<ProviderResult> CallAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken) =>
        _workflow.CallAsync(request, tools, executeToolAsync, cancellationToken);
}
