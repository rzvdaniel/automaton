using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Exceptions;
using Automaton.Studio.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

public class SubscriptionGatewayService : ISubscriptionGatewayService
{
    private readonly ApplicationDbContext _db;

    public SubscriptionGatewayService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<(bool canCreateProjects, bool canCreateOrganizations)> GetPlanFeaturesAsync(Guid userId)
    {
        // Try user's assigned plan first
        var allowance = await _db.UserTokenAllowances
            .AsNoTracking()
            .Include(a => a.SubscriptionPlan)
            .FirstOrDefaultAsync(a => a.UserId == userId);

        if (allowance?.SubscriptionPlan != null)
            return (allowance.SubscriptionPlan.CanCreateProjects, allowance.SubscriptionPlan.CanCreateOrganizations);

        // Fall back to the default plan
        var defaultPlan = await _db.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsDefault);

        if (defaultPlan != null)
            return (defaultPlan.CanCreateProjects, defaultPlan.CanCreateOrganizations);

        // No plans configured — allow everything (dev/fresh install)
        return (true, true);
    }

    public async Task EnsureCanCreateProjectAsync(Guid userId)
    {
        var (canCreate, _) = await GetPlanFeaturesAsync(userId);
        if (!canCreate)
            throw new PlanLimitException("CanCreateProjects", "Starter");
    }

    public async Task EnsureCanCreateOrganizationAsync(Guid userId)
    {
        var (_, canCreate) = await GetPlanFeaturesAsync(userId);
        if (!canCreate)
            throw new PlanLimitException("CanCreateOrganizations", "Pro");
    }
}
