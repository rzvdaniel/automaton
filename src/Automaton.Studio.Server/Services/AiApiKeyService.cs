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

    public AiApiKeyService(ApplicationDbContext dbContext, ILogger<AiApiKeyService> logger, ConfigurationService configuration)
    {
        this.dbContext = dbContext;
        this.logger = logger;
        this.configuration = configuration;
    }

    public async Task<AiApiKey> CreateAiApiKeyAsync(Guid userId, string name, AiProvider provider, string apiKey, string? description = null)
    {
        // Encrypt the API key for storage
        var encryptedApiKey = EncryptApiKey(apiKey);

        var entity = new AiApiKey
        {
            Id = Guid.NewGuid(),
            Name = name,
            Provider = provider,
            EncryptedApiKey = encryptedApiKey,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            Description = description,
            IsActive = true
        };

        dbContext.AiApiKeys.Add(entity);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI API key created for user {UserId} with provider {Provider}", userId, provider);
        return entity;
    }

    public async Task<IEnumerable<AiApiKey>> GetUserAiApiKeysAsync(Guid userId)
    {
        return await dbContext.AiApiKeys
            .Where(ak => ak.UserId == userId && ak.IsActive)
            .OrderByDescending(ak => ak.CreatedAt)
            .ToListAsync();
    }

    public async Task<AiApiKey?> GetAiApiKeyAsync(Guid id, Guid userId)
    {
        return await dbContext.AiApiKeys
            .FirstOrDefaultAsync(ak => ak.Id == id && ak.UserId == userId && ak.IsActive);
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
        var aiApiKey = await GetAiApiKeyAsync(id, userId);
        if (aiApiKey == null) return false;

        aiApiKey.IsActive = false;
        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI API key {AiApiKeyId} deactivated for user {UserId}", id, userId);
        return true;
    }

    public async Task<IEnumerable<AiApiKey>> GetAiApiKeysByProviderAsync(Guid userId, AiProvider provider)
    {
        return await dbContext.AiApiKeys
            .Where(ak => ak.UserId == userId && ak.Provider == provider && ak.IsActive)
            .OrderByDescending(ak => ak.LastUsedAt ?? ak.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// For Desktop environments - read from environment variables
    /// </summary>
    public Dictionary<AiProvider, string?> GetEnvironmentApiKeys()
    {
        var environmentKeys = new Dictionary<AiProvider, string?>();

        // Check common environment variable names for each provider
        environmentKeys[AiProvider.OpenAI] = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        environmentKeys[AiProvider.Anthropic] = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") 
            ?? Environment.GetEnvironmentVariable("CLAUDE_API_KEY");
        environmentKeys[AiProvider.Google] = Environment.GetEnvironmentVariable("GOOGLE_AI_API_KEY") 
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        environmentKeys[AiProvider.Azure] = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

        return environmentKeys;
    }

    /// <summary>
    /// Get available AI providers with their key status (for UI selection)
    /// </summary>
    public async Task<IEnumerable<AiProviderInfo>> GetAvailableProvidersAsync(Guid userId, bool includeEnvironment = false)
    {
        var providers = new List<AiProviderInfo>();

        // Get stored keys
        var storedKeys = await GetUserAiApiKeysAsync(userId);
        var groupedKeys = storedKeys.GroupBy(k => k.Provider);

        // Get environment keys (for desktop)
        var envKeys = includeEnvironment ? GetEnvironmentApiKeys() : new Dictionary<AiProvider, string?>();

        foreach (AiProvider provider in Enum.GetValues<AiProvider>())
        {
            var storedKeysForProvider = groupedKeys.FirstOrDefault(g => g.Key == provider)?.ToList() ?? new List<AiApiKey>();
            var hasEnvironmentKey = envKeys.TryGetValue(provider, out var envKey) && !string.IsNullOrEmpty(envKey);

            providers.Add(new AiProviderInfo
            {
                Provider = provider,
                ProviderName = GetProviderDisplayName(provider),
                HasStoredKeys = storedKeysForProvider.Any(),
                StoredKeyCount = storedKeysForProvider.Count,
                HasEnvironmentKey = hasEnvironmentKey,
                StoredKeys = storedKeysForProvider,
                SupportedModels = GetSupportedModels(provider)
            });
        }

        return providers.Where(p => p.HasStoredKeys || p.HasEnvironmentKey || p.Provider != AiProvider.Custom)
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

    private static string GetProviderDisplayName(AiProvider provider) => provider switch
    {
        AiProvider.OpenAI => "OpenAI (GPT)",
        AiProvider.Anthropic => "Anthropic (Claude)", 
        AiProvider.Google => "Google (Gemini)",
        AiProvider.Azure => "Azure OpenAI",
        AiProvider.Custom => "Custom Provider",
        _ => provider.ToString()
    };

    private static List<string> GetSupportedModels(AiProvider provider) => provider switch
    {
        AiProvider.OpenAI => new List<string> { "gpt-4", "gpt-4-turbo", "gpt-3.5-turbo" },
        AiProvider.Anthropic => new List<string> { "claude-3-sonnet", "claude-3-haiku", "claude-3-opus" },
        AiProvider.Google => new List<string> { "gemini-pro", "gemini-pro-vision", "gemini-1.5-pro" },
        AiProvider.Azure => new List<string> { "gpt-4", "gpt-35-turbo" },
        AiProvider.Custom => new List<string> { "custom-model" },
        _ => new List<string>()
    };
}

public class AiProviderInfo
{
    public AiProvider Provider { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public bool HasStoredKeys { get; set; }
    public int StoredKeyCount { get; set; }
    public bool HasEnvironmentKey { get; set; }
    public List<AiApiKey> StoredKeys { get; set; } = new();
    public List<string> SupportedModels { get; set; } = new();
}