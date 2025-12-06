using AntDesign;
using Automaton.Studio.Helpers;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Automaton.Studio.Pages.Apps.Components.PublishAppDialog;
using Automaton.Studio.Pages.Apps.Components.LinkStepDialog;
using Automaton.Client.Auth.Interfaces;
using BlazorMonaco.Editor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using Automaton.Studio.Pages.Authentication.Config;
using Automaton.Studio.Services.Mcp.Clients;
using Automaton.Studio.Services.Mcp.EditorBridges;
using Automaton.Studio.Services.Mcp.SystemPrompts;

namespace Automaton.Studio.Pages.Apps;

partial class AppPage : ComponentBase, IAsyncDisposable
{
    private StandaloneCodeEditor codeEditor;
    private bool loading = false;
    private bool saving = false;
    private bool publishing = false;
    private Dictionary<string, string> uploadHeaders = new();
    private AppModel currentApp = new AppModel
    {
        Name = "Loading...",
        DisplayName = "",
        Description = "",
        Definition = new AppDefinition()
    };

    private IEnumerable<RunnerModel> availableRunners = new List<RunnerModel>();
    
    // Backend Actions tab support
    private List<LinkedStepInfo> linkedSteps = new();
    private bool loadingSteps = false;
    
    // Preview support
    private bool previewHandlerInitialized = false;
    private string? automatonApiScript;

    [Inject] private AppsService AppsService { get; set; }
    [Inject] private AppStepsService AppStepsService { get; set; }
    [Inject] private MessageService MessageService { get; set; }
    [Inject] private NavigationManager NavigationManager { get; set; }
    [Inject] private IJSRuntime JSRuntime { get; set; }
    [Inject] private ModalService ModalService { get; set; }
    [Inject] private RunnerService RunnerService { get; set; }
    [Inject] private IWebHostEnvironment WebHostEnvironment { get; set; }
    [Inject] private ILogger<AppPage> Logger { get; set; }
    [Inject] private IframeMessageHandler PreviewMessageHandler { get; set; }
    [Inject] private Services.ConfigurationService ConfigurationService { get; set; }
    [Inject] private IAuthenticationStorage AuthenticationStorage { get; set; }
    [Inject] private IAppSystemPromptBuilder WebAppSystemPromptBuilder { get; set; }
    [Inject] private IAppEditorBridge CurrentWebAppContextService { get; set; }
    [Inject] private IAppMcpClient WebAppMcpClient { get; set; }

    [Parameter] public string AppId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        loading = true;

        // Subscribe to code changes from MCP tools
        CurrentWebAppContextService.CodeChanged += OnCodeChangedByMcpTools;

