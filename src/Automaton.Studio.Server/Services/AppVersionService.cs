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
        string? aiModel = null,
        string? summary = null)
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

        // Generate file change summary by comparing with previous version
        var filesChangeSummary = await GenerateFilesChangeSummaryAsync(appId, nextVersion);
        
        // Use provided summary if given, otherwise generate from file changes
        var resolvedSummary = summary ?? await GenerateVersionSummaryAsync(appId, nextVersion, filesChangeSummary);

        var appVersion = new AppVersion
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            VersionNumber = nextVersion,
            Summary = resolvedSummary,
            Trigger = trigger,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId,
            AiSessionId = aiSessionId,
            AiModel = aiModel,
            SnapshotPath = snapshotPath,
            FilesChangeSummary = filesChangeSummary
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

    public async Task UpdateSummaryAsync(Guid appVersionId, string summary)
    {
        var version = await dataContext.AppVersions.FindAsync(appVersionId);
        if (version == null)
        {
            logger.Warning("AppVersion {AppVersionId} not found when trying to update summary", appVersionId);
            return;
        }

        version.Summary = summary;
        await dataContext.SaveChangesAsync();

        logger.Information("Updated summary for app version {AppVersionId}", appVersionId);
    }

    /// <summary>
    /// Returns true if the app has already been generated at least once.
    /// Uses _metadata.json as the reliable marker — AI always creates it on first generation.
    /// </summary>
    public bool AppHasBeenGenerated(Guid appId, Guid userId)
    {
        var appPath = fileStorageService.GetAppDirectoryPath(userId, appId);
        var metadataPath = Path.Combine(appPath, "_metadata.json");
        return File.Exists(metadataPath);
    }

    public async Task<(List<AppVersion> Versions, int TotalCount)> GetVersionsAsync(Guid appId, int pageNumber = 1, int pageSize = 20)
    {
        var query = dataContext.AppVersions
            .Include(v => v.CreatedByUser)
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
            .Include(v => v.CreatedByUser)
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

        // Restore files from version snapshot to current working directory
        await fileStorageService.RestoreVersionAsync(ownerId, appId, versionNumber);

        logger.Information("Restored app {AppId} to version {VersionNumber}", appId, versionNumber);
    }

    /// <summary>
    /// Generate a human-readable summary of version changes.
    /// Includes trigger type and overview of file/step changes.
    /// </summary>
    public async Task<string> GenerateVersionSummaryAsync(Guid appId, int versionNumber, string? filesChangeSummary = null)
    {
        try
        {
            var summaryParts = new List<string>();

            // Get the previous version to track step changes
            var prevVersionNumber = versionNumber - 1;
            var currentVersion = await dataContext.AppVersions
                .Include(v => v.Steps)
                .FirstOrDefaultAsync(v => v.AppId == appId && v.VersionNumber == versionNumber);

            var prevVersion = await dataContext.AppVersions
                .Include(v => v.Steps)
                .FirstOrDefaultAsync(v => v.AppId == appId && v.VersionNumber == prevVersionNumber);

            // Parse file changes if available
            if (!string.IsNullOrEmpty(filesChangeSummary))
            {
                try
                {
                    var fileChanges = JsonSerializer.Deserialize<JsonElement>(filesChangeSummary);
                    if (fileChanges.ValueKind == JsonValueKind.Object)
                    {
                        var added = fileChanges.GetProperty("added").GetArrayLength();
                        var modified = fileChanges.GetProperty("modified").GetArrayLength();
                        var deleted = fileChanges.GetProperty("deleted").GetArrayLength();

                        if (added > 0 || modified > 0 || deleted > 0)
                        {
                            summaryParts.Add($"{added + modified + deleted} file(s) changed");
                            if (added > 0) summaryParts.Add($"{added} added");
                            if (modified > 0) summaryParts.Add($"{modified} modified");
                            if (deleted > 0) summaryParts.Add($"{deleted} removed");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Could not parse file changes summary");
                }
            }

            // Track step changes
            var currentSteps = currentVersion?.Steps ?? new List<AppVersionStep>();
            var prevSteps = prevVersion?.Steps ?? new List<AppVersionStep>();

            var addedSteps = currentSteps.Where(cs => !prevSteps.Any(ps => ps.StepId == cs.StepId)).ToList();
            var removedSteps = prevSteps.Where(ps => !currentSteps.Any(cs => cs.StepId == ps.StepId)).ToList();

            if (addedSteps.Any())
            {
                summaryParts.Add($"{addedSteps.Count} step(s) added");
            }
            if (removedSteps.Any())
            {
                summaryParts.Add($"{removedSteps.Count} step(s) removed");
            }

            // Fallback if no changes detected
            if (!summaryParts.Any())
            {
                return "Version snapshot created";
            }

            return string.Join("; ", summaryParts);
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to generate version summary for app {AppId} version {Version}", appId, versionNumber);
            return $"Version {versionNumber} snapshot";
        }
    }

    /// <summary>
    /// Generate a summary of file changes by comparing current version with previous version.
    /// Returns a JSON string describing added, modified, and deleted files.
    /// </summary>
    private async Task<string?> GenerateFilesChangeSummaryAsync(Guid appId, int newVersionNumber)
    {
        try
        {
            // Get app owner to access file storage
            var app = await dataContext.WebApps
                .Include(x => x.WebAppUsers)
                .FirstOrDefaultAsync(x => x.Id == appId);

            if (app == null)
            {
                return null;
            }

            var ownerId = app.WebAppUsers.FirstOrDefault()?.UserId;
            if (ownerId == null)
            {
                return null;
            }

            // Get current version snapshot files
            var appPath = fileStorageService.GetAppDirectoryPath(ownerId.Value, appId);
            var versionsPath = Path.Combine(appPath, "versions");
            var newVersionPath = Path.Combine(versionsPath, $"v{newVersionNumber:D3}", "files");

            if (!Directory.Exists(newVersionPath))
            {
                return null;
            }

            // Get files from new snapshot
            var newFiles = Directory.GetFiles(newVersionPath, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(newVersionPath, f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Get files from previous version if it exists
            var prevVersionNumber = newVersionNumber - 1;
            var prevVersionPath = Path.Combine(versionsPath, $"v{prevVersionNumber:D3}", "files");

            HashSet<string> prevFiles = new(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(prevVersionPath))
            {
                prevFiles = Directory.GetFiles(prevVersionPath, "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(prevVersionPath, f))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            // Determine changes
            var addedFiles = newFiles.Where(f => !prevFiles.Contains(f)).OrderBy(f => f).ToList();
            var deletedFiles = prevFiles.Where(f => !newFiles.Contains(f)).OrderBy(f => f).ToList();
            var modifiedFiles = new List<string>();

            // Check for modified files (files in both but content differs)
            foreach (var file in newFiles.Where(f => prevFiles.Contains(f)))
            {
                var newFilePath = Path.Combine(newVersionPath, file);
                var prevFilePath = Path.Combine(prevVersionPath, file);

                try
                {
                    var newContent = await File.ReadAllTextAsync(newFilePath);
                    var prevContent = await File.ReadAllTextAsync(prevFilePath);

                    if (newContent != prevContent)
                    {
                        modifiedFiles.Add(file);
                    }
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Could not compare file {File} for change detection", file);
                }
            }

            // Build summary JSON
            var summary = new
            {
                totalChanges = addedFiles.Count + modifiedFiles.Count + deletedFiles.Count,
                added = addedFiles,
                modified = modifiedFiles.OrderBy(f => f).ToList(),
                deleted = deletedFiles
            };

            return JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to generate file change summary for app {AppId} version {Version}", appId, newVersionNumber);
            return null;
        }
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
