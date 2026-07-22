using Automaton.Api.Models;

namespace Automaton.Api.Services.Interfaces;

public interface ISecretStore
{
    Task<SecretVersionCreatedResult> CreateVersionAsync(
        SecretOwner owner,
        string name,
        string plaintext,
        DateTime? expiresAtUtc,
        CancellationToken cancellationToken);

    Task RevokeVersionAsync(Guid secretVersionId, CancellationToken cancellationToken);
}
