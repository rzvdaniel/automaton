using System.ComponentModel.DataAnnotations;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/ai-providers")]
[Authorize(Policy = "Administrators")]
public class AdminAiSettingsController : ControllerBase
{
    private readonly IAiProvidersSettingsService settingsService;
    private readonly IAuditEventService auditEventService;

    public AdminAiSettingsController(
        IAiProvidersSettingsService settingsService,
        IAuditEventService auditEventService)
    {
        this.settingsService = settingsService;
        this.auditEventService = auditEventService;
    }
    #region Operational Settings

    /// <summary>
    /// Get the current operational AI settings (iteration limits, token caps, etc.).
    /// Returns the DB-persisted values if set, otherwise the appsettings.json defaults.
    /// </summary>
    [HttpGet("~/api/admin/ai-settings")]
    public async Task<ActionResult<AiProvidersOperationalConfigResponse>> GetAiSettings()
    {
        var settings = await settingsService.GetOperationalSettingsAsync();
        return Ok(MapToResponse(settings));
    }

    /// <summary>
    /// Save operational AI settings to the database.
    /// Changes take effect immediately — no server restart required.
    /// </summary>
    [HttpPut("~/api/admin/ai-settings")]
    public async Task<ActionResult<AiProvidersOperationalConfigResponse>> SaveAiSettings(
        [FromBody] SaveAiSettingsRequest request)
    {
        var normalizedHistoryCompaction = NormalizeHistoryCompaction(request.HistoryCompaction);

        var settings = new AiProvidersOperationalConfig
        {
            MaxConversationHistoryMessages = request.MaxConversationHistoryMessages,
            MaxIterations                  = request.MaxIterations,
            MaxConsecutiveFailures         = request.MaxConsecutiveFailures,
            DefaultMaxOutputTokens         = request.DefaultMaxOutputTokens,
            AppVerificationDefaultStrategy = request.AppVerificationDefaultStrategy,
            AppVerificationAllowUserOverride = request.AppVerificationAllowUserOverride,
            AppExecutionDefaultPolicy = request.AppExecutionDefaultPolicy,
            AppExecutionAllowUserOverride = request.AppExecutionAllowUserOverride,
            IterationLimits = new()
            {
                Simple     = request.IterationLimits.Simple,
                Medium     = request.IterationLimits.Medium,
                Large      = request.IterationLimits.Large,
                Enterprise = request.IterationLimits.Enterprise,
                Autonomous = request.IterationLimits.Autonomous
            },
            HistoryCompaction = new()
            {
                RecentMessages   = normalizedHistoryCompaction.RecentMessages,
                MaxSummaryEntries = normalizedHistoryCompaction.MaxSummaryEntries,
                MaxSummaryChars  = normalizedHistoryCompaction.MaxSummaryChars,
                MaxEntryChars    = normalizedHistoryCompaction.MaxEntryChars
            }
        };

        await settingsService.SaveOperationalSettingsAsync(settings);

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.AiOperationalSettingsUpdated,
            Action = AuditActions.Updated,
            EntityType = AuditEntityTypes.AiSettings,
            Message = "AI operational settings were updated",
            Metadata = new
            {
                settings.MaxConversationHistoryMessages,
                settings.MaxIterations,
                settings.MaxConsecutiveFailures,
                settings.DefaultMaxOutputTokens,
                settings.AppVerificationDefaultStrategy,
                settings.AppVerificationAllowUserOverride,
                settings.AppExecutionDefaultPolicy,
                settings.AppExecutionAllowUserOverride,
                settings.IterationLimits.Simple,
                settings.IterationLimits.Medium,
                settings.IterationLimits.Large,
                settings.IterationLimits.Enterprise,
                settings.IterationLimits.Autonomous,
                settings.HistoryCompaction.RecentMessages,
                settings.HistoryCompaction.MaxSummaryEntries,
                settings.HistoryCompaction.MaxSummaryChars,
                settings.HistoryCompaction.MaxEntryChars
            }
        });

        return Ok(MapToResponse(settings));
    }

    private static AiProvidersOperationalConfigResponse MapToResponse(AiProvidersOperationalConfig s) => new()
    {
        MaxConversationHistoryMessages = s.MaxConversationHistoryMessages,
        MaxIterations                  = s.MaxIterations,
        MaxConsecutiveFailures         = s.MaxConsecutiveFailures,
        DefaultMaxOutputTokens         = s.DefaultMaxOutputTokens,
        AppVerificationDefaultStrategy = s.AppVerificationDefaultStrategy,
        AppVerificationAllowUserOverride = s.AppVerificationAllowUserOverride,
        AppExecutionDefaultPolicy = s.AppExecutionDefaultPolicy,
        AppExecutionAllowUserOverride = s.AppExecutionAllowUserOverride,
        IterationLimits = new()
        {
            Simple     = s.IterationLimits.Simple,
            Medium     = s.IterationLimits.Medium,
            Large      = s.IterationLimits.Large,
            Enterprise = s.IterationLimits.Enterprise,
            Autonomous = s.IterationLimits.Autonomous
        },
        HistoryCompaction = new()
        {
            RecentMessages    = s.HistoryCompaction.RecentMessages,
            MaxSummaryEntries = s.HistoryCompaction.MaxSummaryEntries,
            MaxSummaryChars   = s.HistoryCompaction.MaxSummaryChars,
            MaxEntryChars     = s.HistoryCompaction.MaxEntryChars
        }
    };

    private static HistoryCompactionConfig NormalizeHistoryCompaction(HistoryCompactionConfigRequest request)
    {
        return new HistoryCompactionConfig
        {
            RecentMessages = Math.Clamp(
                request.RecentMessages,
                HistoryCompactionBounds.MinRecentMessages,
                HistoryCompactionBounds.MaxRecentMessages),
            MaxSummaryEntries = Math.Clamp(
                request.MaxSummaryEntries,
                HistoryCompactionBounds.MinMaxSummaryEntries,
                HistoryCompactionBounds.MaxMaxSummaryEntries),
            MaxSummaryChars = Math.Clamp(
                request.MaxSummaryChars,
                HistoryCompactionBounds.MinMaxSummaryChars,
                HistoryCompactionBounds.MaxMaxSummaryChars),
            MaxEntryChars = Math.Clamp(
                request.MaxEntryChars,
                HistoryCompactionBounds.MinMaxEntryChars,
                HistoryCompactionBounds.MaxMaxEntryChars)
        };
    }

    #endregion
}
