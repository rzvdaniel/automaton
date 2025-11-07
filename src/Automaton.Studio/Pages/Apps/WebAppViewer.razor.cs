using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Hosting;
using System.IO;

namespace Automaton.Studio.Pages.Apps;

partial class WebAppViewer
{
    [Inject] AppsService AppsService { get; set; } = default!;
    [Inject] NavigationManager Navigation { get; set; } = default!;
    [Inject] AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] ILogger<WebAppViewer> Logger { get; set; } = default!;
    [Inject] IframeMessageHandler MessageHandler { get; set; } = default!;
    [Inject] IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] IWebHostEnvironment WebHostEnvironment { get; set; } = default!;
    [Parameter] public string Slug { get; set; } = string.Empty;

    private bool loading = true;
    private string? error;
    private string errorTitle = "Error";
    private bool requiresLogin = false;
    private AppModel? currentApp;
    private bool handlerInitialized = false;
    private ElementReference iframeElement;
    private string? automatonApiScript;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Logger.LogInformation("[WebAppViewer] Loading app with slug: {Slug}", Slug);

            // Load the automatonApi.js script once
            await LoadAutomatonApiScript();

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

    private async Task LoadAutomatonApiScript()
    {
        try
        {
            var scriptPath = Path.Combine(WebHostEnvironment.WebRootPath, "automatonApi.js");
            automatonApiScript = await File.ReadAllTextAsync(scriptPath);
            Logger.LogInformation("[WebAppViewer] Loaded automatonApi.js script ({Length} bytes)", automatonApiScript.Length);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[WebAppViewer] Failed to load automatonApi.js");
            automatonApiScript = "console.error('[AutomatonAPI] Failed to load script');";
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Early return if already initialized - prevents multiple calls
        if (handlerInitialized)
        {
            return;
        }

        // Only initialize if we have data and no errors
        if (currentApp != null && string.IsNullOrEmpty(error))
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
        // Double-check lock pattern to prevent race conditions
        if (handlerInitialized)
        {
            Logger.LogWarning("[WebAppViewer] Handler already initialized, skipping");
            return;
        }

        // Set flag FIRST to prevent race conditions if OnAfterRenderAsync is called again
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
        if (currentApp?.Definition?.HtmlContent != null && automatonApiScript != null)
        {
            // Inject the app slug into the script
            var scriptWithSlug = automatonApiScript.Replace("'{{APP_SLUG}}'", $"'{Slug}'");
            var wrappedScript = $"<script>{scriptWithSlug}</script>";
            
            var html = currentApp.Definition.HtmlContent;

            // Try to inject before </body>, </html>, or at the end
            if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
            {
                html = html.Replace("</body>", $"{wrappedScript}</body>", StringComparison.OrdinalIgnoreCase);
            }
            else if (html.Contains("</html>", StringComparison.OrdinalIgnoreCase))
            {
                html = html.Replace("</html>", $"{wrappedScript}</html>", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                html += wrappedScript;
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
