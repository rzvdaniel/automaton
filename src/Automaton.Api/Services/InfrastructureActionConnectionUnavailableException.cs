namespace Automaton.Api.Services;

/// <summary>
/// Indicates that a trusted infrastructure action could not be dispatched because
/// the persisted SignalR connection disappeared before execution was claimed.
/// The CAP delivery must be retried after the Runner reconnects.
/// </summary>
public sealed class InfrastructureActionConnectionUnavailableException(Guid operationId, string connectionId, Exception innerException)
    : InvalidOperationException(
        $"Infrastructure Action operation '{operationId}' could not reach connection '{connectionId}'; retry after the Runner reconnects.",
        innerException);
