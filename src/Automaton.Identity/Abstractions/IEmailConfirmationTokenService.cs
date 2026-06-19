namespace Automaton.Identity.Abstractions;

public interface IEmailConfirmationTokenService
{
    Task<string> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken = default);
}