        try
        {
            // Load automatonApi.js for preview
            await LoadAutomatonApiScript();
            
            // Initialize upload headers with JWT token
            await InitializeUploadHeaders();
            
            if (Guid.TryParse(AppId, out var appGuid))
            {
                currentApp = await AppsService.Load(appGuid);
                
                // Register current web app with the context service
                CurrentWebAppContextService.SetCurrentApp(
                    currentApp,
                    GetCurrentCodeForAI);
                    
                // Load linked steps
                await LoadLinkedSteps();
            }
        }
        catch
        {
            _ = MessageService.ErrorAsync("Failed to load app");
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
            Logger.LogInformation("Loaded automatonApi.js ({Length} bytes)", automatonApiScript.Length);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load automatonApi.js");
            automatonApiScript = null;
        }
    }

    private async Task InitializeUploadHeaders()
    {
        try
        {
            var accessToken = await AuthenticationStorage.GetAccessToken();
            if (!string.IsNullOrEmpty(accessToken))
            {
                uploadHeaders = new Dictionary<string, string>
                {
                    { "Authorization", $"Bearer {accessToken}" }
                };
                Logger.LogInformation("Upload headers initialized with JWT token");
            }
            else
            {
                Logger.LogWarning("No access token found in authentication storage");
                uploadHeaders = new Dictionary<string, string>();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize upload headers");
            uploadHeaders = new Dictionary<string, string>();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !previewHandlerInitialized)
        {
            await LoadRunners();
            // Always initialize the preview handler so the iframe can communicate
            await InitializePreviewHandler();
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
            Logger.LogInformation("Preview handler initialized");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize preview handler");
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
            _ = MessageService.SuccessAsync("App saved successfully");
        }
        catch
        {
            _ = MessageService.ErrorAsync("Failed to save app");
        }
        finally
        {
            saving = false;
        }
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
            // Use DisplayName if available, otherwise fall back to Name
            var displayName = !string.IsNullOrEmpty(currentApp.DisplayName) ? currentApp.DisplayName : currentApp.Name;
            
            var model = new PublishAppModel
            {
                AppId = currentApp.Id,
                DisplayName = displayName,
                PublicUrl = currentApp.PublicUrl ?? AppsService.GenerateSlugPreview(displayName),
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
                    _ = MessageService.WarningAsync("Please enter a URL slug");
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

                    _ = MessageService.SuccessAsync($"App published! Access it at /web/{model.PublicUrl}");
                    await InvokeAsync(StateHasChanged);
                }
                catch (Exception ex)
                {
                    _ = MessageService.ErrorAsync($"Failed to publish app: {ex.Message}");
                }
            };
        }
        catch (Exception ex)
        {
            _ = MessageService.ErrorAsync($"Error showing publish dialog: {ex.Message}");
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

            _ = MessageService.SuccessAsync("App unpublished successfully");
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            _ = MessageService.ErrorAsync($"Failed to unpublish app: {ex.Message}");
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

            // Inject automatonApi.js only if published (requires public URL for flow execution)
            if (currentApp.IsPublished && !string.IsNullOrEmpty(automatonApiScript) && !string.IsNullOrEmpty(currentApp.PublicUrl))
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
            Logger.LogError(ex, "Error generating preview HTML");
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
            
            // Save silently (no need to publish for preview)
            await AppsService.Update(currentApp);

            // Reinitialize handler if not ready
            if (!previewHandlerInitialized)
            {
                await InitializePreviewHandler();
            }

            // Force re-render
            await InvokeAsync(StateHasChanged);

            _ = MessageService.SuccessAsync("Preview refreshed", 1);
        }
        catch (Exception ex)
        {
            _ = MessageService.ErrorAsync($"Failed to refresh preview: {ex.Message}");
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
            _ = MessageService.WarningAsync("This app has not been published yet.");
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
            _ = MessageService.SuccessAsync("Code generated successfully! You can now review and modify it.");
        }
        catch (Exception ex)
        {
            _ = MessageService.ErrorAsync($"Failed to update code editor: {ex.Message}");
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

    // Create AI Chat context for WebApp code generation
    private Automaton.Studio.Components.AiChat.Contexts.AppAiChatContext CreateAiChatContext()
    {
        return new Automaton.Studio.Components.AiChat.Contexts.AppAiChatContext(
            WebAppSystemPromptBuilder,
            AppId,
            GetCurrentCodeForAI
        );
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

    private async Task OnBackgroundImageUpload(UploadInfo fileInfo)
    {
        try
        {
            if (fileInfo.File.State == UploadState.Success)
            {
                // The file is uploaded via the API endpoint
                // The response should contain the image URL (relative path)
                try
                {
                    var response = fileInfo.File.GetResponse<UploadImageResponse>();
                    if (response != null && !string.IsNullOrEmpty(response.ImageUrl))
                    {
                        // Store the relative path from server, but display it with full server URL
                        currentApp.Appearance.BackgroundImageUrl = $"{ConfigurationService.BaseUrl}{response.ImageUrl}";
                        _ = MessageService.SuccessAsync("Image uploaded successfully");
                        await InvokeAsync(StateHasChanged);
                    }
                    else
                    {
                        _ = MessageService.ErrorAsync("Invalid response from server: missing image URL");
                    }
                }
                catch (Exception parseEx)
                {
                    Logger.LogError(parseEx, "Failed to parse upload response");
                    _ = MessageService.ErrorAsync($"Failed to process upload response: {parseEx.Message}");
                }
            }
            else if (fileInfo.File.State == UploadState.Fail)
            {
                // Upload failed - show error message once
                _ = MessageService.ErrorAsync("Image upload failed. Please check the file size and format (max 2MB, PNG/JPG only)");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error uploading image");
            _ = MessageService.ErrorAsync($"Error uploading image: {ex.Message}");
        }
    }

    private async Task RemoveBackgroundImage()
    {
        var confirmed = await ModalService.ConfirmAsync(new ConfirmOptions
        {
            Title = "Remove Image?",
            Content = "The background image will be removed from this app.",
            OkText = "Remove",
            CancelText = "Cancel"
        });

        if (confirmed)
        {
            currentApp.Appearance.BackgroundImageUrl = null;
            _ = MessageService.SuccessAsync("Image removed");
            await InvokeAsync(StateHasChanged);
        }
    }

    private string GetPreviewImageStyle()
    {
        var opacity = currentApp.Appearance.BackgroundImageOpacity;
        return $"position: relative; margin-bottom: 16px; border-radius: 8px; overflow: hidden; opacity: {opacity};";
    }

    private string GetUploadAction()
    {
        return $"{ConfigurationService.BaseUrl}/api/apps/{currentApp.Id}/upload-background";
    }

    public class UploadImageResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("imageUrl")]
        public string ImageUrl { get; set; }
    }

    public async ValueTask DisposeAsync()
    {
        // Unsubscribe from code changes
        CurrentWebAppContextService.CodeChanged -= OnCodeChangedByMcpTools;
        
        // Dispose preview handler
        if (PreviewMessageHandler != null)
        {
            await PreviewMessageHandler.DisposeAsync();
        }
    }

    /// <summary>
    /// Handle code changes from MCP tools (AI-generated code updates)
    /// </summary>
    private async void OnCodeChangedByMcpTools(object? sender, AppCodeChangedEventArgs e)
    {
        try
        {
            // Update Monaco editor with the new code saved by MCP tools
            if (codeEditor != null && !string.IsNullOrEmpty(e.NewCode))
            {
                await InvokeAsync(async () =>
                {
                    await codeEditor.SetValue(e.NewCode);
                    currentApp.Definition.HtmlContent = e.NewCode;
                    StateHasChanged();
                });
                
                Logger.LogInformation("Code updated in Monaco editor from MCP tools: {Length} chars", e.NewCode.Length);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating editor from MCP tools");
            _ = MessageService.ErrorAsync($"Error updating editor: {ex.Message}");
        }
    }
    
    // Backend Actions Tab Methods
    private async Task LoadLinkedSteps()
    {
        loadingSteps = true;
        
        try
        {
            linkedSteps = await AppStepsService.GetLinkedStepsAsync(currentApp.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load linked steps");
            _ = MessageService.ErrorAsync("Failed to load linked steps");
        }
        finally
        {
            loadingSteps = false;
        }
    }
    
    private async Task ShowLinkStepDialog()
    {
        var availableSteps = await AppStepsService.GetAvailableStepsAsync(currentApp.Id);
        
        if (!availableSteps.Any())
        {
            _ = MessageService.WarningAsync("No steps available to link. Create a new step first.");
            return;
        }
        
        var model = new LinkStepDialogModel
        {
            AvailableSteps = availableSteps,
            AppId = currentApp.Id
        };
        
        var modalRef = await ModalService.CreateModalAsync<LinkStepDialog, LinkStepDialogModel>(
            new ModalOptions
            {
                Title = "Link Step to App",
                Width = 600,
                OkText = "Link Step",
                CancelText = "Cancel"
            },
            model
        );
        
        // Handle OK - link the selected step
        modalRef.OnOk = async () =>
        {
            if (!model.SelectedStepId.HasValue)
            {
                _ = MessageService.WarningAsync("Please select a step to link");
                return;
            }

            try
            {
                await AppStepsService.LinkStepAsync(model.AppId, model.SelectedStepId.Value);
                _ = MessageService.SuccessAsync("Step linked successfully");
                await LoadLinkedSteps();
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                _ = MessageService.ErrorAsync($"Failed to link step: {ex.Message}");
            }
        };
        
        // Reload steps after dialog closes (in case user navigated to create new step)
        modalRef.OnClose = async () =>
        {
            await LoadLinkedSteps();
            await InvokeAsync(StateHasChanged);
        };
    }
        
    private void NavigateToStep(Guid stepId)
    {
        NavigationManager.NavigateTo($"/step/{stepId}");
    }
    
    private async Task CopyStepCode(LinkedStepInfo step)
    {
        var code = GenerateStepCode(step);
        await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", code);
        _ = MessageService.SuccessAsync("Code copied to clipboard!");
    }
    
    private string GenerateStepCode(LinkedStepInfo step)
    {
        var inputsExample = step.HasInputVariables
            ? string.Join(", ", step.InputVariables.Take(3).Select(v => $"{v.Name}: 'value'"))
            : "";

        return $@"// Execute {step.DisplayName}
const result = await automatonApi.executeStep('{step.Name}', {{
    inputs: {{ {inputsExample} }}
}});

// Access results
console.log('Success:', result.success);
console.log('Outputs:', result.outputVariables);";
    }
    
    private async Task UnlinkStep(Guid stepId)
    {
        try
        {
            await AppStepsService.UnlinkStepAsync(currentApp.Id, stepId);
            linkedSteps.RemoveAll(s => s.Id == stepId);
            _ = MessageService.SuccessAsync("Step unlinked successfully");
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to unlink step");
            _ = MessageService.ErrorAsync("Failed to unlink step");
        }
    }
    
    private string GetStepIcon(string language) => StepHelpers.GetStepIcon(language);
    
    private string GetLanguageColor(string language) => StepHelpers.GetLanguageColor(language);
}
