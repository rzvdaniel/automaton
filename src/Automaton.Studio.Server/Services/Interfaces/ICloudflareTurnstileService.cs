namespace Automaton.Api.Services.Interfaces;

public interface ICloudflareTurnstileService
{
    Task<bool> ValidateAsync(string? token, string? remoteIp, CancellationToken cancellationToken);
}
