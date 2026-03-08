using Automaton.Studio.Server.Exceptions;

namespace Automaton.Studio.Server.Services.Interfaces;

public interface ISubscriptionGatewayService
{
    /// <summary>Returns the active subscription plan for a user. Falls back to the default plan.</summary>
    Task<(bool canCreateProjects, bool canCreateOrganizations)> GetPlanFeaturesAsync(Guid userId);

    /// <summary>Throws <see cref="PlanLimitException"/> if the user cannot create projects.</summary>
    Task EnsureCanCreateProjectAsync(Guid userId);

    /// <summary>Throws <see cref="PlanLimitException"/> if the user cannot create organizations.</summary>
    Task EnsureCanCreateOrganizationAsync(Guid userId);
}
