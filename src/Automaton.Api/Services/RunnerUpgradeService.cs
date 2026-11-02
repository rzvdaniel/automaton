using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

/// <summary>Gives a Runner's owner the host command that moves it to the server's configured Runner image.</summary>
public sealed class RunnerUpgradeService(
    ApplicationDbContext db,
    ResourceAuthorizationService authorization,
    IOptions<HostedRunnerOptions> hostedRunnerOptions)
{
    public async Task<RunnerUpgradeInstructions> PrepareAsync(Guid id, CancellationToken token)
    {
        // Upgrading replaces the Runner's software without changing who may use it, so the owner may
        // upgrade a Runner that projects share; deleting it still requires removing it from them first.
        if (!await authorization.CanUpdateRunnerAsync(id, token))
            throw new UnauthorizedAccessException($"You do not have permission to manage runner {id}");
        var runner = await db.Runners.AsNoTracking().SingleAsync(runner => runner.Id == id, token);
        var image = hostedRunnerOptions.Value.DockerImage;
        var upToDate = string.Equals(runner.ImageDigest, image, StringComparison.Ordinal);
        return new(id, image, upToDate, RunnerUpgradeCommand.Build(id, runner.InstallationId, image, runner.Name));
    }
}
