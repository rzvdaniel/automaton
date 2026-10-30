using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Automaton.Api.Services;

public class AiApiKeyService
{
    private readonly ApplicationDbContext dbContext;
    private readonly ILogger<AiApiKeyService> logger;
    private readonly ConfigurationService configuration;
    private readonly ResourceAuthorizationService authorizationService;

    public AiApiKeyService(
        ApplicationDbContext dbContext,
        ILogger<AiApiKeyService> logger,
        ConfigurationService configuration,
        ResourceAuthorizationService authorizationService)
    {
        this.dbContext = dbContext;
        this.logger = logger;
        this.configuration = configuration;
        this.authorizationService = authorizationService;
    }

    public async Task<AiApiKey> CreateAiApiKeyAsync(Guid userId, string name, string providerName, string apiKey, string? description = null, Guid? projectId = null, string? baseUrl = null, string? defaultModel = null)
    {
        // Encrypt the API key for storage
        var encryptedApiKey = EncryptApiKey(apiKey);

        // Look up AiProvider FK (admin-configured providers only)
        var aiProvider = await dbContext.AiProviders
            .FirstOrDefaultAsync(p => p.Name == providerName);

        var entity = new AiApiKey
        {
            Id = Guid.NewGuid(),
            Name = name,
            ProviderName = providerName,
            AiProviderId = aiProvider?.Id,
            EncryptedApiKey = encryptedApiKey,
            CreatedAt = DateTime.UtcNow,
            Description = description,
            IsActive = true,
            // Guid.Empty signals a system/admin key — store as null so the "system key" query works
            CreatedByUserId = userId == Guid.Empty ? (Guid?)null : userId,
            BaseUrl = baseUrl,
            DefaultModel = defaultModel
        };

        dbContext.AiApiKeys.Add(entity);

        // If project is specified, add to project
        if (projectId.HasValue)
        {
            var projectAiApiKey = new ProjectAiApiKey
            {
                ProjectId = projectId.Value,
                AiApiKeyId = entity.Id,
                AddedAt = DateTime.UtcNow,
                AddedBy = userId
            };
            dbContext.ProjectAiApiKeys.Add(projectAiApiKey);
        }

        await dbContext.SaveChangesAsync();

        logger.LogDebug("AI API key created by user {UserId} for provider {ProviderName}", userId, providerName);
        return entity;
    }

    public async Task<IEnumerable<AiApiKey>> GetUserAiApiKeysAsync(Guid userId, Guid? projectId = null)
    {
        if (projectId.HasValue)
        {
            // Project-scoped: use authorization service
            return await authorizationService.GetAiApiKeysQueryByContext(projectId, userId)
                .OrderByDescending(ak => ak.CreatedAt)
                .ToListAsync();
        }

        // Personal: return only keys created by this user
        return await dbContext.AiApiKeys
            .Where(ak => ak.IsActive && ak.CreatedByUserId == userId)
            .OrderByDescending(ak => ak.CreatedAt)
            .ToListAsync();
    }

    public async Task<AiApiKey?> GetAiApiKeyAsync(Guid id, Guid userId)
    {
        // User can access their own key; admin keys (CreatedByUserId == null) are not user-accessible
        return await dbContext.AiApiKeys
            .FirstOrDefaultAsync(ak => ak.Id == id && ak.IsActive && ak.CreatedByUserId == userId);
    }

