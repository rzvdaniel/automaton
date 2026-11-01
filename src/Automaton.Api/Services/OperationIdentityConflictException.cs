namespace Automaton.Api.Services;

/// <summary>
/// An operation ID was reused by its owner for a different request. Permanent: retrying the same
/// request cannot succeed, so the API answers 409 Conflict rather than a retryable server error.
/// </summary>
public sealed class OperationIdentityConflictException(string message) : InvalidOperationException(message);
