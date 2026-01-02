using Automaton.Studio.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Automaton.Studio.Services;

/// <summary>
/// Client-side service for managing AI chat history
/// </summary>
public class AiChatHistoryService
{
    private readonly HttpClient httpClient;
    private readonly ILogger<AiChatHistoryService> logger;

    public AiChatHistoryService(HttpClient httpClient, ILogger<AiChatHistoryService> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    /// <summary>
    /// Get all chat sessions for a specific resource
    /// </summary>
    public async Task<IEnumerable<ChatSessionModel>> GetSessionsAsync(ChatResourceType resourceType, Guid resourceId)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/AiChatHistory/sessions?resourceType={resourceType}&resourceId={resourceId}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var sessions = JsonSerializer.Deserialize<IEnumerable<ChatSessionModel>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return sessions ?? new List<ChatSessionModel>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching chat sessions for {ResourceType} {ResourceId}", resourceType, resourceId);
            throw;
        }
    }

    /// <summary>
    /// Get a specific chat session by ID
    /// </summary>
    public async Task<ChatSessionModel?> GetSessionAsync(Guid sessionId)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/AiChatHistory/sessions/{sessionId}");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var session = JsonSerializer.Deserialize<ChatSessionModel>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return session;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching session {SessionId}", sessionId);
            return null;
        }
    }

    /// <summary>
    /// Get messages for a specific session
    /// </summary>
    public async Task<IEnumerable<ChatMessageModel>> GetMessagesAsync(Guid sessionId)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/AiChatHistory/sessions/{sessionId}/messages");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var messages = JsonSerializer.Deserialize<IEnumerable<ChatMessageModel>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return messages ?? new List<ChatMessageModel>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching messages for session {SessionId}", sessionId);
            throw;
        }
    }

    /// <summary>
    /// Get session with all its messages
    /// </summary>
    public async Task<ChatSessionWithMessagesModel?> GetSessionWithMessagesAsync(Guid sessionId)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/AiChatHistory/sessions/{sessionId}/full");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ChatSessionWithMessagesModel>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching session with messages {SessionId}", sessionId);
            return null;
        }
    }

    /// <summary>
    /// Get or create the active session for a resource
    /// </summary>
    public async Task<ChatSessionModel?> GetOrCreateActiveSessionAsync(
        ChatResourceType resourceType,
        Guid resourceId,
        string? provider = null,
        string? model = null)
    {
        try
        {
            var url = $"api/AiChatHistory/sessions/active?resourceType={resourceType}&resourceId={resourceId}";
            if (!string.IsNullOrEmpty(provider))
            {
                url += $"&provider={provider}";
            }
            if (!string.IsNullOrEmpty(model))
            {
                url += $"&model={model}";
            }

            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var session = JsonSerializer.Deserialize<ChatSessionModel>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return session;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting or creating active session for {ResourceType} {ResourceId}", resourceType, resourceId);
            return null;
        }
    }

    /// <summary>
    /// Create a new chat session
    /// </summary>
    public async Task<ChatSessionModel?> CreateSessionAsync(CreateChatSessionRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync("api/AiChatHistory/sessions", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var session = JsonSerializer.Deserialize<ChatSessionModel>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return session;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating chat session");
            return null;
        }
    }

    /// <summary>
    /// Set a session as the active session
    /// </summary>
    public async Task<bool> SetActiveSessionAsync(Guid sessionId)
    {
        try
        {
            var response = await httpClient.PutAsync($"api/AiChatHistory/sessions/{sessionId}/activate", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting session {SessionId} as active", sessionId);
            return false;
        }
    }

    /// <summary>
    /// Update session title
    /// </summary>
    public async Task<bool> UpdateSessionTitleAsync(Guid sessionId, string title)
    {
        try
        {
            var request = new UpdateSessionTitleRequest { Title = title };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PutAsync($"api/AiChatHistory/sessions/{sessionId}/title", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating session {SessionId} title", sessionId);
            return false;
        }
    }

    /// <summary>
    /// Delete a chat session and all its messages
    /// </summary>
    public async Task<bool> DeleteSessionAsync(Guid sessionId)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"api/AiChatHistory/sessions/{sessionId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting session {SessionId}", sessionId);
            return false;
        }
    }

    /// <summary>
    /// Save a single chat message to a session
    /// </summary>
    public async Task<ChatMessageModel?> SaveMessageAsync(SaveChatMessageRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync("api/AiChatHistory/messages", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var message = JsonSerializer.Deserialize<ChatMessageModel>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving message");
            return null;
        }
    }

    /// <summary>
    /// Save multiple chat messages to a session (batch operation)
    /// </summary>
    public async Task<IEnumerable<ChatMessageModel>> SaveMessagesAsync(SaveChatMessagesRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync("api/AiChatHistory/messages/batch", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var messages = JsonSerializer.Deserialize<IEnumerable<ChatMessageModel>>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return messages ?? new List<ChatMessageModel>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving messages");
            return new List<ChatMessageModel>();
        }
    }

    /// <summary>
    /// Clear all messages from a session (start new conversation in same session)
    /// </summary>
    public async Task<bool> ClearSessionMessagesAsync(Guid sessionId)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"api/AiChatHistory/sessions/{sessionId}/messages");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error clearing session {SessionId} messages", sessionId);
            return false;
        }
    }
}
