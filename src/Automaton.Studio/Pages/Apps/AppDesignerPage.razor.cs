using AntDesign;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Automaton.Studio.Pages.Apps.Components.PublishAppDialog;
using BlazorMonaco.Editor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Automaton.Studio.Pages.Apps;

partial class AppDesignerPage : ComponentBase, IAsyncDisposable
{
    private StandaloneCodeEditor codeEditor;
    private bool loading = false;
    private bool saving = false;
    private bool publishing = false;
    private AppModel currentApp = new AppModel
    {
        Name = "Loading...",
        DisplayName = "",
        Description = "",
        Definition = new AppDefinition()
    };

    private IEnumerable<RunnerModel> availableRunners = new List<RunnerModel>();
    
    // Preview support
    private bool previewHandlerInitialized = false;
    private string? automatonApiScript;

    [Inject] private AppsService AppsService { get; set; }
    [Inject] private MessageService MessageService { get; set; }
    [Inject] private NavigationManager NavigationManager { get; set; }
    [Inject] private IJSRuntime JSRuntime { get; set; }
    [Inject] private ModalService ModalService { get; set; }
    [Inject] private RunnerService RunnerService { get; set; }
    [Inject] private IWebHostEnvironment WebHostEnvironment { get; set; }
    [Inject] private ILogger<AppDesignerPage> Logger { get; set; }
    [Inject] private IframeMessageHandler PreviewMessageHandler { get; set; }

    [Parameter] public string AppId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        loading = true;

