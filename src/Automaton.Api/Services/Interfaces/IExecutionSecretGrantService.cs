using Automaton.Api.Models;

namespace Automaton.Api.Services.Interfaces;

public interface IExecutionSecretGrantService
{
    Task<Guid> CreateGrantAsync(
        Guid secretVersionId,
        SecretGrantScope scope,
        string targetName,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken,
        string deliveryPolicy = SecretDeliveryPolicies.SingleDelivery);

    Task<ExecutionSecretLease> LeaseAsync(
        SecretGrantScope scope,
        IReadOnlyCollection<Guid> grantIds,
        CancellationToken cancellationToken);

    Task MarkConsumedAsync(
        IReadOnlyCollection<Guid> grantIds,
        CancellationToken cancellationToken);

    Task RevokeByVersionAsync(Guid secretVersionId, CancellationToken cancellationToken);
}
