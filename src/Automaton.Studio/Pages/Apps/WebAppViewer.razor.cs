using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Automaton.Studio.Pages.Apps;

partial class WebAppViewer
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
    private ElementReference iframeElement;

    // Client-side automatonApi.js script to inject into iframe
    private const string AutomatonApiScriptTemplate = @"
<script>
(function(window) {
  'use strict';

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
      
      // Retry ping/pong handshake
      let retries = 0;
      const maxRetries = 50;
      
      function sendWhenReady() {
        window.parent.postMessage({ type: 'ping' }, '*');
        
        const pingTimeout = setTimeout(() => {
          retries++;
          if (retries < maxRetries) {
            sendWhenReady();
          } else {
            reject(new Error('Parent handler not ready after 5 seconds'));
          }
        }, 100);
        
        function pongHandler(e) {
          if (e.data?.type === 'pong') {
            clearTimeout(pingTimeout);
            window.removeEventListener('message', pongHandler);
            window.parent.postMessage({ type: 'apiRequest', payload: payload }, '*');
            console.log('[AutomatonAPI-IFRAME] Message posted to parent');
          }
        }
        window.addEventListener('message', pongHandler);
      }
      
      sendWhenReady();
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

            if (!currentApp.IsPublished)
            {
                Logger.LogWarning("[WebAppViewer] App '{AppName}' is not published", currentApp.Name);
                errorTitle = "App Not Published";
                error = "This app is not currently published and cannot be accessed.";
                return;
            }

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
        // Don't rely on firstRender - use our own flag
        // This handles prerendering where firstRender=true but data isn't loaded yet
        if (!handlerInitialized && currentApp != null && string.IsNullOrEmpty(error))
        {
            Logger.LogWarning("[WebAppViewer] ===== ATTEMPTING INITIALIZATION ===== (firstRender: {FirstRender})", firstRender);
            
            try
            {
                await InitializeHandlerAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[WebAppViewer] Failed in OnAfterRenderAsync");
            }
        }
    }

    private async Task InitializeHandlerAsync()
    {
        // Double-check to prevent multiple initializations (in case OnAfterRenderAsync is called multiple times)
        if (handlerInitialized)
        {
            Logger.LogWarning("[WebAppViewer] Handler already initialized, skipping");
            return;
        }

        // Set flag FIRST to prevent race conditions
        handlerInitialized = true;

        Logger.LogWarning("[WebAppViewer] ===== INITIALIZING HANDLER =====");

        try
        {
            await MessageHandler.InitializeAsync();
            Logger.LogInformation("[WebAppViewer] Message handler initialized successfully");
        }
        catch (Exception ex)
        {
            // Reset flag on failure so it can retry
            handlerInitialized = false;
            Logger.LogError(ex, "[WebAppViewer] Failed to initialize handler");
            throw;
        }
    }

    private string GetAppHtml()
    {
        if (currentApp?.Definition?.HtmlContent != null)
        {
            var automatonApiScript = AutomatonApiScriptTemplate.Replace("{{APP_SLUG}}", Slug);
            var html = currentApp.Definition.HtmlContent;

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
}
