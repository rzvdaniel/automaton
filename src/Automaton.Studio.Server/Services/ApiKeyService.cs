using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Automaton.Studio.Server.Services;

public class ApiKeyService
{
    private readonly ApplicationDbContext dbContext;
    private readonly ILogger<ApiKeyService> logger;
    private readonly ResourceAuthorizationService authorizationService;

    public ApiKeyService(
        ApplicationDbContext dbContext, 
        ILogger<ApiKeyService> logger,
        ResourceAuthorizationService authorizationService)
    {
        this.dbContext = dbContext;
        this.logger = logger;
        this.authorizationService = authorizationService;
    }

    public async Task<(string apiKey, ApiKey entity)> CreateApiKeyAsync(Guid userId, string name, string? description = null, DateTime? expiresAt = null, ApiKeyScope scope = ApiKeyScope.Runner, Guid? projectId = null)
    {
        // Generate a cryptographically secure API key
        var keyBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(keyBytes);
        }

        var rawKey = Convert.ToBase64String(keyBytes).Replace("+", "").Replace("/", "").Replace("=", "")[..40];
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
        
        await dbContext.SaveChangesAsync();

        logger.LogInformation("API key created for user {UserId} with scope {Scope}", userId, scope);

        return (fullApiKey, entity);
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

    public async Task<IEnumerable<ApiKey>> GetUserApiKeysAsync(Guid userId)
    {
        // Use authorization service query to get accessible API keys
        return await authorizationService.GetAccessibleApiKeysQuery(userId)
            .OrderByDescending(ak => ak.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> RevokeApiKeyAsync(Guid apiKeyId, Guid userId)
    {
        // Check if user can delete (revoke) this API key
        if (!await authorizationService.CanDeleteApiKeyAsync(apiKeyId, userId))
        {
            logger.LogWarning("User {UserId} attempted to revoke API key {ApiKeyId} without permission", userId, apiKeyId);
            return false;
        }

        var apiKey = await authorizationService.GetAccessibleApiKeysQuery(userId)
            .FirstOrDefaultAsync(ak => ak.Id == apiKeyId);

        if (apiKey == null)
            return false;

        apiKey.IsActive = false;
        await dbContext.SaveChangesAsync();

        logger.LogInformation("API key revoked: {KeyPrefix} for user {UserId}", apiKey.KeyPrefix, userId);
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