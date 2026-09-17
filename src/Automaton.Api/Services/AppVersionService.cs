using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Api.Services;

public class AppVersionService
{
    private readonly ApplicationDbContext dataContext;
    private readonly AppFileStorageService fileStorageService;
    private readonly Serilog.ILogger logger;

    public sealed record CreateSnapshotResult(bool SnapshotCreated, AppVersion? Version, string Message);

    public AppVersionService(ApplicationDbContext dataContext, AppFileStorageService fileStorageService)
    {
        this.dataContext = dataContext;
        this.fileStorageService = fileStorageService;
        this.logger = Serilog.Log.ForContext<AppVersionService>();
    }

    public async Task<CreateSnapshotResult> CreateSnapshotAsync(
        Guid appId,
        Guid userId,
        AppVersionTrigger trigger,
        Guid? aiSessionId = null,
        string? aiModel = null,
        string? summary = null,
        string? source = null,
        string? reason = null)
    {
        var app = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .FirstOrDefaultAsync(x => x.Id == appId);

        if (app == null)
        {
            throw new InvalidOperationException("App not found.");
        }

        var ownerId = app.WebAppUsers.FirstOrDefault()?.UserId ?? userId;
        var resolvedSource = source ?? GetDefaultSource(trigger);
        var resolvedReason = reason ?? GetDefaultReason(trigger);

        if (trigger == AppVersionTrigger.ManualSave &&
            string.Equals(resolvedSource, AppVersionSources.CliDeploy, StringComparison.Ordinal) &&
            await MatchesLatestSnapshotAsync(ownerId, appId))
        {
            return new CreateSnapshotResult(false, null, "Skipped restore point creation because source files have not changed since the latest restore point.");
        }

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
            Source = resolvedSource,
            Reason = resolvedReason,
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
                inputVariables = definition.CodeInputVariables ?? new List<Core.Models.StepVariable>(),
                outputVariables = definition.CodeOutputVariables ?? new List<Core.Models.StepVariable>()
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

        logger.Debug("Created app version {VersionNumber} for app {AppId}", nextVersion, appId);

        return new CreateSnapshotResult(true, appVersion, "Version snapshot created.");
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
            var versionsPath = fileStorageService.GetAppVersionDirectoryPath(ownerId.Value, appId);
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

    public string GetEffectiveSource(AppVersion version)
    {
        return !string.IsNullOrWhiteSpace(version.Source)
            ? version.Source
            : GetDefaultSource(version.Trigger);
    }

    public string GetEffectiveReason(AppVersion version)
    {
        return !string.IsNullOrWhiteSpace(version.Reason)
            ? version.Reason
            : GetDefaultReason(version.Trigger);
    }

    private static string GetDefaultSource(AppVersionTrigger trigger) => trigger switch
    {
        AppVersionTrigger.NewGeneration => AppVersionSources.AiChat,
        _ => AppVersionSources.Studio,
    };

    private static string GetDefaultReason(AppVersionTrigger trigger) => trigger switch
    {
        AppVersionTrigger.NewGeneration => AppVersionReasons.AfterAiSession,
        AppVersionTrigger.Publish => AppVersionReasons.Publish,
        AppVersionTrigger.Restore => AppVersionReasons.Restore,
        _ => AppVersionReasons.Manual,
    };

    private async Task<bool> MatchesLatestSnapshotAsync(Guid ownerId, Guid appId)
    {
        var latestVersion = await dataContext.AppVersions
            .Where(version => version.AppId == appId)
            .OrderByDescending(version => version.VersionNumber)
            .FirstOrDefaultAsync();

        if (latestVersion == null)
        {
            return false;
        }

        var currentSourcePath = fileStorageService.GetAppSourceDirectoryPath(ownerId, appId);
        if (!Directory.Exists(currentSourcePath))
        {
            return false;
        }

        var latestSnapshotPath = fileStorageService.GetVersionSnapshotFilesPath(ownerId, appId, latestVersion.VersionNumber);
        if (latestSnapshotPath == null)
        {
            return false;
        }

        return fileStorageService.DirectoriesMatch(currentSourcePath, latestSnapshotPath);
    }
}
