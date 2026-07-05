using Automaton.AiGuidance;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class AdminAiGuidanceService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAutomatonGuidanceProvider _embeddedGuidanceProvider;
    private readonly IAuditEventService _auditEventService;

    public AdminAiGuidanceService(
        ApplicationDbContext dbContext,
        IAutomatonGuidanceProvider embeddedGuidanceProvider,
        IAuditEventService auditEventService)
    {
        _dbContext = dbContext;
        _embeddedGuidanceProvider = embeddedGuidanceProvider;
        _auditEventService = auditEventService;
    }

    public Task<List<AiGuidanceDocument>> ListAsync(CancellationToken cancellationToken = default) =>
        _dbContext.AiGuidanceDocuments
            .AsNoTracking()
            .OrderBy(x => x.Surface)
            .ThenBy(x => x.Title)
            .ToListAsync(cancellationToken);

    public Task<AiGuidanceDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.AiGuidanceDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<AiGuidanceDocument> CreateAsync(
        string key,
        string title,
        string surface,
        string documentKind,
        string? description,
        string? tagsJson,
        string contentMarkdown,
        bool isEnabled,
        bool requiredByDefault,
        bool isModelLoadable,
        int sortOrder,
        CancellationToken cancellationToken = default)
    {
        await EnsureUniqueAsync(key, null, cancellationToken);

        var now = DateTime.UtcNow;
        var entity = new AiGuidanceDocument
        {
            Id = Guid.NewGuid(),
            Key = key.Trim(),
            Title = title.Trim(),
            Surface = surface.Trim(),
            DocumentKind = documentKind.Trim(),
            Description = NormalizeOptional(description),
            TagsJson = NormalizeOptional(tagsJson),
            ContentMarkdown = contentMarkdown,
            IsEnabled = isEnabled,
            RequiredByDefault = requiredByDefault,
            IsModelLoadable = isModelLoadable,
            SortOrder = sortOrder,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.AiGuidanceDocuments.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await WriteAuditAsync(
            AuditEventTypes.AiGuidanceCreated,
            AuditActions.Created,
            entity,
            "AI guidance document created",
            cancellationToken);

        return entity;
    }

    public async Task<AiGuidanceDocument?> UpdateAsync(
        Guid id,
        string key,
        string title,
        string surface,
        string documentKind,
        string? description,
        string? tagsJson,
        string contentMarkdown,
        bool isEnabled,
        bool requiredByDefault,
        bool isModelLoadable,
        int sortOrder,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.AiGuidanceDocuments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        await EnsureUniqueAsync(key, id, cancellationToken);

        entity.Key = key.Trim();
        entity.Title = title.Trim();
        entity.Surface = surface.Trim();
        entity.DocumentKind = documentKind.Trim();
        entity.Description = NormalizeOptional(description);
        entity.TagsJson = NormalizeOptional(tagsJson);
        entity.ContentMarkdown = contentMarkdown;
        entity.IsEnabled = isEnabled;
        entity.RequiredByDefault = requiredByDefault;
        entity.IsModelLoadable = isModelLoadable;
        entity.SortOrder = sortOrder;
        entity.Version += 1;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await WriteAuditAsync(
            AuditEventTypes.AiGuidanceUpdated,
            AuditActions.Updated,
            entity,
            "AI guidance document updated",
            cancellationToken);

        return entity;
    }

    public async Task<AiGuidanceDocument?> SetEnabledAsync(Guid id, bool isEnabled, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.AiGuidanceDocuments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (entity.IsEnabled == isEnabled)
        {
            return entity;
        }

        entity.IsEnabled = isEnabled;
        entity.Version += 1;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await WriteAuditAsync(
            isEnabled ? AuditEventTypes.AiGuidanceEnabled : AuditEventTypes.AiGuidanceDisabled,
            isEnabled ? AuditActions.Enabled : AuditActions.Disabled,
            entity,
            isEnabled ? "AI guidance document enabled" : "AI guidance document disabled",
            cancellationToken);

        return entity;
    }

    public async Task<AiGuidanceDocument?> ResetToEmbeddedDefaultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.AiGuidanceDocuments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var embedded = FindEmbeddedDefault(entity.Key);
        if (embedded is null)
        {
            throw new InvalidOperationException($"No embedded default exists for '{entity.Key}'.");
        }

        var parsed = AiGuidanceMarkdownFrontMatter.Parse(embedded.Content);
        entity.Title = string.IsNullOrWhiteSpace(parsed.Title) ? entity.Title : parsed.Title;
        entity.Description = string.IsNullOrWhiteSpace(parsed.Description) ? entity.Description : parsed.Description;
        entity.ContentMarkdown = parsed.Body;
        entity.Version += 1;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await WriteAuditAsync(
            AuditEventTypes.AiGuidanceResetToDefault,
            AuditActions.Changed,
            entity,
            "AI guidance document reset to embedded default",
            cancellationToken);

        return entity;
    }

    public bool HasEmbeddedDefault(string key) => FindEmbeddedDefault(key) is not null;

    private GuidanceSource? FindEmbeddedDefault(string key) =>
        _embeddedGuidanceProvider
            .GetReferenceSources()
            .FirstOrDefault(x => string.Equals(BuildKeyFromRelativePath(x.RelativePath), key, StringComparison.Ordinal));

    private async Task EnsureUniqueAsync(string key, Guid? excludeId, CancellationToken cancellationToken)
    {
        var normalizedKey = key.Trim();

        var duplicateKey = await _dbContext.AiGuidanceDocuments.AnyAsync(
            x => x.Key == normalizedKey && (!excludeId.HasValue || x.Id != excludeId.Value),
            cancellationToken);
        if (duplicateKey)
        {
            throw new InvalidOperationException($"A guidance document with key '{normalizedKey}' already exists.");
        }
    }

    private async Task WriteAuditAsync(
        string eventType,
        string action,
        AiGuidanceDocument entity,
        string message,
        CancellationToken cancellationToken)
    {
        await _auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = eventType,
            Action = action,
            EntityType = AuditEntityTypes.AiGuidance,
            EntityId = entity.Id,
            Message = $"{message}: {entity.Key}",
            Metadata = new
            {
                entity.Key,
                entity.Title,
                entity.Surface,
                entity.DocumentKind,
                entity.TagsJson,
                entity.IsEnabled,
                entity.RequiredByDefault,
                entity.IsModelLoadable,
                entity.SortOrder,
                entity.Version,
                HasEmbeddedDefault = HasEmbeddedDefault(entity.Key)
            }
        }, cancellationToken);
    }

    private static string BuildKeyFromRelativePath(string relativePath) =>
        relativePath.Replace("llms/", string.Empty, StringComparison.Ordinal)
            .Replace(".md", string.Empty, StringComparison.Ordinal);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
