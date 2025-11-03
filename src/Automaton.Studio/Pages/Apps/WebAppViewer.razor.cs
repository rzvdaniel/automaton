using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.Authorization;

namespace Automaton.Studio.Pages.Apps;

partial class WebAppViewer : ComponentBase
{
    [Inject] AppsService AppsService { get; set; } = default!;
    [Inject] NavigationManager Navigation { get; set; } = default!;
    [Inject] AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] ILogger<WebAppViewer> Logger { get; set; } = default!;
    [Parameter] public string Slug { get; set; } = string.Empty;

    private bool loading = true;
    private string? error;
    private string errorTitle = "Error";
    private bool requiresLogin = false;
    private AppModel? currentApp;

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

    private string GetAppHtml()
    {
        if (currentApp?.Definition?.HtmlContent != null)
        {
            return currentApp.Definition.HtmlContent;
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
}
