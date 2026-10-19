using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Core.Enums;
using Automaton.Core.Models;
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
        if (!await authorizationService.CanExecuteAssignedOrganizationAppAsync(app.Id, userId, cancellationToken))
        {
            return await AuthorizeAsync(app, runnerId, userId, cancellationToken);
        }

        var runner = await dbContext.Runners.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == runnerId, cancellationToken)
            ?? throw new InvalidOperationException("Execution Runner not found.");
        if (RunnerApplicationProfiles.Normalize(runner.ApplicationProfile) != RunnerApplicationProfiles.MachineManagement
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

        if (RunnerCapabilities.RequiresAdministrator(runner.CapabilitiesJson, runner.Purpose))
        {
            await EnsureInfrastructureRunnerAuthorizedAsync(app, runner, userId, cancellationToken);
            return runner;
        }

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

    private async Task EnsureInfrastructureRunnerAuthorizedAsync(
        WebApp app,
        Runner runner,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                RunnerPurposes.Normalize(runner.Purpose),
                RunnerPurposes.Infrastructure,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Privileged capabilities require an Infrastructure Runner.");
        }

        if (!InfrastructureAppTrust.IsTrustedApp(app.Id))
        {
            throw new UnauthorizedAccessException(
                "Infrastructure Runners may only be assigned to the trusted infrastructure App.");
        }

        var isAdministrator = await dbContext.UserRoles
            .Where(link => link.UserId == userId)
            .Join(
                dbContext.Roles,
                link => link.RoleId,
                role => role.Id,
                (_, role) => role.Name)
            .AnyAsync(
                roleName => roleName == UserRole.Administrator ||
                            roleName == UserRole.SystemAdministrator,
                cancellationToken);
        if (!isAdministrator)
        {
            throw new UnauthorizedAccessException(
                "Administrator access is required to use Infrastructure Runners.");
        }

        if (!RunnerCapabilities.Includes(
                runner.CapabilitiesJson,
                RunnerCapabilities.ApplicationHosting,
                runner.Purpose))
        {
            throw new InvalidOperationException(
                "The Infrastructure Runner does not support application hosting.");
        }
    }
}
