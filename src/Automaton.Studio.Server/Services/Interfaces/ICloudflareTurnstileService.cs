namespace Automaton.Studio.Server.Services.Interfaces;

public interface ICloudflareTurnstileService
{
    Task<bool> ValidateAsync(string? token, string? remoteIp, CancellationToken cancellationToken);
}
