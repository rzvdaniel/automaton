using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;

using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Automaton.Api.Services;

public class ApiKeyService
{
    private const int RawKeyLength = 40;
    private readonly ApplicationDbContext dbContext;
    private readonly ILogger<ApiKeyService> logger;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly IAuditEventService auditEventService;

    public ApiKeyService(
        ApplicationDbContext dbContext, 
        ILogger<ApiKeyService> logger,
        ResourceAuthorizationService authorizationService,
        IAuditEventService auditEventService)
    {
        this.dbContext = dbContext;
        this.logger = logger;
        this.authorizationService = authorizationService;
        this.auditEventService = auditEventService;
    }

    public async Task<(string apiKey, ApiKey entity)> CreateApiKeyAsync(
        Guid userId,
        string name,
        ApiKeyScope scope,
        string? description = null,
        DateTime? expiresAt = null,
        Guid? projectId = null,
        bool saveChanges = true,
        int? maximumUses = null)
    {
        // Generate a cryptographically secure API key
        var rawKey = GenerateRawKey();
        var prefix = scope switch
        {
            ApiKeyScope.ReadOnly => "ak_readonly_",
            ApiKeyScope.Execute => "ak_execute_",
            ApiKeyScope.Runner => "ak_runner_",
            ApiKeyScope.WebApp => "ak_webapp_",
            ApiKeyScope.InfrastructureRunnerEnroll => "ak_infra_enroll_",
            ApiKeyScope.FullAccess => "ak_fullaccess_",
            ApiKeyScope.Developer => "ak_developer_",
            _ => "ak_"
        };

        var fullApiKey = $"{prefix}{rawKey}";
        var hashedKey = HashApiKey(fullApiKey);

        var entity = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = name,
            HashedKey = hashedKey,
            KeyPrefix = $"{prefix}{rawKey[..8]}...",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            MaximumUses = maximumUses,
            Description = description,
            Scope = scope,
            IsActive = true
        };

        dbContext.ApiKeys.Add(entity);
        
        // Create personal ownership link
        var apiKeyUser = new ApiKeyUser
        {
            ApiKeyId = entity.Id,
            UserId = userId
        };
        dbContext.ApiKeyUsers.Add(apiKeyUser);
        
        // If project is specified, add to project
        if (projectId.HasValue)
        {
            var projectApiKey = new ProjectApiKey
            {
                ProjectId = projectId.Value,
                ApiKeyId = entity.Id,
                AddedAt = DateTime.UtcNow,
                AddedBy = userId
            };
            dbContext.ProjectApiKeys.Add(projectApiKey);
        }
        
        if (saveChanges)
        {
            await dbContext.SaveChangesAsync();

            await auditEventService.WriteAsync(new AuditEventWriteRequest
            {
                Action = AuditActions.Created,
                EntityType = AuditEntityTypes.ApiKey,
                EntityId = entity.Id,
                EventType = AuditEventTypes.ApiKeyCreated,
                Message = $"API key '{entity.Name}' was created",
                Outcome = AuditOutcomes.Success,
                ActorUserId = userId,
                Metadata = new
                {
                    entity.Name,
                    entity.Scope,
                    entity.ExpiresAt,
                    entity.KeyPrefix,
                    ProjectId = projectId
                }
            });
        }

        logger.LogDebug("API key created for user {UserId} with scope {Scope}", userId, scope);

