using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Persists a newly enrolled Machine Management Runner.</summary>
public sealed class RunnerRegistrationService(
    ApplicationDbContext dbContext,
    UserContextService userContextService,
    RunnerAuditService runnerAuditService)
{
    private Guid UserId => userContextService.GetUserId();

    public async Task<int> Add(RunnerDetails runnerModel, CancellationToken cancellationToken)
    {
        if (UserId == Guid.Empty)
            throw new InvalidOperationException("Authenticated user context is required to register a Runner.");

        var purpose = RunnerPurposes.Normalize(runnerModel.Purpose);
        if (!RunnerProfileRules.IsValid(purpose, runnerModel.ApplicationProfile))
            throw new InvalidOperationException("Only Machine Management Runners can be registered.");
        var capabilities = RunnerCapabilities.Normalize(runnerModel.Capabilities, purpose);

        var runner = new Runner
        {
            Id = runnerModel.Id,
            Name = runnerModel.Name,
            Status = RunnerConnectivityStatuses.Unknown,
            Version = runnerModel.Version,
            MachineName = runnerModel.MachineName,
            IpAddress = runnerModel.IpAddress,
            Purpose = purpose,
            CapabilitiesJson = RunnerCapabilities.Serialize(capabilities, purpose),
            InstallationId = runnerModel.InstallationId
        };
        dbContext.Runners.Add(runner);

        dbContext.RunnerUsers.Add(new RunnerUser
        {
            RunnerId = runner.Id,
            UserId = UserId
        });

        var result = await dbContext.SaveChangesAsync(cancellationToken);

        await runnerAuditService.WriteCreatedAsync(runner, UserId, result, cancellationToken);
        return result;
    }
}
