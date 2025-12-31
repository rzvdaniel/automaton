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

        // Create personal ownership link
        var aiApiKeyUser = new AiApiKeyUser
        {
            AiApiKeyId = entity.Id,
            UserId = userId
        };
        dbContext.AiApiKeyUsers.Add(aiApiKeyUser);

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

        logger.LogInformation("AI API key created for user {UserId} with provider {ProviderName}", userId, providerName);
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
        // Check if user can access this AI API key
        if (!await authorizationService.CanAccessAiApiKeyAsync(id, userId))
        {
            logger.LogWarning("User {UserId} attempted to access AI API key {AiApiKeyId} without permission", userId, id);
            return null;
        }

        return await authorizationService.GetAccessibleAiApiKeysQuery(userId)
            .FirstOrDefaultAsync(ak => ak.Id == id);
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

    public async Task<bool> UpdateAiApiKeyAsync(Guid id, Guid userId, string? name = null, string? description = null, bool? isActive = null)
    {
        // Check if user can update this AI API key
        if (!await authorizationService.CanUpdateAiApiKeyAsync(id, userId))
        {
            logger.LogWarning("User {UserId} attempted to update AI API key {AiApiKeyId} without permission", userId, id);
            return false;
        }

        var aiApiKey = await GetAiApiKeyAsync(id, userId);
        if (aiApiKey == null) return false;

        if (name != null) aiApiKey.Name = name;
        if (description != null) aiApiKey.Description = description;
        if (isActive != null) aiApiKey.IsActive = isActive.Value;

        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI API key {AiApiKeyId} updated for user {UserId}", id, userId);
        return true;
    }

    public async Task<bool> DeleteAiApiKeyAsync(Guid id, Guid userId)
    {
        // Check if user can delete this AI API key
        if (!await authorizationService.CanDeleteAiApiKeyAsync(id, userId))
        {
            logger.LogWarning("User {UserId} attempted to delete AI API key {AiApiKeyId} without permission", userId, id);
            return false;
        }

        var aiApiKey = await GetAiApiKeyAsync(id, userId);
        if (aiApiKey == null) return false;

        aiApiKey.IsActive = false;
        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI API key {AiApiKeyId} deactivated for user {UserId}", id, userId);
        return true;
    }

    public async Task<IEnumerable<AiApiKey>> GetAiApiKeysByProviderAsync(Guid userId, string providerName)
    {
        // Use authorization service query to get accessible AI API keys
        return await authorizationService.GetAccessibleAiApiKeysQuery(userId)
            .Where(ak => ak.ProviderName == providerName)
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
    /// Now uses database instead of appsettings.json
    /// </summary>
    public async Task<IEnumerable<AiProviderInfo>> GetAvailableProvidersAsync(Guid userId, bool includeEnvironment = false)
    {
        var providers = new List<AiProviderInfo>();

        // Get stored keys
        var storedKeys = await GetUserAiApiKeysAsync(userId);
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
                HasStoredKeys = storedKeysForProvider.Any(),
                StoredKeyCount = storedKeysForProvider.Count,
                HasEnvironmentKey = hasEnvironmentKey,
                StoredKeys = storedKeysForProvider,
                SupportedModels = dbProvider.Models
                    .OrderBy(m => m.SortOrder)
                    .Select(m => m.ModelId)
                    .ToList()
            });
        }

        return providers.Where(p => p.HasStoredKeys || p.HasEnvironmentKey)
                      .OrderByDescending(p => p.HasStoredKeys || p.HasEnvironmentKey);
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

    public bool HasStoredKeys { get; set; }
    public int StoredKeyCount { get; set; }
    public bool HasEnvironmentKey { get; set; }
    public List<AiApiKey> StoredKeys { get; set; } = new();
    public List<string> SupportedModels { get; set; } = new();
}