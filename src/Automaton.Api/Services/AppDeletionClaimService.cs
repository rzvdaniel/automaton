using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Automaton.Api.Services;

/// <summary>Serializes app admission with a durable, owner-fenced deletion claim.</summary>
public sealed class AppDeletionClaimService(ApplicationDbContext db)
{
    private const string Conflict = "The app is being deleted. New targets and deployments are not allowed.";

    public async Task<Guid> AcquireAsync(Guid appId, CancellationToken token = default)
    {
        var id = Guid.NewGuid();
        if (db.Database.IsRelational())
        {
            var changed = await db.WebApps.Where(app => app.Id == appId && app.DeletionClaimId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(app => app.DeletionClaimId, id), token);
            if (changed != 1) throw new AppDeploymentTargetConflictException(Conflict);
        }
        else
        {
            var app = await db.WebApps.SingleAsync(app => app.Id == appId, token);
            if (app.DeletionClaimId.HasValue) throw new AppDeploymentTargetConflictException(Conflict);
            app.DeletionClaimId = id;
            await db.SaveChangesAsync(token);
        }
        return id;
    }

    public async Task ReleaseAsync(Guid appId, Guid id, CancellationToken token = default)
    {
        if (db.Database.IsRelational())
            await db.WebApps.Where(app => app.Id == appId && app.DeletionClaimId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(app => app.DeletionClaimId, (Guid?)null), token);
        else
        {
            var app = await db.WebApps.SingleOrDefaultAsync(app => app.Id == appId, token);
            if (app?.DeletionClaimId != id) return;
            app.DeletionClaimId = null;
            await db.SaveChangesAsync(token);
        }
    }

    public async Task<Guid> RecoverAsync(AppDeletionExecutionLock owner, CancellationToken token = default)
    {
        await owner.AssertOwnedAsync(token);
        var previous = await db.WebApps.AsNoTracking().Where(a => a.Id == owner.AppId)
            .Select(a => a.DeletionClaimId).SingleAsync(token);
        if (previous is null) return await AcquireAsync(owner.AppId, token);
        var id = Guid.NewGuid();
        if (db.Database.IsRelational())
        {
            var changed = await db.WebApps.Where(a => a.Id == owner.AppId && a.DeletionClaimId == previous)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.DeletionClaimId, id), token);
            if (changed != 1) throw new AppDeploymentTargetConflictException(Conflict);
        }
        else
        {
            var app = await db.WebApps.SingleAsync(a => a.Id == owner.AppId, token);
            if (app.DeletionClaimId != previous) throw new AppDeploymentTargetConflictException(Conflict);
            app.DeletionClaimId = id;
            await db.SaveChangesAsync(token);
        }
        return id;
    }

    public async Task AssertOwnedAsync(AppDeletionExecutionLock owner, Guid claim, CancellationToken token)
    {
        await owner.AssertOwnedAsync(token);
        if (!await db.WebApps.AsNoTracking().AnyAsync(a => a.Id == owner.AppId && a.DeletionClaimId == claim, token))
            throw new AppDeploymentTargetConflictException(Conflict);
    }

    public async Task<IDbContextTransaction?> OpenAdmissionAsync(Guid appId, CancellationToken token)
    {
        var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(token) : null;
        try
        {
            // A conditional no-op UPDATE locks the app row until admission commits.
            // The claim UPDATE waits for this lock, then sees the admitted target/deployment.
            var allowed = db.Database.IsRelational()
                ? await db.WebApps.Where(app => app.Id == appId && app.DeletionClaimId == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(app => app.DeletionClaimId, (Guid?)null), token) == 1
                : await db.WebApps.AsNoTracking().AnyAsync(app => app.Id == appId && app.DeletionClaimId == null, token);
            if (!allowed) throw new AppDeploymentTargetConflictException(Conflict);
            return transaction;
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            throw;
        }
    }
}
