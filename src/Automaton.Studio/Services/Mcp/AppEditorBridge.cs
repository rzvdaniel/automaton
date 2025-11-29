using Automaton.Studio.Models;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services.Mcp;

/// <summary>
/// Implementation of the current app context service
/// </summary>
public class AppEditorBridge : IAppEditorBridge
{
    private AppModel? _currentApp;
    private Func<Task<string>>? _getCurrentCodeCallback;
    private readonly ILogger<AppEditorBridge> _logger;

    public event EventHandler<AppCodeChangedEventArgs>? CodeChanged;

    public AppEditorBridge(ILogger<AppEditorBridge> logger)
    {
        _logger = logger;
    }

    public event EventHandler<AppEditorBridgeChangedEventArgs>? WebAppContextChanged;

    public void SetCurrentApp(AppModel app, Func<Task<string>>? getCurrentCodeCallback = null)
    {
        _currentApp = app;
        _getCurrentCodeCallback = getCurrentCodeCallback;
        
        _logger.LogDebug("Current web app context set: {AppId}", app.Id);
        
        WebAppContextChanged?.Invoke(this, new AppEditorBridgeChangedEventArgs
        {
            AppId = app.Id.ToString()
        });
    }

    public string? GetCurrentAppId()
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
            CodeChanged?.Invoke(this, new AppCodeChangedEventArgs
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
            CodeChanged?.Invoke(this, new AppCodeChangedEventArgs
            {
                AppId = null,  // Unknown app ID - subscriber should use code directly
                NewCode = code
            });
        }

        await Task.CompletedTask;
    }
}

/// <summary>
/// Event arguments for app context changes
/// </summary>
public class AppEditorBridgeChangedEventArgs : EventArgs
{
    public string? AppId { get; set; }
}

/// <summary>
/// Event arguments for app code changes
/// </summary>
public class AppCodeChangedEventArgs : EventArgs
{
    public string? AppId { get; set; }
    public string NewCode { get; set; } = string.Empty;
}
