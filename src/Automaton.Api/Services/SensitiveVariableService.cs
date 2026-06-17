using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Automaton.Api.Services;

/// <summary>
/// Service for managing sensitive variables with encryption.
/// Uses the same encryption method as AiApiKeyService for consistency.
/// </summary>
public class SensitiveVariableService
{
    private readonly ApplicationDbContext dbContext;
    private readonly ILogger<SensitiveVariableService> logger;
    private readonly ConfigurationService configuration;
    private readonly ResourceAuthorizationService authorizationService;

    public SensitiveVariableService(
        ApplicationDbContext dbContext,
        ILogger<SensitiveVariableService> logger,
        ConfigurationService configuration,
        ResourceAuthorizationService authorizationService)
    {
        this.dbContext = dbContext;
        this.logger = logger;
        this.configuration = configuration;
        this.authorizationService = authorizationService;
    }

    /// <summary>
    /// Create or update a sensitive variable
    /// </summary>
    public async Task<SensitiveVariable> CreateOrUpdateSensitiveVariableAsync(
        Guid userId,
        string name,
        string value,
        string resourceType,
        Guid resourceId,
        string variableScope,
        string? description = null,
        Guid? existingId = null)
    {
        // Validate resource access
        var canAccess = resourceType switch
        {
            SensitiveVariableResourceType.Flow => 
                await authorizationService.CanUpdateFlowAsync(resourceId, userId),
            SensitiveVariableResourceType.CustomStep => 
                await authorizationService.CanUpdateCustomStepAsync(resourceId, userId),
            _ => false
        };

        if (!canAccess)
        {
            throw new UnauthorizedAccessException($"User does not have permission to access {resourceType} {resourceId}");
        }

        // Encrypt the value
        var encryptedValue = EncryptValue(value);

        SensitiveVariable sensitiveVar;

        if (existingId.HasValue)
        {
            // Update existing
            sensitiveVar = await dbContext.SensitiveVariables
                .FirstOrDefaultAsync(sv => sv.Id == existingId.Value);

            if (sensitiveVar == null)
            {
                throw new InvalidOperationException($"Sensitive variable {existingId.Value} not found");
            }

            sensitiveVar.Name = name;
            sensitiveVar.EncryptedValue = encryptedValue;
            sensitiveVar.Description = description;
            sensitiveVar.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // Create new
            sensitiveVar = new SensitiveVariable
            {
                Id = Guid.NewGuid(),
                Name = name,
                EncryptedValue = encryptedValue,
                Description = description,
                ResourceType = resourceType,
                ResourceId = resourceId,
                VariableScope = variableScope,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            };

            dbContext.SensitiveVariables.Add(sensitiveVar);

            // Create user link
            var sensitiveVarUser = new SensitiveVariableUser
            {
                SensitiveVariableId = sensitiveVar.Id,
                UserId = userId
            };
            dbContext.SensitiveVariableUsers.Add(sensitiveVarUser);
        }

        await dbContext.SaveChangesAsync();

        logger.LogDebug("Sensitive variable {Id} created/updated for {ResourceType} {ResourceId}",
            sensitiveVar.Id, resourceType, resourceId);

        return sensitiveVar;
    }

    /// <summary>
    /// Get decrypted value of a sensitive variable
    /// </summary>
    public async Task<string?> GetDecryptedValueAsync(Guid id, Guid userId)
    {
        var sensitiveVar = await dbContext.SensitiveVariables
            .FirstOrDefaultAsync(sv => sv.Id == id && sv.IsActive);

        if (sensitiveVar == null)
        {
            logger.LogWarning("Sensitive variable {Id} not found", id);
            return null;
        }

        // Verify user has access to the parent resource
        var canAccess = sensitiveVar.ResourceType switch
        {
            SensitiveVariableResourceType.Flow => 
                await authorizationService.CanAccessFlowAsync(sensitiveVar.ResourceId, userId),
            SensitiveVariableResourceType.CustomStep => 
                await authorizationService.CanAccessCustomStepAsync(sensitiveVar.ResourceId, userId),
            _ => false
        };

        if (!canAccess)
        {
            logger.LogWarning("User {UserId} attempted to access sensitive variable {Id} without permission", 
                userId, id);
            return null;
        }

        try
        {
            // Update last used timestamp
            sensitiveVar.LastUsedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();

            return DecryptValue(sensitiveVar.EncryptedValue);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decrypt sensitive variable {Id}", id);
            return null;
        }
    }

