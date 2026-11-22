using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Replaces the printed setup token with a new root-only value once the first administrator exists: when setup
/// completes, and once for a server that was set up before the token file was mounted (its printed token may sit in
/// old installer output). The marker keeps later restarts from rotating again.
/// </summary>
public static class SetupTokenRotation
{
    internal const string MarkerKey = "Installation.SetupTokenRotated";

    public static async Task<bool> RotateAsync(ApplicationDbContext db, IConfiguration configuration, CancellationToken token)
    {
        if (!InstallationSetupAccess.Rotate(configuration)) return false;
        if (!await db.Settings.AnyAsync(item => item.Key == MarkerKey, token))
        {
            db.Settings.Add(new Setting { Key = MarkerKey, Value = "true", Description = "The setup token was replaced after setup completed." });
            await db.SaveChangesAsync(token);
        }
        return true;
    }

    /// <summary>True when there is nothing left to do (no token file, setup not complete is retried later).</summary>
    internal static async Task<bool> RotateIfSetupCompleteAsync(ApplicationDbContext db, InstallService install,
        IConfiguration configuration, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(configuration["Bootstrap:TokenFile"])) return true;
        if (await db.Settings.AnyAsync(item => item.Key == MarkerKey, token)) return true;
        if (!await install.InstallationComplete()) return false;
        return await RotateAsync(db, configuration, token);
    }
}

/// <summary>Runs the one-time rotation for servers that finished setup before this existed.</summary>
public sealed class SetupTokenRotationService(IServiceScopeFactory scopeFactory, IConfiguration configuration,
    ILogger<SetupTokenRotationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["Bootstrap:TokenFile"])) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                if (await SetupTokenRotation.RotateIfSetupCompleteAsync(db, scope.ServiceProvider.GetRequiredService<InstallService>(),
                        configuration, stoppingToken)) return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The setup token could not be replaced yet; trying again.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
