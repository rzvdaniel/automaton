using Automaton.Api.Enums;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

internal sealed class AppDeletionRecoveryFixture : IAsyncDisposable
{
    private readonly string filesRoot = Path.Combine(Path.GetTempPath(), "deletion-recovery-" + Guid.NewGuid());
    private AppDeletionService? deletion;
    internal required AppDeploymentTargetManagementServiceTests.TestFixtureContext Context { get; init; }

    internal static async Task<AppDeletionRecoveryFixture> CreateAsync()
    {
        var context = await AppDeploymentTargetManagementServiceTests.TestFixtureContext.CreateAsync(successfulCleanup: true);
        var runner = context.AddRunner(online: true);
        context.App.DeletionClaimId = Guid.NewGuid();
        var target = await context.Db.AppDeploymentTargets.SingleAsync(t => t.WebAppId == context.App.Id);
        target.ExecutionRunnerId = runner.Id;
        target.Status = DeploymentTargetStatus.Deploying;
        target.CleanupPending = true;
        await context.Db.SaveChangesAsync();
        var fixture = new AppDeletionRecoveryFixture { Context = context };
        AppAuthorizationTestServiceFactory.CreateAppsService(context.Db, context.UserId, fixture.filesRoot, out var storage);
        fixture.deletion = new AppDeletionService(context.Db, storage, context.Service);
        return fixture;
    }

    internal async Task DeleteAsync(bool recover)
    {
        var app = await Context.Db.WebApps.SingleAsync(a => a.Id == Context.App.Id);
        await deletion!.DeleteResourcesAsync(app.Id, app, Context.UserId, recover);
    }

    internal void ProbeAdmissionDuringCleanup()
        => Context.Commands.BeforeSend = async () =>
        {
            await using var replica = new ApplicationDbContext(Context.ContextOptions);
            await Assert.ThatAsync(() => new AppDeletionClaimService(replica)
                .OpenAdmissionAsync(Context.App.Id, CancellationToken.None),
                Throws.TypeOf<AppDeploymentTargetConflictException>());
        };

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        if (Directory.Exists(filesRoot)) Directory.Delete(filesRoot, recursive: true);
    }
}
