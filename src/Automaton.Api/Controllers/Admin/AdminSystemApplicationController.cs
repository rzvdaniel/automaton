using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/system-application")]
[Authorize(Policy = "Administrators")]
public sealed class AdminSystemApplicationController(
    ApplicationDbContext db,
    ITrustedSystemActionManifestSource manifests) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var schedules = await db.ActionSchedules.AsNoTracking()
            .Where(x => x.ApplicationId == InfrastructureAppTrust.PlatformAppId)
            .ToListAsync(cancellationToken);
        var executions = await db.AppModuleExecutions.AsNoTracking()
            .Where(x => x.AppId == InfrastructureAppTrust.PlatformAppId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(cancellationToken);
        var actions = manifests.List().Select(manifest =>
        {
            var lastExecution = executions.FirstOrDefault(x => x.ModuleKey == manifest.Metadata.Key);
            return new
            {
                key = manifest.Metadata.Key,
                name = manifest.Metadata.Name,
                description = manifest.Metadata.Description,
                trustLevel = "Platform",
                schedulable = manifest.Spec.Schedulable,
                allowedExecutionScopes = manifest.Spec.AllowedExecutionScopes,
                inputSchema = manifest.Spec.Input,
                timeoutSeconds = manifest.Spec.TimeoutSeconds,
                minimumIntervalSeconds = manifest.Spec.MinimumIntervalSeconds,
                deployedHash = lastExecution?.DeploymentHash,
                scheduleCount = schedules.Count(x => x.ActionKey == manifest.Metadata.Key),
                lastExecution = lastExecution == null ? null : new
                {
                    lastExecution.Id,
                    lastExecution.ModuleKey,
                    lastExecution.Status,
                    lastExecution.FailureSummary,
                    lastExecution.CreatedAtUtc,
                    lastExecution.StartedAtUtc,
                    lastExecution.CompletedAtUtc,
                    lastExecution.ScheduleOccurrenceId,
                    lastExecution.DeploymentHash
                }
            };
        });
        return Ok(new
        {
            application = new { id = InfrastructureAppTrust.PlatformAppId, name = InfrastructureAppTrust.PlatformAppName, readOnly = true },
            actions,
            schedules = schedules.Select(x => new
            {
                x.Id, x.ApplicationId, x.ActionKey, x.Name, x.Description, x.ExecutionScope,
                x.ScheduleType, x.RunAtUtc, x.IntervalSeconds, x.CronExpression, x.TimeZone,
                x.Enabled, x.NextRunAtUtc, x.LastTriggeredAtUtc, x.LastExecutionId,
                x.ConcurrencyPolicy, x.MisfirePolicy
            }),
            executions = executions.Select(x => new
            {
                x.Id, x.ModuleKey, x.Status, x.FailureSummary, x.CreatedAtUtc,
                x.StartedAtUtc, x.CompletedAtUtc, x.ScheduleOccurrenceId, x.DeploymentHash
            })
        });
    }
}
