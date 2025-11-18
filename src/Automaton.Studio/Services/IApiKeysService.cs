using Automaton.Studio.Models;

namespace Automaton.Studio.Services;

public interface IApiKeysService
{
    /// <summary>
    /// Get all API keys for the current user filtered by project context
    /// </summary>
    Task<List<ApiKeyResponse>> GetApiKeysAsync(Guid? projectId = null);

    /// <summary>
    /// Create a new API key
    /// </summary>
    Task<CreateApiKeyResponse> CreateApiKeyAsync(CreateApiKeyRequest request);

    /// <summary>
    /// Revoke an API key
    /// </summary>
    Task<bool> RevokeApiKeyAsync(Guid apiKeyId);
}