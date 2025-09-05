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

    public ApiKeyService(ApplicationDbContext dbContext, ILogger<ApiKeyService> logger)
    {
        this.dbContext = dbContext;
        this.logger = logger;
    }

    public async Task<(string apiKey, ApiKey entity)> CreateApiKeyAsync(Guid userId, string name, string? description = null, DateTime? expiresAt = null, ApiKeyScope scope = ApiKeyScope.Runner)
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
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            Description = description,
            Scope = scope,
            IsActive = true
        };

        dbContext.ApiKeys.Add(entity);
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
            .Include(ak => ak.User)
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
        return await dbContext.ApiKeys
            .Where(ak => ak.UserId == userId)
            .OrderByDescending(ak => ak.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> RevokeApiKeyAsync(Guid apiKeyId, Guid userId)
    {
        var apiKey = await dbContext.ApiKeys
            .FirstOrDefaultAsync(ak => ak.Id == apiKeyId && ak.UserId == userId);

        if (apiKey == null)
            return false;

        apiKey.IsActive = false;
        await dbContext.SaveChangesAsync();

        logger.LogInformation("API key revoked: {KeyPrefix} for user {UserId}", apiKey.KeyPrefix, userId);
        return true;
    }

    private static string HashApiKey(string apiKey)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToBase64String(hashedBytes);
    }
}