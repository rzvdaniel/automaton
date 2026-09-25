using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Services;

public sealed class HostedRunnerPlanCapabilityValidator(
    IHostedRunnerSpecificationResolver specificationResolver)
{
    public async Task<string?> ValidateAsync(PlanCapabilities capabilities, CancellationToken cancellationToken)
    {
        var capability = capabilities.HostedRunner;
        if (!capability.Enabled)
        {
            return null;
        }

        var resolution = await specificationResolver.ValidateAsync(capability, cancellationToken);
        return resolution.Failure switch
        {
            HostedRunnerSpecificationResolutionFailure.SpecificationNotConfigured => "A hosted runner specification is required when hosted runners are enabled.",
            HostedRunnerSpecificationResolutionFailure.SpecificationNotFound => "The selected hosted runner specification does not exist.",
            HostedRunnerSpecificationResolutionFailure.SpecificationInactive => "The selected hosted runner specification is inactive.",
            _ => null
        };
    }
}
