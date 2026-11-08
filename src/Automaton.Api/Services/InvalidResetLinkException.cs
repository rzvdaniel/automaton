namespace Automaton.Api.Services;

/// <summary>The password reset link cannot be used. Reason is "used", "expired" or "invalid".</summary>
public sealed class InvalidResetLinkException(string reason) : InvalidOperationException("The password reset link is no longer valid.")
{
    public string Reason { get; } = reason;
}
