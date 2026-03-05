using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Automaton.Studio.Server.Services;

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

    public async Task<AiApiKey> CreateAiApiKeyAsync(Guid userId, string name, string providerName, string apiKey, string? description = null, Guid? projectId = null)
    {
        // Encrypt the API key for storage
        var encryptedApiKey = EncryptApiKey(apiKey);

        // Optionally look up AiProvider by name for FK
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
            IsActive = true
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

        logger.LogInformation("AI API key created for provider {ProviderName}", providerName);
        return entity;
    }

    public async Task<IEnumerable<AiApiKey>> GetUserAiApiKeysAsync(Guid userId, Guid? projectId = null)
    {
        // Use authorization service query to get AI API keys filtered by project context
        return await authorizationService.GetAiApiKeysQueryByContext(projectId, userId)
            .OrderByDescending(ak => ak.CreatedAt)
            .ToListAsync();
    }

    public async Task<AiApiKey?> GetAiApiKeyAsync(Guid id, Guid userId)
    {
        // For system-level keys, just check if the key exists and is active
        return await dbContext.AiApiKeys
            .FirstOrDefaultAsync(ak => ak.Id == id && ak.IsActive);
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
    /// Get decrypted API key for a specific provider (returns first active key found)
    /// Used by AI conversation streaming - now uses system-level keys
    /// </summary>
    public async Task<string?> GetDecryptedApiKeyForProviderAsync(Guid userId, string providerName)
    {
        // Get system-level keys for this provider (active keys only)
        var keys = await dbContext.AiApiKeys
            .Where(k => k.ProviderName == providerName && k.IsActive)
            .OrderByDescending(k => k.LastUsedAt ?? k.CreatedAt)
            .ToListAsync();

        var firstActiveKey = keys.FirstOrDefault();

        if (firstActiveKey == null)
        {
            logger.LogWarning("No active system-level API key found for provider {ProviderName}", providerName);
            return null;
        }

        try
        {
            // Update last used timestamp
            firstActiveKey.LastUsedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();

            var decryptedKey = DecryptApiKey(firstActiveKey.EncryptedApiKey);

            return decryptedKey;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decrypt API key for provider {ProviderName} (key id: {KeyId}). " +
                "This usually means the AiApiEncryptionKey in appsettings does not match the key used when the API key was stored.",
                providerName, firstActiveKey.Id);
            return null;
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

        logger.LogInformation("AI API key {AiApiKeyId} updated", id);
        return true;
    }

    public async Task<bool> DeleteAiApiKeyAsync(Guid id, Guid userId)
    {
        var aiApiKey = await GetAiApiKeyAsync(id, userId);
        if (aiApiKey == null) return false;

        aiApiKey.IsActive = false;
        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI API key {AiApiKeyId} deactivated", id);
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
    public Dictionary<string, string?> GetEnvironmentApiKeys()
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
    /// Now returns system-level keys only
    /// </summary>
    public async Task<IEnumerable<AiProviderInfo>> GetAvailableProvidersAsync(Guid userId, bool includeEnvironment = false)
    {
        var providers = new List<AiProviderInfo>();

        // Get system-level keys
        var storedKeys = await dbContext.AiApiKeys
            .Where(k => k.IsActive)
            .ToListAsync();
        var groupedKeys = storedKeys.GroupBy(k => k.ProviderName, StringComparer.OrdinalIgnoreCase);

        // Get environment keys (for desktop)
        var envKeys = includeEnvironment ? GetEnvironmentApiKeys() : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // Get active AI providers from database with their models
        var dbProviders = await dbContext.AiProviders
            .Where(p => p.IsActive)
            .Include(p => p.Models.Where(m => m.IsActive))
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        foreach (var dbProvider in dbProviders)
        {
            var storedKeysForProvider = groupedKeys
                .FirstOrDefault(g => string.Equals(g.Key, dbProvider.Name, StringComparison.OrdinalIgnoreCase))
                ?.ToList() ?? new List<AiApiKey>();
            var hasEnvironmentKey = envKeys.TryGetValue(dbProvider.Name, out var envKey) && !string.IsNullOrEmpty(envKey);

            providers.Add(new AiProviderInfo
            {
                ProviderName = dbProvider.Name,
                DisplayName = dbProvider.DisplayName,
                Description = dbProvider.Description,
                IconName = dbProvider.IconName,
                ApiKeyHelpUrl = dbProvider.ApiKeyHelpUrl,
                HasStoredKeys = storedKeysForProvider.Any(),
                StoredKeyCount = storedKeysForProvider.Count,
                HasEnvironmentKey = hasEnvironmentKey,
                StoredKeys = storedKeysForProvider.Select(k => new AiApiKeyModel
                {
                    Id = k.Id,
                    Name = k.Name,
                    ProviderName = k.ProviderName,
                    Description = k.Description,
                    CreatedAt = k.CreatedAt,
                    LastUsedAt = k.LastUsedAt,
                    IsActive = k.IsActive
                }).ToList(),
                SupportedModels = dbProvider.Models
                    .OrderByDescending(m => m.IsDefault)
                    .ThenBy(m => m.SortOrder)
                    .Select(m => m.ModelId)
                    .ToList(),
                DefaultModel = dbProvider.Models
                    .Where(m => m.IsDefault)
                    .Select(m => m.ModelId)
                    .FirstOrDefault()
            });
        }

        // Only return providers that have keys configured (stored or environment)
        return providers
            .Where(p => p.HasStoredKeys || p.HasEnvironmentKey)
            .ToList();
    }

    public async Task<string?> GetFirstProviderWithActiveKeyAsync()
    {
        return await dbContext.AiProviders
            .Where(p => p.IsActive)
            .Where(p => dbContext.AiApiKeys.Any(k => k.IsActive && k.ProviderName == p.Name))
            .OrderBy(p => p.SortOrder)
            .Select(p => p.Name)
            .FirstOrDefaultAsync();
    }

    public bool HasAnyActiveKeys()
    {
        return dbContext.AiApiKeys.Any(k => k.IsActive);
    }

    private string EncryptApiKey(string apiKey)
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

    public bool HasStoredKeys { get; set; }
    public int StoredKeyCount { get; set; }
    public bool HasEnvironmentKey { get; set; }
    public List<AiApiKeyModel> StoredKeys { get; set; } = new();
    public List<string> SupportedModels { get; set; } = new();
    
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