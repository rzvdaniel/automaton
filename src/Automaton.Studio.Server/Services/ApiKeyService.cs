using Automaton.Core.Enums;
using Automaton.Studio.Server.Constants;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Automaton.Studio.Server.Services;

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
        string? description = null,
        DateTime? expiresAt = null,
        ApiKeyScope scope = ApiKeyScope.Runner,
        Guid? projectId = null,
        bool saveChanges = true)
    {
        // Generate a cryptographically secure API key
        var rawKey = GenerateRawKey();
        var prefix = scope switch
        {
            ApiKeyScope.ReadOnly => "ak_readonly_",
            ApiKeyScope.Execute => "ak_execute_",
            ApiKeyScope.Runner => "ak_runner_",
            ApiKeyScope.WebApp => "ak_webapp_",
            ApiKeyScope.FullAccess => "ak_fullaccess_",
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
