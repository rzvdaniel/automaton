using AntDesign;
using Automaton.Studio.Helpers;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Automaton.Studio.Pages.Apps.Components.PublishAppDialog;
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
using Automaton.Studio.Components.AiChat.Contexts;

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
    private List<StepWithLinkStatus> allSteps = new();
    private bool loadingSteps = false;
    private string stepSearchText = "";
    
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
    [Inject] private IUnifiedAppSystemPromptBuilder UnifiedAppSystemPromptBuilder { get; set; }
    [Inject] private IAppEditorBridge CurrentWebAppContextService { get; set; }
    [Inject] private IAppMcpClient WebAppMcpClient { get; set; }
    [Inject] private IUnifiedAppMcpClient UnifiedAppMcpClient { get; set; }

    [Parameter] public string AppId { get; set; }

    // Computed properties for steps
    private List<StepWithLinkStatus> FilteredSteps => string.IsNullOrWhiteSpace(stepSearchText)
        ? allSteps
        : allSteps.Where(s =>
            (s.Step.Name?.Contains(stepSearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (s.Step.DisplayName?.Contains(stepSearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (s.Step.Description?.Contains(stepSearchText, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

    private int LinkedStepsCount => allSteps.Count(s => s.IsLinked);
    
    private bool AllFilteredStepsLinked => FilteredSteps.Any() && FilteredSteps.All(s => s.IsLinked);

    protected override async Task OnInitializedAsync()
    {
        loading = true;

        CurrentWebAppContextService.CodeChanged += OnCodeChangedByMcpTools;
        UnifiedAppMcpClient.BackendStepsChanged += OnBackendStepsChanged;

        try
        {
            await LoadAutomatonApiScript();
            await InitializeUploadHeaders();
            
            if (Guid.TryParse(AppId, out var appGuid))
            {
                currentApp = await AppsService.Load(appGuid);
                
                CurrentWebAppContextService.SetCurrentApp(
                    currentApp,
                    GetCurrentCodeForAI);
                    
                // Set up unified MCP client with app context
                UnifiedAppMcpClient.SetCurrentApp(currentApp.Id);
                    
                await LoadAllSteps();
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
            }
            else
            {
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

    private async Task TogglePublish()
    {
        if (currentApp.IsPublished)
            await UnpublishApp();
        else
            await PublishApp();
    }

    private async Task PublishApp()
    {
        publishing = true;
        try
        {
            var displayName = !string.IsNullOrEmpty(currentApp.DisplayName) ? currentApp.DisplayName : currentApp.Name;
            
            var model = new PublishAppModel
            {
                AppId = currentApp.Id,
                DisplayName = displayName,
                PublicUrl = currentApp.PublicUrl ?? AppsService.GenerateSlugPreview(displayName),
                ExecutionRunnerId = currentApp.ExecutionRunnerId
            };

            var modalRef = await ModalService.CreateModalAsync<PublishAppDialog, PublishAppModel>(
                new ModalOptions { Title = "Publish App", Width = 600, OkText = "Publish", CancelText = "Cancel" },
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

    private async Task UnpublishApp()
    {
        var confirmed = await ModalService.ConfirmAsync(new ConfirmOptions
        {
            Title = "Unpublish App?",
            Content = "The app will no longer be accessible via public URL.",
            OkText = "Unpublish",
            CancelText = "Cancel"
        });
        if (!confirmed) return;

        publishing = true;
        try
        {
            await AppsService.UnpublishAsync(currentApp.Id);
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

    private string GetPreviewHtml()
    {
        try
        {
            var html = currentApp.Definition?.HtmlContent ?? GetDefaultHtmlContent();
            if (currentApp.IsPublished && !string.IsNullOrEmpty(automatonApiScript) && !string.IsNullOrEmpty(currentApp.PublicUrl))
            {
                var scriptWithSlug = automatonApiScript.Replace("'{{APP_SLUG}}'", $"'{currentApp.PublicUrl}'");
                var wrappedScript = $"<script>{scriptWithSlug}</script>";
                if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
                    html = html.Replace("</body>", $"{wrappedScript}</body>", StringComparison.OrdinalIgnoreCase);
                else
                    html += wrappedScript;
            }
            return html;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error generating preview HTML");
            return GetDefaultHtmlContent();
        }
    }

    private async Task RefreshPreview()
    {
        try
        {
            var code = await codeEditor.GetValue();
            currentApp.Definition.HtmlContent = code;
            await AppsService.Update(currentApp);
            if (!previewHandlerInitialized) await InitializePreviewHandler();
            await InvokeAsync(StateHasChanged);
            _ = MessageService.SuccessAsync("Preview refreshed", 1);
        }
        catch (Exception ex)
        {
            _ = MessageService.ErrorAsync($"Failed to refresh preview: {ex.Message}");
        }
    }

    private async Task OpenPublishedApp()
    {
        if (!string.IsNullOrEmpty(currentApp.PublicUrl))
            await JSRuntime.InvokeVoidAsync("open", $"/web/{currentApp.PublicUrl}", "_blank");
        else
            _ = MessageService.WarningAsync("This app has not been published yet.");
    }

    private async Task OnCodeGenerated(string generatedCode)
    {
        try
        {
            await codeEditor.SetValue(generatedCode);
            currentApp.Definition.HtmlContent = generatedCode;
            _ = MessageService.SuccessAsync("Code generated successfully!");
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
            if (codeEditor != null) return await codeEditor.GetValue() ?? string.Empty;
            return currentApp.Definition?.HtmlContent ?? string.Empty;
        }
        catch
        {
            return currentApp.Definition?.HtmlContent ?? string.Empty;
        }
    }

    private UnifiedAppAiChatContext CreateAiChatContext()
    {
        // Use unified context that supports both frontend and backend generation
        return new UnifiedAppAiChatContext(
            UnifiedAppSystemPromptBuilder, 
            AppId, 
            GetCurrentCodeForAI,
            GetLinkedStepsForAI
        );
    }

    private async Task<List<LinkedStepInfo>> GetLinkedStepsForAI()
    {
        try
        {
            return await AppStepsService.GetLinkedStepsAsync(currentApp.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load linked steps for AI context");
            return new List<LinkedStepInfo>();
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
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 0; padding: 20px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); min-height: 100vh; display: flex; justify-content: center; align-items: center; }
        .container { background: white; padding: 40px; border-radius: 10px; box-shadow: 0 10px 40px rgba(0,0,0,0.2); max-width: 600px; width: 100%; }
        h1 { color: #333; margin-top: 0; }
        p { color: #666; line-height: 1.6; }
        .button { background: #667eea; color: white; border: none; padding: 12px 24px; border-radius: 5px; cursor: pointer; font-size: 16px; margin-top: 20px; }
        .button:hover { background: #5568d3; }
    </style>
</head>
<body>
    <div class=""container"">
        <h1>Welcome to Your App!</h1>
        <p>This is your app's starting template.</p>
        <button class=""button"" onclick=""alert('Hello!')"">Click Me</button>
    </div>
    <script>console.log('App loaded!');</script>
</body>
</html>";
    }

    private async Task OnBackgroundImageUpload(UploadInfo fileInfo)
    {
        try
        {
            if (fileInfo.File.State == UploadState.Success)
            {
                var response = fileInfo.File.GetResponse<UploadImageResponse>();
                if (response != null && !string.IsNullOrEmpty(response.ImageUrl))
                {
                    currentApp.Appearance.BackgroundImageUrl = $"{ConfigurationService.BaseUrl}{response.ImageUrl}";
                    _ = MessageService.SuccessAsync("Image uploaded successfully");
                    await InvokeAsync(StateHasChanged);
                }
            }
            else if (fileInfo.File.State == UploadState.Fail)
            {
                _ = MessageService.ErrorAsync("Image upload failed.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error uploading image");
        }
    }

    private async Task RemoveBackgroundImage()
    {
        var confirmed = await ModalService.ConfirmAsync(new ConfirmOptions { Title = "Remove Image?", Content = "The background image will be removed.", OkText = "Remove", CancelText = "Cancel" });
        if (confirmed)
        {
            currentApp.Appearance.BackgroundImageUrl = null;
            _ = MessageService.SuccessAsync("Image removed");
            await InvokeAsync(StateHasChanged);
        }
    }

    private string GetPreviewImageStyle() => $"position: relative; margin-bottom: 16px; border-radius: 8px; overflow: hidden; opacity: {currentApp.Appearance.BackgroundImageOpacity};";
    private string GetUploadAction() => $"{ConfigurationService.BaseUrl}/api/apps/{currentApp.Id}/upload-background";

    public class UploadImageResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("imageUrl")]
        public string ImageUrl { get; set; }
    }

    public async ValueTask DisposeAsync()
    {
        CurrentWebAppContextService.CodeChanged -= OnCodeChangedByMcpTools;
        UnifiedAppMcpClient.BackendStepsChanged -= OnBackendStepsChanged;
        if (PreviewMessageHandler != null) await PreviewMessageHandler.DisposeAsync();
    }

    private async void OnCodeChangedByMcpTools(object? sender, AppCodeChangedEventArgs e)
    {
        try
        {
            if (codeEditor != null && !string.IsNullOrEmpty(e.NewCode))
            {
                await InvokeAsync(async () =>
                {
                    await codeEditor.SetValue(e.NewCode);
                    currentApp.Definition.HtmlContent = e.NewCode;
                    StateHasChanged();
                    
                    // Auto-save the app after AI generates/modifies code
                    try
                    {
                        await AppsService.Update(currentApp);
                        Logger.LogInformation("App auto-saved after AI code generation");
                    }
                    catch (Exception saveEx)
                    {
                        Logger.LogWarning(saveEx, "Failed to auto-save app after AI code generation");
                        // Don't show error to user - the code is still in the editor
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating editor from MCP tools");
        }
    }

    private async void OnBackendStepsChanged(object? sender, Automaton.Studio.Services.Mcp.ToolProviders.BackendStepsChangedEventArgs e)
    {
        try
        {
            // Only refresh if the event is for the current app
            if (e.AppId == currentApp.Id)
            {
                Logger.LogInformation("Backend steps changed ({ChangeType}: {StepName}), refreshing list", e.ChangeType, e.StepName);
                
                await InvokeAsync(async () =>
                {
                    await LoadAllSteps();
                    StateHasChanged();
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error refreshing steps after backend change");
        }
    }
    
    // Backend Actions Tab Methods
    private async Task LoadAllSteps()
    {
        loadingSteps = true;
        try
        {
            var linkedSteps = await AppStepsService.GetLinkedStepsAsync(currentApp.Id);
            var availableSteps = await AppStepsService.GetAvailableStepsAsync(currentApp.Id);
            
            allSteps = linkedSteps
                .Select(s => new StepWithLinkStatus { Step = s, IsLinked = true })
                .Concat(availableSteps.Select(s => new StepWithLinkStatus { Step = s, IsLinked = false }))
                .OrderBy(s => s.Step.DisplayName ?? s.Step.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load steps");
            _ = MessageService.ErrorAsync("Failed to load steps");
        }
        finally
        {
            loadingSteps = false;
        }
    }

    private async Task ToggleStepLink(StepWithLinkStatus stepWithStatus)
    {
        try
        {
            if (stepWithStatus.IsLinked)
            {
                await AppStepsService.UnlinkStepAsync(currentApp.Id, stepWithStatus.Step.Id);
                stepWithStatus.IsLinked = false;
            }
            else
            {
                await AppStepsService.LinkStepAsync(currentApp.Id, stepWithStatus.Step.Id);
                stepWithStatus.IsLinked = true;
            }
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to toggle step link");
            _ = MessageService.ErrorAsync($"Failed to update step link: {ex.Message}");
        }
    }

    private async Task ToggleAllFilteredSteps()
    {
        try
        {
            var filtered = FilteredSteps;
            if (!filtered.Any()) return;

            var shouldLink = !AllFilteredStepsLinked;

            foreach (var stepWithStatus in filtered)
            {
                if (stepWithStatus.IsLinked != shouldLink)
                {
                    if (shouldLink)
                        await AppStepsService.LinkStepAsync(currentApp.Id, stepWithStatus.Step.Id);
                    else
                        await AppStepsService.UnlinkStepAsync(currentApp.Id, stepWithStatus.Step.Id);
                    stepWithStatus.IsLinked = shouldLink;
                }
            }

            var action = shouldLink ? "linked" : "unlinked";
            _ = MessageService.SuccessAsync($"{filtered.Count} step(s) {action}");
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to toggle all steps");
            _ = MessageService.ErrorAsync($"Failed to update steps: {ex.Message}");
        }
    }

    private void OnStepSearchInput(ChangeEventArgs e)
    {
        stepSearchText = e.Value?.ToString() ?? "";
        StateHasChanged();
    }
        
    private void NavigateToStep(Guid stepId) => NavigationManager.NavigateTo($"/step/{stepId}");
    
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
        return $@"const result = await automatonApi.executeStep('{step.Name}', {{ inputs: {{ {inputsExample} }} }});
console.log(result.outputVariables);";
    }
    
    private string GetStepIcon(string language) => StepHelpers.GetStepIcon(language);
    private string GetLanguageColor(string language) => StepHelpers.GetLanguageColor(language);

    public class StepWithLinkStatus
    {
        public LinkedStepInfo Step { get; set; } = new();
        public bool IsLinked { get; set; }
    }
}
