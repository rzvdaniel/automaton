namespace Automaton.Api.Services;

/// <summary>
/// System-level AI service that uses Automaton's API key to provide
/// AI capabilities to all users. Used for utility tasks like app name
/// generation, suggestions, etc.
/// </summary>
public interface ISystemAiService
{
    /// <summary>
    /// Generate a short, catchy app name from a user description.
    /// </summary>
    /// <param name="description">The user's description of the app</param>
    /// <param name="userId">Optional user ID for rate limiting and tracking</param>
    /// <returns>A generated app name (2-4 words)</returns>
    Task<string> GenerateAppNameAsync(string description, string? userId = null);

    /// <summary>
    /// Generate a concise session name from a chat prompt.
    /// </summary>
    /// <param name="prompt">The first message/prompt in the session</param>
    /// <param name="userId">Optional user ID for rate limiting and tracking</param>
    /// <returns>A generated session name (3-5 words)</returns>
    Task<string> GenerateSessionNameAsync(string prompt, string? userId = null);

    /// <summary>
    /// Summarize what an AI generation changed in a web app, given the user's prompt and the AI's final response.
    /// </summary>
    /// <param name="userPrompt">The user's original request</param>
    /// <param name="aiResponse">The AI's final response message</param>
    /// <param name="userId">Optional user ID for rate limiting and tracking</param>
    /// <returns>JSON string with overview and changes array</returns>
    Task<string> SummarizeGenerationAsync(string userPrompt, string aiResponse, string? userId = null);

    /// <summary>
    /// General-purpose AI completion for utility tasks.
    /// </summary>
    /// <param name="prompt">The prompt to send to the AI</param>
    /// <param name="systemPrompt">Optional system prompt for context</param>
    /// <param name="userId">Optional user ID for rate limiting and tracking</param>
    /// <returns>The AI's response</returns>
    Task<string> CompleteAsync(string prompt, string? systemPrompt = null, string? userId = null);

    /// <summary>
    /// Check if the system AI service is available and configured.
    /// </summary>
    bool IsAvailable { get; }
}