    public async Task<string?> GetDecryptedApiKeyAsync(Guid id, Guid userId)
    {
        var aiApiKey = await GetAiApiKeyAsync(id, userId);
        if (aiApiKey == null) return null;

        try
        {
            // Update last used timestamp
            aiApiKey.LastUsedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();

            return DecryptApiKey(aiApiKey.EncryptedApiKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decrypt AI API key {AiApiKeyId}", id);
            return null;
        }
    }

    /// <summary>
    /// Get decrypted API key for a specific provider.
    /// Checks the user's own keys first, then falls back to admin/system keys (CreatedByUserId == null).
    /// </summary>
    public async Task<string?> GetDecryptedApiKeyForProviderAsync(Guid userId, string providerName)
    {
        var (key, _) = await GetDecryptedApiKeyWithSourceAsync(userId, providerName);
        return key;
    }

    /// <summary>
    /// Get decrypted API key for a specific provider, also indicating whether a system/admin key was used.
    /// Checks the user's own keys first, then falls back to admin/system keys (CreatedByUserId == null).
    /// </summary>
    public async Task<(string? Key, bool IsSystemKey)> GetDecryptedApiKeyWithSourceAsync(Guid userId, string providerName)
    {
        logger.LogDebug("Looking up AI API key for provider '{ProviderName}' for user {UserId}", providerName, userId);

        // 1. Prefer user's own key
        var key = await dbContext.AiApiKeys
            .Where(k => k.ProviderName == providerName && k.IsActive && k.CreatedByUserId == userId)
            .OrderByDescending(k => k.LastUsedAt ?? k.CreatedAt)
            .FirstOrDefaultAsync();

        var isSystemKey = false;

        // 2. Fall back to admin/system keys
        if (key == null)
        {
            key = await dbContext.AiApiKeys
                .Where(k => k.ProviderName == providerName && k.IsActive && k.CreatedByUserId == null)
                .OrderByDescending(k => k.LastUsedAt ?? k.CreatedAt)
                .FirstOrDefaultAsync();
            isSystemKey = key != null;
        }

        if (key == null)
        {
            logger.LogWarning("No active API key found for provider '{ProviderName}' (user {UserId} or system)", providerName, userId);
            return (null, false);
        }

        try
        {
            key.LastUsedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();

            var decryptedKey = DecryptApiKey(key.EncryptedApiKey);
            logger.LogDebug(
                "Selected AI API key {KeyId} for provider '{ProviderName}' (systemKey={IsSystemKey})",
                key.Id, providerName, isSystemKey);
            return (decryptedKey, isSystemKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decrypt API key for provider '{ProviderName}' (key id: {KeyId}). " +
                "This usually means the AiApiEncryptionKey in appsettings does not match the key used when the API key was stored.",
                providerName, key.Id);
            return (null, false);
        }
    }

    public async Task<bool> UpdateAiApiKeyAsync(Guid id, Guid userId, string? name = null, string? description = null, bool? isActive = null)
    {
        var aiApiKey = await GetAiApiKeyAsync(id, userId);
        if (aiApiKey == null) return false;

        if (name != null) aiApiKey.Name = name;
        if (description != null) aiApiKey.Description = description;
        if (isActive != null) aiApiKey.IsActive = isActive.Value;

        await dbContext.SaveChangesAsync();

        logger.LogDebug("AI API key {AiApiKeyId} updated", id);
        return true;
    }

    public async Task<bool> DeleteAiApiKeyAsync(Guid id, Guid userId)
    {
        // Users may only delete keys they own
        var aiApiKey = await dbContext.AiApiKeys
            .FirstOrDefaultAsync(ak => ak.Id == id && ak.IsActive && ak.CreatedByUserId == userId);
        if (aiApiKey == null) return false;

        aiApiKey.IsActive = false;
        await dbContext.SaveChangesAsync();

        logger.LogDebug("AI API key {AiApiKeyId} deactivated by user {UserId}", id, userId);
        return true;
    }

    public async Task<IEnumerable<AiApiKey>> GetAiApiKeysByProviderAsync(Guid userId, string providerName)
    {
        // Return system-level keys for this provider
        return await dbContext.AiApiKeys
            .Where(ak => ak.ProviderName == providerName && ak.IsActive)
            .OrderByDescending(ak => ak.LastUsedAt ?? ak.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// For Desktop environments - read from environment variables
    /// </summary>
    public static Dictionary<string, string?> GetEnvironmentApiKeys()
    {
        var environmentKeys = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // Check common environment variable names for each provider
        environmentKeys["OpenAI"] = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        environmentKeys["Anthropic"] = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? Environment.GetEnvironmentVariable("CLAUDE_API_KEY");
        environmentKeys["Google"] = Environment.GetEnvironmentVariable("GOOGLE_AI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        return environmentKeys;
    }

    /// <summary>
    /// Get available AI providers with their key status (for UI selection)
    /// Returns system-level keys (CreatedByUserId == null) and the current user's own keys.
    /// </summary>
    public async Task<IEnumerable<AiProviderInfo>> GetAvailableProvidersAsync(Guid userId, bool includeEnvironment = false, bool allProviders = false)
    {
        var environmentKeys = includeEnvironment
            ? GetEnvironmentApiKeys()
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var providers = await LoadConfiguredProvidersAsync(userId, environmentKeys);
        await AddCustomProvidersAsync(providers, userId);
        return allProviders
            ? providers
            : providers.Where(HasConfiguredKey).ToList();
    }

    private async Task<List<AiProviderInfo>> LoadConfiguredProvidersAsync(
        Guid userId,
        IReadOnlyDictionary<string, string?> environmentKeys)
    {
        var storedKeys = await dbContext.AiApiKeys
            .Where(key => key.IsActive && (key.CreatedByUserId == null || key.CreatedByUserId == userId))
            .ToListAsync();
        var keysByProvider = storedKeys
            .GroupBy(key => key.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var databaseProviders = await dbContext.AiProviders
            .Where(provider => provider.IsActive)
            .Include(provider => provider.Models.Where(model => model.IsActive))
            .OrderBy(provider => provider.SortOrder)
            .ToListAsync();

        return databaseProviders
            .Select(provider => MapConfiguredProvider(provider, keysByProvider, userId, environmentKeys))
            .ToList();
    }

    private static AiProviderInfo MapConfiguredProvider(
        AiProvider provider,
        IReadOnlyCollection<IGrouping<string, AiApiKey>> keysByProvider,
        Guid userId,
        IReadOnlyDictionary<string, string?> environmentKeys)
    {
        var allKeys = keysByProvider
            .FirstOrDefault(group => string.Equals(group.Key, provider.Name, StringComparison.OrdinalIgnoreCase))
            ?.ToList() ?? [];
        var userKeys = allKeys.Where(key => key.CreatedByUserId == userId).ToList();
        var systemKeys = allKeys.Where(key => key.CreatedByUserId == null).ToList();
        var hasEnvironmentKey = environmentKeys.TryGetValue(provider.Name, out var environmentKey)
            && !string.IsNullOrEmpty(environmentKey);

        return new AiProviderInfo
        {
            ProviderName = provider.Name,
            DisplayName = provider.DisplayName,
            Description = provider.Description,
            IconName = provider.IconName,
            ApiKeyHelpUrl = provider.ApiKeyHelpUrl,
            IsActive = provider.IsActive,
            HasStoredKeys = userKeys.Any(),
            HasSystemKey = systemKeys.Any(),
            StoredKeyCount = userKeys.Count,
            HasEnvironmentKey = hasEnvironmentKey,
            StoredKeys = userKeys.Select(key => new AiApiKeyModel
            {
                Id = key.Id,
                Name = key.Name,
                ProviderName = key.ProviderName,
                Description = key.Description,
                CreatedAt = key.CreatedAt,
                LastUsedAt = key.LastUsedAt,
                IsActive = key.IsActive
            }).ToList(),
            SupportedModels = provider.Models
                .OrderByDescending(model => model.IsDefault)
                .ThenBy(model => model.SortOrder)
                .Select(model => model.ModelId)
                .ToList(),
            ModelDisplayNames = provider.Models
                .Where(model => !string.IsNullOrWhiteSpace(model.ModelId))
                .GroupBy(model => model.ModelId)
                .ToDictionary(
                    group => group.Key,
                    group => string.IsNullOrWhiteSpace(group.First().DisplayName) ? group.Key : group.First().DisplayName),
            DefaultModel = provider.Models
                .Where(model => model.IsDefault)
                .Select(model => model.ModelId)
                .FirstOrDefault()
        };
    }

    private async Task AddCustomProvidersAsync(List<AiProviderInfo> providers, Guid userId)
    {
        var userCustomKeys = await dbContext.AiApiKeys
            .Where(key => key.IsActive && key.CreatedByUserId == userId && key.BaseUrl != null && key.DefaultModel != null)
            .ToListAsync();

        foreach (var group in userCustomKeys.GroupBy(key => key.ProviderName, StringComparer.OrdinalIgnoreCase))
        {
            if (providers.Any(provider => string.Equals(provider.ProviderName, group.Key, StringComparison.OrdinalIgnoreCase)))
                continue;

            providers.Add(MapCustomProvider(group));
        }
    }

    private static AiProviderInfo MapCustomProvider(IGrouping<string, AiApiKey> group)
    {
        var firstKey = group.First();
        return new AiProviderInfo
        {
            ProviderName = group.Key,
            DisplayName = group.Key,
            IsActive = true,
            HasStoredKeys = true,
            StoredKeyCount = group.Count(),
            SupportedModels = group.Select(key => key.DefaultModel!).Distinct().ToList(),
            DefaultModel = firstKey.DefaultModel
        };
    }

    private static bool HasConfiguredKey(AiProviderInfo provider)
        => provider.HasStoredKeys || provider.HasSystemKey || provider.HasEnvironmentKey;

    public async Task<string?> GetFirstProviderWithActiveKeyAsync()
    {
        return await dbContext.AiProviders
            .Where(p => p.IsActive)
            .Where(p => dbContext.AiApiKeys.Any(k => k.IsActive && k.ProviderName == p.Name))
            .OrderBy(p => p.SortOrder)
            .Select(p => p.Name)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Returns true when the provider has at least one active system key
    /// (a key added by an admin and available to all users).
    /// </summary>
    public async Task<bool> HasSystemApiKeyAsync(string providerName)
    {
        return await dbContext.AiApiKeys
            .AnyAsync(k => k.ProviderName == providerName && k.IsActive && k.CreatedByUserId == null);
    }

    public bool HasAnyActiveKeys()
    {
        return dbContext.AiApiKeys.Any(k => k.IsActive);
    }

    internal string EncryptApiKey(string apiKey)
    {
        // Simple encryption - in production, use more robust encryption
        var encryptionKey = GetEncryptionKey();
        var keyBytes = Encoding.UTF8.GetBytes(apiKey);
        
        using var aes = Aes.Create();
        aes.Key = encryptionKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(keyBytes, 0, keyBytes.Length);
        
        // Combine IV and encrypted data
        var result = new byte[aes.IV.Length + encrypted.Length];
        Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
        Array.Copy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
        
        return Convert.ToBase64String(result);
    }

    private string DecryptApiKey(string encryptedApiKey)
    {
        var encryptionKey = GetEncryptionKey();
        var encryptedData = Convert.FromBase64String(encryptedApiKey);
        
        using var aes = Aes.Create();
        aes.Key = encryptionKey;
        
        // Extract IV (first 16 bytes)
        var iv = new byte[16];
        Array.Copy(encryptedData, 0, iv, 0, 16);
        aes.IV = iv;
        
        // Extract encrypted content
        var encrypted = new byte[encryptedData.Length - 16];
        Array.Copy(encryptedData, 16, encrypted, 0, encrypted.Length);
        
        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        
        return Encoding.UTF8.GetString(decrypted);
    }

    private byte[] GetEncryptionKey()
    {
        var keyString = configuration.AppConfig.AiApiEncryptionKey;
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(keyString));
    }
}

public class AiProviderInfo
{
    /// <summary>
    /// Provider identifier name (e.g., "OpenAI", "Anthropic")
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Display name for UI (e.g., "OpenAI", "Anthropic Claude")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Description of the provider
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Icon name for UI (e.g., "openai", "anthropic", "google")
    /// </summary>
    public string? IconName { get; set; }

    /// <summary>
    /// URL where users can get their API key
    /// </summary>
    public string? ApiKeyHelpUrl { get; set; }
    public bool IsActive { get; set; }

    public bool HasStoredKeys { get; set; }
    public bool HasSystemKey { get; set; }
    public int StoredKeyCount { get; set; }
    public bool HasEnvironmentKey { get; set; }
    public List<AiApiKeyModel> StoredKeys { get; set; } = new();
    public List<string> SupportedModels { get; set; } = new();

    /// <summary>
    /// Mapping from model identifier to admin-configured display name.
    /// </summary>
    public Dictionary<string, string> ModelDisplayNames { get; set; } = new();
    
    /// <summary>
    /// The default model for this provider (marked as IsDefault in database)
    /// </summary>
    public string? DefaultModel { get; set; }
}

public class AiApiKeyModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; }
}