        try
        {
            // Load automatonApi.js for preview
            await LoadAutomatonApiScript();
            
            if (Guid.TryParse(AppId, out var appGuid))
            {
                currentApp = await AppsService.Load(appGuid);
            }
        }
        catch
        {
            await MessageService.ErrorAsync("Failed to load app");
            NavigationManager.NavigateTo("/apps");
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
            Logger.LogInformation("[AppDesigner] Loaded automatonApi.js ({Length} bytes)", automatonApiScript.Length);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[AppDesigner] Failed to load automatonApi.js");
            automatonApiScript = null;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await LoadRunners();
            
            // Initialize preview handler once (only if app is loaded and published)
            if (currentApp?.IsPublished == true && !previewHandlerInitialized)
            {
                await InitializePreviewHandler();
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    private async Task InitializePreviewHandler()
    {
        if (previewHandlerInitialized) return;
        
        try
        {
            await PreviewMessageHandler.InitializeAsync();
            previewHandlerInitialized = true;
            Logger.LogInformation("[AppDesigner] Preview handler initialized");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[AppDesigner] Failed to initialize preview handler");
        }
    }

    private async Task LoadRunners()
    {
        try
        {
            availableRunners = await RunnerService.List();
            await InvokeAsync(StateHasChanged);
        }
        catch
        {
            availableRunners = [];
        }
    }

    private StandaloneEditorConstructionOptions EditorConstructionOptions(StandaloneCodeEditor editor)
    {
        return new StandaloneEditorConstructionOptions
        {
            AutomaticLayout = true,
            Language = "html",
            Value = currentApp.Definition?.HtmlContent ?? GetDefaultHtmlContent()
        };
    }

    private async Task Save()
    {
        saving = true;

        try
        {
            var code = await codeEditor.GetValue();
            currentApp.Definition.HtmlContent = code;

            // Persist the current app including ExecutionRunnerId (if changed)
            await AppsService.Update(currentApp);
            await MessageService.SuccessAsync("App saved successfully");
        }
        catch
        {
            await MessageService.ErrorAsync("Failed to save app");
        }
        finally
        {
            saving = false;
        }
    }

    private string GetRunnerStatusIcon(Automaton.Core.Enums.RunnerStatus status)
    {
        return status switch
        {
            Automaton.Core.Enums.RunnerStatus.Online => "check-circle",
            Automaton.Core.Enums.RunnerStatus.Offline => "close-circle",
            _ => "question-circle",
        };
    }

    private string GetRunnerStatusClass(Automaton.Core.Enums.RunnerStatus status)
    {
        return $"runner-status-icon runner-status-{status.ToString().ToLower()}";
    }

    // Publishing Methods - Phase 2 Implementation

    /// <summary>
    /// Toggle between publish and unpublish
    /// </summary>
    private async Task TogglePublish()
    {
        if (currentApp.IsPublished)
        {
            await UnpublishApp();
        }
        else
        {
            await PublishApp();
        }
    }

    /// <summary>
    /// Publish the app with a public URL
    /// </summary>
    private async Task PublishApp()
    {
        publishing = true;

        try
        {
            var model = new PublishAppModel
            {
                AppId = currentApp.Id,
                DisplayName = currentApp.DisplayName,
                PublicUrl = currentApp.PublicUrl ?? AppsService.GenerateSlugPreview(currentApp.DisplayName),
                ExecutionRunnerId = currentApp.ExecutionRunnerId
            };

            var modalRef = await ModalService.CreateModalAsync<PublishAppDialog, PublishAppModel>
            (
                new ModalOptions
                {
                    Title = "Publish App",
                    Width = 600,
                    OkText = "Publish",
                    CancelText = "Cancel"
                },
                model
            );

            modalRef.OnOk = async () =>
            {
                if (string.IsNullOrWhiteSpace(model.PublicUrl))
                {
                    await MessageService.WarningAsync("Please enter a URL slug");
                    return;
                }

                try
                {
                    await AppsService.PublishAsync(currentApp.Id, model.PublicUrl, model.RequiresAuthentication, model.ExecutionRunnerId);

                    // Update local model
                    currentApp.IsPublished = true;
                    currentApp.PublicUrl = model.PublicUrl;
                    currentApp.PublishedAt = DateTime.UtcNow;
                    currentApp.Version++;
                    currentApp.RequiresAuthentication = model.RequiresAuthentication;
                    currentApp.ExecutionRunnerId = model.ExecutionRunnerId;

                    await MessageService.SuccessAsync($"App published! Access it at /web/{model.PublicUrl}");
                    await InvokeAsync(StateHasChanged);
                }
                catch (Exception ex)
                {
                    await MessageService.ErrorAsync($"Failed to publish app: {ex.Message}");
                }
            };
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync($"Error showing publish dialog: {ex.Message}");
        }
        finally
        {
            publishing = false;
        }
    }

    /// <summary>
    /// Unpublish the app
    /// </summary>
    private async Task UnpublishApp()
    {
        var confirmed = await ModalService.ConfirmAsync(new ConfirmOptions
        {
            Title = "Unpublish App?",
            Content = "The app will no longer be accessible via public URL. You can republish it later with the same URL.",
            OkText = "Unpublish",
            CancelText = "Cancel"
        });

        if (!confirmed) return;

        publishing = true;

        try
        {
            await AppsService.UnpublishAsync(currentApp.Id);

            // Update local model
            currentApp.IsPublished = false;
            currentApp.PublishedAt = null;

            await MessageService.SuccessAsync("App unpublished successfully");
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync($"Failed to unpublish app: {ex.Message}");
        }
        finally
        {
            publishing = false;
        }
    }

    /// <summary>
    /// Get HTML content for preview
    /// </summary>
    private string GetPreviewHtml()
    {
        try
        {
            // Get current code (or fallback to saved)
            var html = currentApp.Definition?.HtmlContent ?? GetDefaultHtmlContent();

            // If not published, show message
            if (!currentApp.IsPublished)
            {
                return @"
<html>
<body style='padding: 40px; font-family: Arial; text-align: center; background: #fafafa;'>
    <div style='max-width: 500px; margin: 0 auto; background: white; padding: 40px; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1);'>
        <h2 style='color: #faad14;'>⚠️ Publish Required</h2>
        <p style='color: #666; line-height: 1.6;'>
            Save and publish your app to enable live preview with flow execution.
        </p>
    </div>
</body>
</html>";
            }

            // Inject automatonApi.js if available
            if (!string.IsNullOrEmpty(automatonApiScript) && !string.IsNullOrEmpty(currentApp.PublicUrl))
            {
                var scriptWithSlug = automatonApiScript.Replace("'{{APP_SLUG}}'", $"'{currentApp.PublicUrl}'");
                var wrappedScript = $"<script>{scriptWithSlug}</script>";

                if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
                {
                    html = html.Replace("</body>", $"{wrappedScript}</body>", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    html += wrappedScript;
                }
            }

            return html;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[AppDesigner] Error generating preview HTML");
            return GetDefaultHtmlContent();
        }
    }

    /// <summary>
    /// Refresh the preview iframe
    /// </summary>
    private async Task RefreshPreview()
    {
        try
        {
            // Save current code
            var code = await codeEditor.GetValue();
            currentApp.Definition.HtmlContent = code;
            
            if (!currentApp.IsPublished)
            {
                await MessageService.WarningAsync("Publish your app first to enable live preview");
                return;
            }
            
            // Save silently
            await AppsService.Update(currentApp);

            // Reinitialize handler if not ready
            if (!previewHandlerInitialized)
            {
                await InitializePreviewHandler();
            }

            // Force re-render
            await InvokeAsync(StateHasChanged);

            await MessageService.SuccessAsync("Preview refreshed", 1);
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync($"Failed to refresh preview: {ex.Message}");
        }
    }

    /// <summary>
    /// Navigate to published app (if published)
    /// </summary>
    private async Task OpenPublishedApp()
    {
        if (!string.IsNullOrEmpty(currentApp.PublicUrl))
        {
            // Open in new tab to avoid disposing the Blazor circuit
            await JSRuntime.InvokeVoidAsync("open", $"/web/{currentApp.PublicUrl}", "_blank");
        }
        else
        {
            await MessageService.WarningAsync("This app has not been published yet.");
        }
    }

    private async Task OnCodeGenerated(string generatedCode)
    {
        try
        {
            // Set the generated code in the Monaco editor
            await codeEditor.SetValue(generatedCode);

            // Update the view model
            currentApp.Definition.HtmlContent = generatedCode;

            // Show success message
            await MessageService.SuccessAsync("Code generated successfully! You can now review and modify it.");
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync($"Failed to update code editor: {ex.Message}");
        }
    }

    private async Task<string> GetCurrentCodeForAI()
    {
        try
        {
            // Get current code from Monaco editor
            if (codeEditor != null)
            {
                return await codeEditor.GetValue() ?? string.Empty;
            }

            // Fallback to view model if editor not available
            return currentApp.Definition?.HtmlContent ?? string.Empty;
        }
        catch
        {
            // Fallback to view model code if Monaco editor fails
            return currentApp.Definition?.HtmlContent ?? string.Empty;
        }
    }

    private string GetDefaultHtmlContent()
    {
        return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>My App</title>
    <style>
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
         margin: 0;
            padding: 20px;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
         display: flex;
     justify-content: center;
   align-items: center;
        }
   .container {
            background: white;
            padding: 40px;
      border-radius: 10px;
    box-shadow: 0 10px 40px rgba(0,0,0,0.2);
     max-width: 600px;
        width: 100%;
        }
        h1 {
            color: #333;
     margin-top: 0;
  }
    p {
            color: #666;
    line-height: 1.6;
        }
        .button {
      background: #667eea;
            color: white;
       border: none;
            padding: 12px 24px;
  border-radius: 5px;
            cursor: pointer;
    font-size: 16px;
         margin-top: 20px;
        }
        .button:hover {
  background: #5568d3;
    }
  </style>
</head>
<body>
    <div class=""container"">
 <h1>Welcome to Your App! 🚀</h1>
        <p>This is your app's starting template. You can edit this HTML, CSS, and JavaScript to build your application.</p>
        <p>Connect to your Flows to add backend functionality!</p>
        <button class=""button"" onclick=""alert('Hello from your app!')"">Click Me</button>
    </div>
    
    <script>
      // Your JavaScript code here
     console.log('App loaded successfully!');
        
 // Example: Call a Flow (coming soon)
     // Automaton.executeFlow('flow-id', { inputVar: 'value' })
        //   .then(result => console.log(result));
    </script>
</body>
</html>";
    }

    public async ValueTask DisposeAsync()
    {
        // Dispose preview handler
        if (PreviewMessageHandler != null)
        {
            await PreviewMessageHandler.DisposeAsync();
        }
    }
}
