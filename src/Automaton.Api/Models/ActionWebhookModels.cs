namespace Automaton.Api.Models;

public sealed class CreateActionWebhookRequest
{
    public string ActionKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed record ActionWebhookCallView(DateTime CalledAtUtc, string Outcome, Guid? ExecutionId, string? Detail);

public sealed record ActionWebhookView(
    Guid Id, Guid ApplicationId, string ActionKey, string Name, bool Enabled,
    DateTime CreatedAtUtc, DateTime? LastCalledAtUtc, string Url, IReadOnlyList<ActionWebhookCallView> Calls,
    string? Secret = null);

/// <summary>What a webhook call answers. The message never contains the secret or any other webhook's data.</summary>
public sealed record ActionWebhookCallResult(int StatusCode, string Message, Guid? ExecutionId = null);
