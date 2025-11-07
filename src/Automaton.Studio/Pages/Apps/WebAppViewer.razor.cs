using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Automaton.Studio.Pages.Apps;

partial class WebAppViewer : ComponentBase, IAsyncDisposable
{
    [Inject] AppsService AppsService { get; set; } = default!;
    [Inject] NavigationManager Navigation { get; set; } = default!;
    [Inject] AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] ILogger<WebAppViewer> Logger { get; set; } = default!;
    [Inject] IframeMessageHandler MessageHandler { get; set; } = default!;
    [Inject] IJSRuntime JSRuntime { get; set; } = default!;
    [Parameter] public string Slug { get; set; } = string.Empty;

    private bool loading = true;
    private string? error;
    private string errorTitle = "Error";
    private bool requiresLogin = false;
    private AppModel? currentApp;
    private bool handlerInitialized = false;

    // Client-side automatonApi.js script to inject into iframe
    // NOTE: APP_SLUG will be replaced with actual slug value
    private const string AutomatonApiScriptTemplate = @"
<script>
(function(window) {
  'use strict';

  // App slug is injected by the server
  const APP_SLUG = '{{APP_SLUG}}';
  console.log('[AutomatonAPI-IFRAME] Initializing for app:', APP_SLUG);

  function getAppSlug() {
    return APP_SLUG;
  }

  function generateRequestId() {
    return 'req_' + Math.random().toString(36).substring(2, 15);
  }

  function apiCall(path, method = 'GET', body = null) {
    return new Promise((resolve, reject) => {
      if (window === window.parent) {
        reject(new Error('This API can only be used within an Automaton web app iframe'));
        return;
      }

      const requestId = generateRequestId();
      const timeout = 30000;

      const payload = { id: requestId, path: path, method: method };
      if (body) {
        payload.body = typeof body === 'string' ? body : JSON.stringify(body);
      }

      console.log('[AutomatonAPI-IFRAME] Sending API request:', requestId, path);

      const timeoutId = setTimeout(() => {
        window.removeEventListener('message', responseHandler);
        reject(new Error('Request timeout after 30 seconds'));
      }, timeout);

      function responseHandler(event) {
        const { type, id, result } = event.data || {};
        if (type === 'apiResponse' && id === requestId) {
          console.log('[AutomatonAPI-IFRAME] Received response:', requestId);
          clearTimeout(timeoutId);
          window.removeEventListener('message', responseHandler);
          if (result.error) {
            reject(result);
          } else {
            resolve(result);
          }
        }
      }

      window.addEventListener('message', responseHandler);
      
      // Use '*' as targetOrigin for srcdoc iframes (origin is null)
      window.parent.postMessage({ type: 'apiRequest', payload: payload }, '*');
      console.log('[AutomatonAPI-IFRAME] Message posted to parent');
    });
  }

  function executeFlow(flowIdentifier, options = {}) {
    const appSlug = getAppSlug();
    if (!appSlug) {
      return Promise.reject(new Error('Could not determine app slug'));
    }
    const path = `/api/web-apps/${appSlug}/flows/${encodeURIComponent(flowIdentifier)}/execute`;
    const body = { inputs: options.inputs || {}, runnerNames: options.runnerNames || null };
    return apiCall(path, 'POST', body).then(response => response.data || response);
  }

  function customCall(path, method = 'GET', body = null) {
    if (!path.startsWith('/api/web-apps/')) {
      return Promise.reject(new Error('API path must start with /api/web-apps/'));
    }
    return apiCall(path, method, body).then(response => response.data || response);
  }

  window.automatonApi = { 
    executeFlow: executeFlow, 
    customCall: customCall, 
    getAppSlug: getAppSlug 
  };
  
  console.log('[AutomatonAPI-IFRAME] Ready! App slug:', APP_SLUG);
})(window);
</script>
";

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Logger.LogInformation("[WebAppViewer] Loading app with slug: {Slug}", Slug);

            currentApp = await AppsService.LoadBySlug(Slug);

            if (currentApp == null)
            {
                Logger.LogWarning("[WebAppViewer] App not found with slug: {Slug}", Slug);
                errorTitle = "App Not Found";
                error = "This app does not exist or has not been published.";
                return;
            }

            // Validate app is published (server should already filter this, but double-check)
            if (!currentApp.IsPublished)
            {
                Logger.LogWarning("[WebAppViewer] App '{AppName}' is not published", currentApp.Name);
                errorTitle = "App Not Published";
                error = "This app is not currently published and cannot be accessed.";
                return;
            }

            // Check authentication requirements
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var isUserAuthenticated = authState.User.Identity?.IsAuthenticated ?? false;

            Logger.LogInformation("[WebAppViewer] App: {AppName}, RequiresAuth: {RequiresAuth}, UserAuth: {UserAuth}",
                currentApp.Name, currentApp.RequiresAuthentication, isUserAuthenticated);

            if (currentApp.RequiresAuthentication && !isUserAuthenticated)
            {
                Logger.LogWarning("[WebAppViewer] Unauthenticated access to protected app '{AppName}'", currentApp.Name);
                requiresLogin = true;
                errorTitle = "Authentication Required";
                error = $"The app '{currentApp.DisplayName ?? currentApp.Name}' requires you to be logged in.";
                return;
            }

            if (string.IsNullOrEmpty(currentApp.Definition?.HtmlContent))
            {
                Logger.LogError("[WebAppViewer] App has no HTML content: {Slug}", Slug);
                errorTitle = "Error Loading App";
                error = "This app has no content configured.";
                return;
            }

            Logger.LogInformation("[WebAppViewer] Successfully loaded app '{AppName}' (HTML length: {Length})",
                currentApp.Name, currentApp.Definition.HtmlContent.Length);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogWarning(ex, "[WebAppViewer] App not found with slug: {Slug}", Slug);
            errorTitle = "App Not Found";
            error = "This app does not exist or has not been published.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[WebAppViewer] Error loading app with slug: {Slug}", Slug);
            errorTitle = "Error Loading App";
            error = "An unexpected error occurred while loading the app.";
        }
        finally
        {
            loading = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Only initialize handler if:
        // 1. First render
        // 2. App loaded successfully (currentApp != null)
        // 3. No errors
        // 4. Handler not already initialized
        if (firstRender && currentApp != null && string.IsNullOrEmpty(error) && !handlerInitialized)
        {
            try
            {
                Logger.LogInformation("[WebAppViewer] App loaded: {AppName}, initializing message handler", currentApp.Name);

                // Initialize the C# message handler
                await MessageHandler.InitializeAsync();

                handlerInitialized = true;
                Logger.LogInformation("[WebAppViewer] Message handler initialized successfully for {AppName}", currentApp.Name);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[WebAppViewer] Failed to initialize message handler for {AppName}", currentApp?.Name ?? Slug);
                // Don't fail the whole page if handler initialization fails
                // The app will still load, but API calls from iframe won't work
            }
        }
        else if (firstRender)
        {
            Logger.LogWarning("[WebAppViewer] Skipping handler init - currentApp={CurrentAppNull}, error={HasError}, initialized={Initialized}",
                currentApp == null, !string.IsNullOrEmpty(error), handlerInitialized);
        }
    }

    private string GetAppHtml()
    {
        if (currentApp?.Definition?.HtmlContent != null)
        {
            // Create the automatonApi script with the actual app slug
            var automatonApiScript = AutomatonApiScriptTemplate.Replace("{{APP_SLUG}}", Slug);
            
            // Inject automatonApi script into the HTML
            var html = currentApp.Definition.HtmlContent;
            
            // Try to inject before </body>, </html>, or at the end
            if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
            {
                html = html.Replace("</body>", $"{automatonApiScript}</body>", StringComparison.OrdinalIgnoreCase);
            }
            else if (html.Contains("</html>", StringComparison.OrdinalIgnoreCase))
            {
                html = html.Replace("</html>", $"{automatonApiScript}</html>", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                html += automatonApiScript;
            }

            return html;
        }

        return "<html><body><h1>Error: No content available</h1></body></html>";
    }

    private void GoHome()
    {
        Navigation.NavigateTo("/");
    }

    private void GoToLogin()
    {
        // Redirect to login with return URL
        var returnUrl = Uri.EscapeDataString($"/web/{Slug}");
        Navigation.NavigateTo($"/account/login?returnUrl={returnUrl}");
    }

    public async ValueTask DisposeAsync()
    {
        if (MessageHandler != null)
        {
            await MessageHandler.DisposeAsync();
        }
    }

    // DEBUG: Manual initialization for testing
    private async Task ManualInit()
    {
        try
        {
            Logger.LogInformation("[WebAppViewer] MANUAL INIT - currentApp null? {IsNull}", currentApp == null);
            
            if (currentApp == null)
            {
                Logger.LogWarning("[WebAppViewer] Cannot init - currentApp is null!");
                return;
            }

            Logger.LogInformation("[WebAppViewer] Manual init for app: {AppName}", currentApp.Name);
            await MessageHandler.InitializeAsync();
            handlerInitialized = true;
            Logger.LogInformation("[WebAppViewer] Manual init completed!");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[WebAppViewer] Manual init failed");
        }
    }
}
