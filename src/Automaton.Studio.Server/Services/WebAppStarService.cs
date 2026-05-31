using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

public class WebAppStarService
{
    private readonly ApplicationDbContext _dbContext;

    public WebAppStarService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> AddStarAsync(Guid appId, Guid userId, CancellationToken cancellationToken = default)
    {
        var app = await _dbContext.WebApps
            .FirstOrDefaultAsync(
                a => a.Id == appId && a.IsPublished && a.ReviewStatus == AppReviewStatuses.Approved && a.AccessStatus == AppAccessStatuses.Active,
                cancellationToken);

        if (app == null)
        {
            return false;
        }

        var existing = await _dbContext.WebAppStars.FindAsync([appId, userId], cancellationToken);
        if (existing != null)
        {
            return true;
        }

        _dbContext.WebAppStars.Add(new Entities.WebAppStar
        {
            WebAppId = appId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        });

        app.StarCount++;
        app.Updated = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveStarAsync(Guid appId, Guid userId, CancellationToken cancellationToken = default)
    {
        var star = await _dbContext.WebAppStars.FindAsync([appId, userId], cancellationToken);
        if (star == null)
        {
            return true;
        }

        _dbContext.WebAppStars.Remove(star);

        var app = await _dbContext.WebApps.FirstOrDefaultAsync(a => a.Id == appId, cancellationToken);
        if (app != null && app.StarCount > 0)
        {
            app.StarCount--;
            app.Updated = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<bool> IsStarredAsync(Guid appId, Guid userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.WebAppStars.AnyAsync(x => x.WebAppId == appId && x.UserId == userId, cancellationToken);
    }
}