    /// <summary>
    /// Get all sensitive variables for a resource
    /// </summary>
    public async Task<List<SensitiveVariable>> GetSensitiveVariablesForResourceAsync(
        string resourceType,
        Guid resourceId,
        Guid userId)
    {
        // Verify user has access to the parent resource
        var canAccess = resourceType switch
        {
            SensitiveVariableResourceType.Flow => 
                await authorizationService.CanAccessFlowAsync(resourceId, userId),
            SensitiveVariableResourceType.CustomStep => 
                await authorizationService.CanAccessCustomStepAsync(resourceId, userId),
            _ => false
        };

        if (!canAccess)
        {
            return new List<SensitiveVariable>();
        }

        return await dbContext.SensitiveVariables
            .Where(sv => sv.ResourceType == resourceType && 
                        sv.ResourceId == resourceId && 
                        sv.IsActive)
            .OrderBy(sv => sv.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Delete a sensitive variable
    /// </summary>
    public async Task<bool> DeleteSensitiveVariableAsync(Guid id, Guid userId)
    {
        var sensitiveVar = await dbContext.SensitiveVariables
            .FirstOrDefaultAsync(sv => sv.Id == id && sv.IsActive);

        if (sensitiveVar == null)
        {
            return false;
        }

        // Verify user has access to the parent resource
        var canUpdate = sensitiveVar.ResourceType switch
        {
            SensitiveVariableResourceType.Flow => 
                await authorizationService.CanUpdateFlowAsync(sensitiveVar.ResourceId, userId),
            SensitiveVariableResourceType.CustomStep => 
                await authorizationService.CanUpdateCustomStepAsync(sensitiveVar.ResourceId, userId),
            _ => false
        };

        if (!canUpdate)
        {
            logger.LogWarning("User {UserId} attempted to delete sensitive variable {Id} without permission",
                userId, id);
            return false;
        }

        sensitiveVar.IsActive = false;
        sensitiveVar.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        logger.LogDebug("Sensitive variable {Id} deleted", id);
        return true;
    }

    /// <summary>
    /// Batch decrypt sensitive variable references in a dictionary
    /// Used when Runner needs to execute workflows
    /// </summary>
    public async Task<Dictionary<string, string>> DecryptVariableBatchAsync(
        Dictionary<string, string> variables,
        Guid userId)
    {
        var result = new Dictionary<string, string>();

        foreach (var kvp in variables)
        {
            // Check if value is a sensitive variable reference (format: "${sensitive:guid}")
            if (kvp.Value?.StartsWith("${sensitive:") == true && kvp.Value.EndsWith("}"))
            {
                var guidPart = kvp.Value.Substring(12, kvp.Value.Length - 13); // Extract GUID
                if (Guid.TryParse(guidPart, out var sensitiveVarId))
                {
                    var decryptedValue = await GetDecryptedValueAsync(sensitiveVarId, userId);
                    result[kvp.Key] = decryptedValue ?? string.Empty;
                }
                else
                {
                    result[kvp.Key] = kvp.Value; // Invalid reference, keep as-is
                }
            }
            else
            {
                result[kvp.Key] = kvp.Value; // Not a sensitive reference
            }
        }

        return result;
    }

    #region Encryption/Decryption (Same as AiApiKeyService)

    private string EncryptValue(string value)
    {
        var encryptionKey = GetEncryptionKey();
        var valueBytes = Encoding.UTF8.GetBytes(value);

        using var aes = Aes.Create();
        aes.Key = encryptionKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(valueBytes, 0, valueBytes.Length);

        // Combine IV and encrypted data
        var result = new byte[aes.IV.Length + encrypted.Length];
        Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
        Array.Copy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

        return Convert.ToBase64String(result);
    }

    private string DecryptValue(string encryptedValue)
    {
        var encryptionKey = GetEncryptionKey();
        var encryptedData = Convert.FromBase64String(encryptedValue);

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
        // Use same encryption key as AiApiKeyService
        var keyString = configuration.AppConfig.AiApiEncryptionKey;
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(keyString));
    }

    #endregion
}
