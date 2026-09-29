using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class RunnerStorageTelemetrySubmissionService(
    ApplicationDbContext db,
    RunnerStorageTelemetryService telemetryService)
{
    public async Task ReportHostedRunnerAsync(
        Guid apiKeyId,
        HostedRunnerStorageTelemetry report,
        CancellationToken cancellationToken)
    {
        var runner = await ResolveHostedRunnerAsync(apiKeyId, cancellationToken);
        await telemetryService.ReportHostedRunnerAsync(runner, report, cancellationToken);
    }

    public async Task ReportRunnerHostAsync(
        Guid apiKeyId,
        RunnerHostStorageTelemetry report,
        CancellationToken cancellationToken)
    {
        var runner = await ResolveInfrastructureRunnerAsync(apiKeyId, cancellationToken);
        await telemetryService.ReportRunnerHostAsync(runner, report, cancellationToken);
    }

    private async Task<RunnerDetails> ResolveHostedRunnerAsync(
        Guid apiKeyId,
        CancellationToken cancellationToken)
    {
        var runner = await ResolveRunnerAsync(apiKeyId, RunnerPurposes.Managed, cancellationToken);
        var hostedRunnerId = await db.HostedRunners.AsNoTracking()
            .Where(item => item.RunnerId == runner.Id)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
        runner.HostedRunnerId = hostedRunnerId
            ?? throw new UnauthorizedAccessException("The API key is not assigned to a Hosted Runner.");
        return runner;
    }

    private async Task<RunnerDetails> ResolveInfrastructureRunnerAsync(
        Guid apiKeyId,
        CancellationToken cancellationToken)
    {
        var runner = await ResolveRunnerAsync(apiKeyId, RunnerPurposes.Infrastructure, cancellationToken);
        var runnerHostId = await db.RunnerHosts.AsNoTracking()
            .Where(item => item.InfrastructureRunnerId == runner.Id)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
        runner.RunnerHostId = runnerHostId
            ?? throw new UnauthorizedAccessException("The API key is not assigned to a Runner Host.");
        return runner;
    }

    private async Task<RunnerDetails> ResolveRunnerAsync(
        Guid apiKeyId,
        string expectedPurpose,
        CancellationToken cancellationToken)
    {
        var runner = await db.Runners.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ApiKeyId == apiKeyId, cancellationToken);
        if (runner is null || runner.Purpose != expectedPurpose)
            throw new UnauthorizedAccessException("The API key is not assigned to the expected Runner type.");

        return new RunnerDetails
        {
            Id = runner.Id,
            Name = runner.Name,
            Purpose = runner.Purpose
        };
    }
}
