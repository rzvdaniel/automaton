namespace Automaton.Api.Services;

/// <summary>
/// Service for rate limiting system AI requests.
/// Shared across utility endpoints and chat conversations.
/// </summary>
public interface ISystemAiRateLimitService
{
    /// <summary>
    /// Check if a user is within their rate limit and record the request.
    /// </summary>
    /// <param name="userId">The user ID to check</param>
    /// <returns>True if within limit, false if exceeded</returns>
    bool CheckAndRecordRequest(string userId);

    /// <summary>
    /// Get the remaining requests for a user in the current window.
    /// </summary>
    /// <param name="userId">The user ID to check</param>
    /// <returns>Number of remaining requests</returns>
    int GetRemainingRequests(string userId);
}
