using Automaton.Studio.Models;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services.Mcp;

/// <summary>
/// Shared service for maintaining current web app context across components.
/// Similar to CurrentStepContextService but for Apps.
/// </summary>
public interface ICurrentWebAppContextService
{
    /// <summary>
    /// Set the current web app being edited
    /// </summary>
    void SetCurrentWebApp(AppModel app, Func<Task<string>>? getCurrentCodeCallback = null);
    
    /// <summary>
    /// Get the current web app's ID
    /// </summary>
    string? GetCurrentWebAppId();
    
    /// <summary>
    /// Get the current web app's HTML code
    /// </summary>
    Task<string> GetCurrentCodeAsync();
    
    /// <summary>
    /// Update the web app's HTML code
    /// </summary>
    Task SetCodeAsync(string code);
    
    /// <summary>
    /// Event fired when web app context changes
    /// </summary>
    event EventHandler<WebAppContextChangedEventArgs>? WebAppContextChanged;

    /// <summary>
    /// Event fired when code is updated (e.g., by MCP tools)
    /// </summary>
    event EventHandler<WebAppCodeChangedEventArgs>? CodeChanged;
}

/// <summary>
/// Implementation of the current web app context service
/// </summary>
public class CurrentWebAppContextService : ICurrentWebAppContextService
{
    private AppModel? _currentApp;
    private Func<Task<string>>? _getCurrentCodeCallback;
    private readonly ILogger<CurrentWebAppContextService> _logger;

    public event EventHandler<WebAppCodeChangedEventArgs>? CodeChanged;

    public CurrentWebAppContextService(ILogger<CurrentWebAppContextService> logger)
    {
        _logger = logger;
    }

    public event EventHandler<WebAppContextChangedEventArgs>? WebAppContextChanged;

    public void SetCurrentWebApp(AppModel app, Func<Task<string>>? getCurrentCodeCallback = null)
    {
        _currentApp = app;
        _getCurrentCodeCallback = getCurrentCodeCallback;
        
        _logger.LogDebug("Current web app context set: {AppId}", app.Id);
        
        WebAppContextChanged?.Invoke(this, new WebAppContextChangedEventArgs
        {
            AppId = app.Id.ToString()
        });
    }

    public string? GetCurrentWebAppId()
    {
        return _currentApp?.Id.ToString();
    }

    public async Task<string> GetCurrentCodeAsync()
    {
        try
        {
            // First try to get code from the live editor callback
            if (_getCurrentCodeCallback != null)
            {
                var liveCode = await _getCurrentCodeCallback();
                if (!string.IsNullOrEmpty(liveCode))
                {
                    return liveCode;
                }
            }
            
            // Fallback to stored code in the app definition
            return _currentApp?.Definition?.HtmlContent ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get current code from callback, using fallback");
            return _currentApp?.Definition?.HtmlContent ?? string.Empty;
        }
    }

    public async Task SetCodeAsync(string code)
    {
        if (_currentApp?.Definition != null)
        {
            _currentApp.Definition.HtmlContent = code;
            _logger.LogDebug("Code updated for web app {AppId}", _currentApp.Id);

            // Notify subscribers that code has changed (e.g., Monaco editor in UI)
            CodeChanged?.Invoke(this, new WebAppCodeChangedEventArgs
            {
                AppId = _currentApp.Id.ToString(),
                NewCode = code
            });
        }
        else
        {
            // Log diagnostic information to help identify the timing issue
            _logger.LogWarning("SetCodeAsync called but _currentApp is null or has no Definition. " +
                "Code length: {CodeLength}. This typically happens when SetCurrentWebApp() wasn't called before the AI conversation started, " +
                "or if the Blazor circuit was reset. Check that AppPage.OnInitializedAsync() completes before AI chat interaction.",
                code?.Length ?? 0);
            
            // Even though _currentApp is null, still fire the CodeChanged event with the code
            // This allows any subscribers to still receive the code update
            CodeChanged?.Invoke(this, new WebAppCodeChangedEventArgs
            {
                AppId = null,  // Unknown app ID - subscriber should use code directly
                NewCode = code
            });
        }

        await Task.CompletedTask;
    }
}

/// <summary>
/// Event arguments for web app context changes
/// </summary>
public class WebAppContextChangedEventArgs : EventArgs
{
    public string? AppId { get; set; }
}

/// <summary>
/// Event arguments for web app code changes
/// </summary>
public class WebAppCodeChangedEventArgs : EventArgs
{
    public string? AppId { get; set; }
    public string NewCode { get; set; } = string.Empty;
}
