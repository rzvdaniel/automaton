using Automaton.Studio.Models;

namespace Automaton.Studio.Services;

public interface IApiKeysService
{
    /// <summary>
    /// Get all API keys for the current user
    /// </summary>
    Task<List<ApiKeyResponse>> GetApiKeysAsync();

    /// <summary>
    /// Create a new API key
    /// </summary>
    Task<CreateApiKeyResponse> CreateApiKeyAsync(CreateApiKeyRequest request);

    /// <summary>
    /// Revoke an API key
    /// </summary>
    Task<bool> RevokeApiKeyAsync(Guid apiKeyId);
}