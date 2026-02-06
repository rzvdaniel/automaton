using System.Text.Json;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Studio.Server.Services;

public class AppVersionService
{
    private readonly ApplicationDbContext dataContext;
    private readonly AppFileStorageService fileStorageService;
    private readonly Serilog.ILogger logger;

    public AppVersionService(ApplicationDbContext dataContext, AppFileStorageService fileStorageService)
    {
        this.dataContext = dataContext;
        this.fileStorageService = fileStorageService;
        this.logger = Serilog.Log.ForContext<AppVersionService>();
    }

    public async Task<AppVersion> CreateSnapshotAsync(
        Guid appId,
        Guid userId,
        AppVersionTrigger trigger,
        Guid? aiSessionId = null,
        string? aiModel = null)
    {
        var app = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .FirstOrDefaultAsync(x => x.Id == appId);

        if (app == null)
        {
            throw new InvalidOperationException("App not found.");
        }

        var ownerId = app.WebAppUsers.FirstOrDefault()?.UserId ?? userId;
        var nextVersion = await GetNextVersionNumberAsync(appId);

        var snapshotPath = await fileStorageService.CreateVersionSnapshotAsync(ownerId, appId, nextVersion);

        var appVersion = new AppVersion
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            VersionNumber = nextVersion,
            Summary = string.Empty,
            Trigger = trigger,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId,
            AiSessionId = aiSessionId,
            AiModel = aiModel,
            SnapshotPath = snapshotPath
        };

        dataContext.AppVersions.Add(appVersion);

        var linkedSteps = await dataContext.WebAppSteps
            .Where(was => was.WebAppId == appId)
            .Include(was => was.CustomStep)
            .Select(was => was.CustomStep)
            .ToListAsync();

        foreach (var step in linkedSteps)
        {
            var definition = JsonSerializer.Deserialize<CustomStepDefinition>(step.Definition)
                ?? new CustomStepDefinition();

            var variablesSnapshot = JsonSerializer.Serialize(new
            {
                inputVariables = definition.CodeInputVariables ?? new List<Automaton.Core.Models.StepVariable>(),
                outputVariables = definition.CodeOutputVariables ?? new List<Automaton.Core.Models.StepVariable>()
            });

            var stepSnapshot = new AppVersionStep
            {
                Id = Guid.NewGuid(),
                AppVersionId = appVersion.Id,
                StepId = step.Id,
                StepName = step.Name,
                Action = AppVersionStepAction.Linked,
                Language = definition.Language,
                CodeSnapshot = definition.Code ?? string.Empty,
                VariablesSnapshot = variablesSnapshot
            };

            dataContext.AppVersionSteps.Add(stepSnapshot);
        }

        await dataContext.SaveChangesAsync();

        logger.Information("Created app version {VersionNumber} for app {AppId}", nextVersion, appId);

        return appVersion;
    }

    public async Task<(List<AppVersion> Versions, int TotalCount)> GetVersionsAsync(Guid appId, int pageNumber = 1, int pageSize = 20)
    {
        var query = dataContext.AppVersions
            .Where(v => v.AppId == appId)
            .OrderByDescending(v => v.VersionNumber);

        var totalCount = await query.CountAsync();

        var versions = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (versions, totalCount);
    }

    public async Task<AppVersion?> GetVersionDetailAsync(Guid appId, int versionNumber)
    {
        var version = await dataContext.AppVersions
            .Include(v => v.Steps)
            .FirstOrDefaultAsync(v => v.AppId == appId && v.VersionNumber == versionNumber);

        return version;
    }

    public async Task RestoreVersionAsync(Guid appId, int versionNumber, Guid userId)
    {
        var version = await dataContext.AppVersions
            .Include(v => v.WebApp)
            .FirstOrDefaultAsync(v => v.AppId == appId && v.VersionNumber == versionNumber);

        if (version == null)
        {
            throw new InvalidOperationException($"Version {versionNumber} not found for app {appId}");
        }

        // Get app owner to determine correct user ID for file operations
        var ownerId = version.WebApp?.WebAppUsers.FirstOrDefault()?.UserId ?? userId;

        // Restore files from version snapshot to current
        await fileStorageService.RestoreVersionAsync(ownerId, appId, versionNumber);

        // TODO: Optionally create a new version to track the restore action
        // For now, we just restore the files

        logger.Information("Restored app {AppId} to version {VersionNumber}", appId, versionNumber);
    }

    /// <summary>
    /// Generate an AI summary of version changes. Currently a placeholder;
    /// will be called by AI chat system to populate version summaries.
    /// </summary>
    public async Task<string> GenerateVersionSummaryAsync(Guid appId, int versionNumber, object? changeSummary = null)
    {
        // TODO: Integrate with AI chat/MCP tools to generate meaningful summaries
        // based on file changes and step modifications
        return $"Version {versionNumber} snapshot";
    }

    private async Task<int> GetNextVersionNumberAsync(Guid appId)
    {
        var maxVersion = await dataContext.AppVersions
            .Where(v => v.AppId == appId)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync();

        return (maxVersion ?? 0) + 1;
    }
}