        return (fullApiKey, entity);
    }

    internal static string GenerateRawKey()
    {
        // 20 bytes = 40 hex chars, which gives us a stable fixed-length suffix
        // without needing brittle substring logic on a variable-length encoded string.
        var keyBytes = new byte[RawKeyLength / 2];
        RandomNumberGenerator.Fill(keyBytes);
        return Convert.ToHexString(keyBytes).ToLowerInvariant();
    }

    public async Task<ApiKey?> ValidateApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return null;

        var hashedKey = HashApiKey(apiKey);

        var entity = await dbContext.ApiKeys
            .FirstOrDefaultAsync(ak => ak.HashedKey == hashedKey && ak.IsActive);

        if (entity == null)
            return null;

        if (entity.MaximumUses.HasValue && entity.UseCount >= entity.MaximumUses.Value)
            return null;

        // Check expiration
        if (entity.ExpiresAt.HasValue && entity.ExpiresAt.Value < DateTime.UtcNow)
        {
            logger.LogWarning("Expired API key used: {KeyPrefix}", entity.KeyPrefix);
            return null;
        }

        // Update last used timestamp
        entity.LastUsedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        return entity;
    }

    public async Task<(ApiKey Key, Guid UserId)?> ConsumeApiKeyAsync(
        string apiKey,
        ApiKeyScope requiredScope,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        if (dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction == null)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);
            var result = await ConsumeApiKeyCoreAsync(apiKey, requiredScope, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        return await ConsumeApiKeyCoreAsync(apiKey, requiredScope, cancellationToken);
    }

    private async Task<(ApiKey Key, Guid UserId)?> ConsumeApiKeyCoreAsync(
        string apiKey,
        ApiKeyScope requiredScope,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.ApiKeys
            .FirstOrDefaultAsync(x => x.HashedKey == HashApiKey(apiKey) && x.IsActive, cancellationToken);
        if (entity == null
            || entity.Scope != requiredScope
            || (entity.ExpiresAt.HasValue && entity.ExpiresAt.Value <= DateTime.UtcNow)
            || entity.UsedAt.HasValue
            || (entity.MaximumUses.HasValue && entity.UseCount >= entity.MaximumUses.Value))
        {
            return null;
        }

        var userId = await dbContext.ApiKeyUsers
            .Where(x => x.ApiKeyId == entity.Id)
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (userId == Guid.Empty)
            return null;

        entity.UseCount++;
        entity.UsedAt = DateTime.UtcNow;
        entity.LastUsedAt = entity.UsedAt;
        if (entity.MaximumUses.HasValue && entity.UseCount >= entity.MaximumUses.Value)
            entity.IsActive = false;

        await dbContext.SaveChangesAsync(cancellationToken);
        return (entity, userId);
    }

    public async Task<IEnumerable<ApiKey>> GetUserApiKeysAsync(Guid userId, Guid? projectId = null)
    {
        // Use authorization service query to get API keys filtered by project context
        return await authorizationService.GetApiKeysQueryByContext(projectId, userId)
            .OrderByDescending(ak => ak.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> DeleteApiKeyAsync(Guid apiKeyId, Guid userId)
    {
        // Check if user can delete this API key
        if (!await authorizationService.CanDeleteApiKeyAsync(apiKeyId, userId))
        {
            logger.LogWarning("User {UserId} attempted to delete API key {ApiKeyId} without permission", userId, apiKeyId);
            return false;
        }

        var apiKey = await authorizationService.GetAccessibleApiKeysQuery(userId)
            .Include(ak => ak.ProjectApiKeys)
            .FirstOrDefaultAsync(ak => ak.Id == apiKeyId);

        if (apiKey == null)
            return false;

        var auditMetadata = new
        {
            apiKey.Name,
            apiKey.Scope,
            apiKey.ExpiresAt,
            apiKey.KeyPrefix,
            ProjectIds = apiKey.ProjectApiKeys.Select(x => x.ProjectId).ToList()
        };

        dbContext.ApiKeys.Remove(apiKey);
        await dbContext.SaveChangesAsync();

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Deleted,
            EntityType = AuditEntityTypes.ApiKey,
            EntityId = apiKey.Id,
            EventType = AuditEventTypes.ApiKeyDeleted,
            Message = $"API key '{apiKey.Name}' was deleted",
            Outcome = AuditOutcomes.Success,
            ActorUserId = userId,
            Metadata = auditMetadata
        });

        logger.LogDebug("API key deleted for user {UserId}", userId);
        return true;
    }

    public Task<List<ApiKey>> GetInfrastructureRunnerEnrollmentKeysAsync(CancellationToken cancellationToken = default)
        => dbContext.ApiKeys
            .Where(x => x.Scope == ApiKeyScope.InfrastructureRunnerEnroll)
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

    public async Task<bool> RevokeInfrastructureRunnerEnrollmentKeyAsync(
        Guid apiKeyId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var key = await dbContext.ApiKeys
            .FirstOrDefaultAsync(x => x.Id == apiKeyId
                && x.Scope == ApiKeyScope.InfrastructureRunnerEnroll,
                cancellationToken);
        if (key == null)
            return false;

        if (key.IsActive)
        {
            key.IsActive = false;
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEventService.WriteAsync(new AuditEventWriteRequest
            {
                Action = AuditActions.Disabled,
                EntityType = AuditEntityTypes.ApiKey,
                EntityId = key.Id,
                EventType = AuditEventTypes.ApiKeyDeleted,
                Message = $"Infrastructure Runner enrollment key '{key.Name}' was revoked.",
                Outcome = AuditOutcomes.Success,
                ActorUserId = userId,
                Metadata = new { key.KeyPrefix, key.Scope, key.ExpiresAt }
            }, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Get the user's active WebApp-scoped API key for flow execution from web apps
    /// </summary>
    public async Task<ApiKey?> GetUserWebAppApiKeyAsync(Guid userId)
    {
        // Use authorization service query to get accessible API keys
        return await authorizationService.GetAccessibleApiKeysQuery(userId)
            .Where(ak => ak.Scope == ApiKeyScope.WebApp
                && ak.IsActive
                && (!ak.ExpiresAt.HasValue || ak.ExpiresAt.Value > DateTime.UtcNow))
            .OrderByDescending(ak => ak.CreatedAt)
            .FirstOrDefaultAsync();
    }

    private static string HashApiKey(string apiKey)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToBase64String(hashedBytes);
    }
}
