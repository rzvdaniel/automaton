using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public interface IAppRunnerAuthorizationPolicy
{
    Task<Runner> AuthorizeActionExecutionAsync(
        WebApp app,
        Guid runnerId,
        Guid userId,
        CancellationToken cancellationToken = default) => AuthorizeAsync(app, runnerId, userId, cancellationToken);

    Task<Runner> AuthorizeAsync(
        WebApp app,
        Guid runnerId,
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed class AppRunnerAuthorizationPolicy(
    ApplicationDbContext dbContext,
    ResourceAuthorizationService authorizationService) : IAppRunnerAuthorizationPolicy
{
    public async Task<Runner> AuthorizeActionExecutionAsync(
        WebApp app,
        Guid runnerId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (await authorizationService.CanAccessAppAsync(app.Id, userId, cancellationToken))
        {
            return await AuthorizeAsync(app, runnerId, userId, cancellationToken);
        }

        if (!await authorizationService.CanExecuteAssignedOrganizationAppAsync(app.Id, userId, cancellationToken))
        {
            throw new UnauthorizedAccessException("You do not have permission to execute Actions in this App.");
        }

        var runner = await dbContext.Runners.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == runnerId, cancellationToken)
            ?? throw new InvalidOperationException("Execution Runner not found.");
        if (RunnerPurposes.Normalize(runner.Purpose) != RunnerPurposes.Managed
            || RunnerApplicationProfiles.Normalize(runner.ApplicationProfile) != RunnerApplicationProfiles.MachineManagement
            || !RunnerCapabilities.CanExecuteApplications(runner.CapabilitiesJson, runner.Purpose)
            || !RunnerCapabilities.Includes(runner.CapabilitiesJson, RunnerCapabilities.WorkflowExecution, runner.Purpose))
        {
            throw new UnauthorizedAccessException("Assigned Action access is limited to isolated Machine Management Runner execution.");
        }

        return runner;
    }

    public async Task<Runner> AuthorizeAsync(
        WebApp app,
        Guid runnerId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var runner = await dbContext.Runners
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == runnerId, cancellationToken)
            ?? throw new InvalidOperationException("Execution Runner not found.");

        if (runner.DeletionRequestedAtUtc.HasValue)
            throw new InvalidOperationException("This Runner is being removed and cannot accept app assignments.");
        if (RunnerPurposes.Normalize(runner.Purpose) != RunnerPurposes.Managed ||
            RunnerApplicationProfiles.Normalize(runner.ApplicationProfile) != RunnerApplicationProfiles.MachineManagement)
            throw new InvalidOperationException("Only Machine Management Runners can run applications.");

        var runnerIsVisible = await authorizationService
            .GetRunnersQueryByContext(app.ProjectId, userId)
            .AnyAsync(item => item.Id == runnerId, cancellationToken);
        if (!runnerIsVisible)
        {
            throw new UnauthorizedAccessException(
                "You do not have access to the assigned Runner.");
        }

        if (!RunnerCapabilities.CanExecuteApplications(runner.CapabilitiesJson, runner.Purpose))
        {
            throw new InvalidOperationException(
                "The selected Runner is not eligible for App execution.");
        }

        return runner;
    }

}
