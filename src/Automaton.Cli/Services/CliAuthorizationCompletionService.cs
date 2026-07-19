using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

/// <summary>
/// Completes CLI authorization delivery independently of the login command so
/// a normal CLI invocation can recover from an interrupted acknowledgement.
/// </summary>
public sealed class CliAuthorizationCompletionService
{
    private readonly ConfigService config;

    public CliAuthorizationCompletionService(ConfigService config)
    {
        this.config = config;
    }

    public async Task<bool> TryCompletePendingAsync(string directory, bool global = false, CancellationToken cancellationToken = default)
    {
        var resolved = global
            ? CreateGlobalContext(config.LoadGlobalConfig())
            : config.ResolveContext(directory: directory);
        var pending = resolved.PendingAuthorizationCompletion;
        if (pending is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(resolved.ApiKey))
        {
            return false;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var client = new AutomatonApiClient(pending.Server);
                await client.CompleteCliAuthorizationAsync(pending.RequestId, pending.Verifier, cancellationToken);
                config.ClearPendingAuthorizationCompletion(directory, resolved);
                return true;
            }
            catch when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static ResolvedCliContext CreateGlobalContext(GlobalConfig stored)
        => new()
        {
            Server = stored.Server,
            ApiKey = stored.ApiKey,
            CredentialSource = CliCredentialSource.Global,
            PendingAuthorizationCompletion = stored.PendingAuthorizationCompletion
        };
}
