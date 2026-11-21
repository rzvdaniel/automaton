namespace Automaton.Api.Entities;

public static class ActionWebhookCallOutcomes
{
    public const string Started = "Started";
    public const string Skipped = "Skipped";
    public const string Disabled = "Disabled";
    public const string AppPaused = "AppPaused";
    public const string Rejected = "Rejected";
    public const string Failed = "Failed";
}

/// <summary>
/// An address outside callers can post to in order to start an app's Action. The caller proves itself with a secret that is
/// stored only as a hash; the Action runs as the user who created the webhook.
/// </summary>
public sealed class ActionWebhook
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string ActionKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>SHA-256 of the secret, lowercase hex. The secret itself is shown once and never stored.</summary>
    public string SecretHash { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LastCalledAtUtc { get; set; }
}

public sealed class ActionWebhookCall
{
    public Guid Id { get; set; }
    public Guid WebhookId { get; set; }
    public DateTime CalledAtUtc { get; set; }
    public string Outcome { get; set; } = ActionWebhookCallOutcomes.Started;
    public Guid? ExecutionId { get; set; }
    public string? Detail { get; set; }
}
