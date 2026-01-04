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
    
    // Backend Actions tab support - renamed to App Steps
    private List<LinkedStepInfo> appSteps = new();
    private bool loadingSteps = false;
    private string stepSearchText = "";
    
    // Preview support
    private bool previewHandlerInitialized = false;
    private string? automatonApiScript;

    // GitHub integration
    private bool hasGitHubToken = false;
    private bool githubSyncEnabled = false;
    private string githubRepoUrl = "";
    private string githubBranch = "main";
    private string? githubPath = null;
    private bool githubPushing = false;
    private bool githubPulling = false;
    private bool githubCheckingStatus = false;
    private bool githubCreatingRepo = false;
    private string? githubStatusMessage = null;
    private AlertType githubStatusType = AlertType.Info;

    // File tree and multi-file editor support
    private List<FileTreeNode> fileTreeData = new();
    private List<OpenFileInfo> openFiles = new();
    private string? activeFileTab = null;
    private bool loadingFiles = false;
    private StandaloneCodeEditor? codeEditor; // Legacy single editor - kept for AI context compatibility

    private class OpenFileInfo
    {
        public string Path { get; set; } = "";
        public string Content { get; set; } = "";
        public string Language { get; set; } = "html";
        public StandaloneCodeEditor? Editor { get; set; }
        public bool IsDirty { get; set; }
    }

    public class FileTreeNode
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "file";
        public bool IsLeaf { get; set; } = true;
        public List<FileTreeNode>? Children { get; set; }
        public string FullPath { get; set; } = "";
    }

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
    [Inject] private IAppSystemPromptBuilder AppSystemPromptBuilder { get; set; }
    [Inject] private IAppEditorBridge CurrentWebAppContextService { get; set; }
    [Inject] private IAppMcpClient AppMcpClient { get; set; }

    [Parameter] public string AppId { get; set; }

    // Computed properties for steps
    private List<LinkedStepInfo> FilteredAppSteps => string.IsNullOrWhiteSpace(stepSearchText)
        ? appSteps
        : appSteps.Where(s =>
            (s.Name?.Contains(stepSearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (s.DisplayName?.Contains(stepSearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (s.Description?.Contains(stepSearchText, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

    protected override async Task OnInitializedAsync()
    {
        loading = true;

        CurrentWebAppContextService.CodeChanged += OnCodeChangedByMcpTools;
        AppMcpClient.BackendStepsChanged += OnBackendStepsChanged;

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
                AppMcpClient.SetCurrentApp(currentApp.Id);
                    
                await LoadAllSteps();
                await LoadGitHubStatus();
                await LoadFileTree();
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
            // Save all open files
            foreach (var file in openFiles.Where(f => f.Editor != null))
            {
                var content = await file.Editor!.GetValue();
                await AppsService.WriteAppFileAsync(Guid.Parse(AppId), file.Path, content);
                file.IsDirty = false;
            }

            // Reload the current app to get updated Definition.HtmlContent
            currentApp = await AppsService.Load(Guid.Parse(AppId));
            
            _ = MessageService.SuccessAsync("App saved successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save app");
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
                // Published app: inject the real API with the app slug
                var scriptWithSlug = automatonApiScript.Replace("'{{APP_SLUG}}'", $"'{currentApp.PublicUrl}'");
                var wrappedScript = $"<script>{scriptWithSlug}</script>";
                html = InjectScriptAtStart(html, wrappedScript);
            }
            else if (!string.IsNullOrEmpty(automatonApiScript))
            {
                // Unpublished app: inject the preview API that uses app ID instead of slug
                var previewScript = GetPreviewApiScript(currentApp.Id);
                html = InjectScriptAtStart(html, previewScript);
            }
            else
            {
                // No API script available: inject stub with error message
                var stubScript = GetPreviewStubScript();
                html = InjectScriptAtStart(html, stubScript);
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
    /// Inject script at the start of the document (after &lt;head&gt; or &lt;body&gt;) 
    /// so it's available before any user code runs.
    /// </summary>
    private static string InjectScriptAtStart(string html, string script)
    {
        // Try to inject after <head> first (preferred location for API scripts)
        if (html.Contains("<head>", StringComparison.OrdinalIgnoreCase))
        {
            var headIndex = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
            var insertIndex = headIndex + 6; // Length of "<head>"
            return html.Insert(insertIndex, script);
        }
        
        // Fall back to after <body>
        if (html.Contains("<body", StringComparison.OrdinalIgnoreCase))
        {
            // Find the end of the body tag (handles <body> and <body class="...">)
            var bodyIndex = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            var bodyEndIndex = html.IndexOf('>', bodyIndex);
            if (bodyEndIndex >= 0)
            {
                return html.Insert(bodyEndIndex + 1, script);
            }
        }
        
        // Last resort: prepend to the HTML
        return script + html;
    }

    private static string InjectScriptIntoHtml(string html, string script)
    {
        if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
            return html.Replace("</body>", $"{script}</body>", StringComparison.OrdinalIgnoreCase);
        else
            return html + script;
    }

    /// <summary>
    /// Generate a preview API script that uses the app ID and preview endpoints.
    /// This allows testing backend steps before publishing the app.
    /// </summary>
    private string GetPreviewApiScript(Guid appId)
    {
        // Modify the automatonApi script to use preview endpoints with app ID
        var previewScript = automatonApiScript!
            .Replace("'{{APP_SLUG}}'", $"'preview/{appId}'");
        
        return $"""
            <script>
            {previewScript}
            console.log('[AutomatonAPI Preview] Preview mode enabled for app ID: {appId}');
            console.log('[AutomatonAPI Preview] Backend steps will execute using preview endpoints.');
            </script>
            """;
    }

    private static string GetPreviewStubScript()
    {
        return """
            <script>
            // Preview stub for automatonApi - shown when API script is not available
            window.automatonApi = {
                executeStep: function(stepName, options) {
                    console.warn('[AutomatonAPI Preview] executeStep called for:', stepName);
                    console.warn('[AutomatonAPI Preview] API script not loaded. Please refresh the page.');
                    
                    return Promise.resolve({
                        success: false,
                        error: 'Preview Mode: API script not loaded. Please refresh the page.',
                        outputVariables: {
                            success: 'false',
                            error_message: 'API script not loaded.'
                        },
                        logs: ['[Preview] API script not loaded.']
                    });
                },
                executeFlow: function(flowName, options) {
                    console.warn('[AutomatonAPI Preview] executeFlow called for:', flowName);
                    return Promise.resolve({
                        success: false,
                        error: 'Preview Mode: API script not loaded.',
                        outputVariables: {},
                        logs: ['[Preview] API script not loaded.']
                    });
                },
                customCall: function(path, method, body) {
                    return Promise.reject(new Error('Preview Mode: API script not loaded.'));
                },
                getAppSlug: function() {
                    return null;
                }
            };
            console.log('[AutomatonAPI Preview] Stub loaded - API script not available.');
            </script>
            """;
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
            // Get content from active file tab if available
            if (!string.IsNullOrEmpty(activeFileTab))
            {
                var activeFile = openFiles.FirstOrDefault(f => f.Path == activeFileTab);
                if (activeFile?.Editor != null)
                {
                    return await activeFile.Editor.GetValue() ?? string.Empty;
                }
            }
            
            // Fall back to legacy single editor or app definition
            if (codeEditor != null) return await codeEditor.GetValue() ?? string.Empty;
            return currentApp.Definition?.HtmlContent ?? string.Empty;
        }
        catch
        {
            return currentApp.Definition?.HtmlContent ?? string.Empty;
        }
    }

    private AppAiChatContext CreateAiChatContext()
    {
        // Use unified context that supports both frontend and backend generation
        return new AppAiChatContext(
            AppSystemPromptBuilder, 
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
        AppMcpClient.BackendStepsChanged -= OnBackendStepsChanged;
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
    
    // App Steps Tab Methods
    private async Task LoadAllSteps()
    {
        loadingSteps = true;
        try
        {
            // Only load steps that are linked to this app
            appSteps = await AppStepsService.GetLinkedStepsAsync(currentApp.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load app steps");
            _ = MessageService.ErrorAsync("Failed to load steps");
        }
        finally
        {
            loadingSteps = false;
        }
    }

    private void OnStepSearchInput(ChangeEventArgs e)
    {
        stepSearchText = e.Value?.ToString() ?? "";
        StateHasChanged();
    }
        
    private void NavigateToStep(Guid stepId) => NavigationManager.NavigateTo($"/stepdesigner/{stepId}");

    private async Task ShowCreateAppStepDialog()
    {
        var stepName = "";
        
        var confirmed = await ModalService.ConfirmAsync(new ConfirmOptions
        {
            Title = "Create New Step",
            Content = new RenderFragment(builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddContent(1, "Enter a name for the new step:");
                builder.OpenElement(2, "input");
                builder.AddAttribute(3, "type", "text");
                builder.AddAttribute(4, "placeholder", "Step name");
                builder.AddAttribute(5, "style", "width: 100%; margin-top: 8px; padding: 8px; border: 1px solid #d9d9d9; border-radius: 4px;");
                builder.AddAttribute(6, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, e => stepName = e.Value?.ToString() ?? ""));
                builder.CloseElement();
                builder.CloseElement();
            }),
            OkText = "Create",
            CancelText = "Cancel"
        });

        if (confirmed && !string.IsNullOrWhiteSpace(stepName))
        {
            await CreateAppStep(stepName);
        }
    }

    private async Task CreateAppStep(string stepName)
    {
        try
        {
            var newStep = await AppStepsService.CreateStepForAppAsync(currentApp.Id, stepName);
            appSteps.Add(newStep);
            await InvokeAsync(StateHasChanged);
            _ = MessageService.SuccessAsync($"Step '{stepName}' created. Click Edit to configure it.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create app step");
            _ = MessageService.ErrorAsync($"Failed to create step: {ex.Message}");
        }
    }

    private async Task DeleteAppStep(Guid stepId)
    {
        try
        {
            // Delete the step completely (not just unlink)
            await AppStepsService.DeleteStepAsync(stepId);
            var step = appSteps.FirstOrDefault(s => s.Id == stepId);
            if (step != null)
            {
                appSteps.Remove(step);
            }
            await InvokeAsync(StateHasChanged);
            _ = MessageService.SuccessAsync("Step deleted");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to delete app step");
            _ = MessageService.ErrorAsync($"Failed to delete step: {ex.Message}");
        }
    }

    private async Task ShowStepCodeDialog(LinkedStepInfo step)
    {
        var code = GenerateStepCode(step);
        
        await ModalService.CreateModalAsync(new ModalOptions
        {
            Title = $"Code for '{step.DisplayName ?? step.Name}'",
            Content = new RenderFragment(builder =>
            {
                builder.OpenElement(0, "div");
                
                // Code block
                builder.OpenElement(1, "pre");
                builder.AddAttribute(2, "style", "background: #f6f8fa; border-radius: 6px; padding: 16px; font-family: 'Consolas', 'Monaco', monospace; font-size: 13px; overflow-x: auto; white-space: pre-wrap; margin: 0;");
                builder.AddContent(3, code);
                builder.CloseElement();
                
                // Copy button
                builder.OpenElement(4, "div");
                builder.AddAttribute(5, "style", "margin-top: 16px; text-align: right;");
                builder.OpenComponent<Button>(6);
                builder.AddAttribute(7, "Icon", "copy");
                builder.AddAttribute(8, "Type", ButtonType.Primary);
                builder.AddAttribute(9, "OnClick", EventCallback.Factory.Create<Microsoft.AspNetCore.Components.Web.MouseEventArgs>(this, async () =>
                {
                    await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", code);
                    _ = MessageService.SuccessAsync("Code copied to clipboard!");
                }));
                builder.AddAttribute(10, "ChildContent", (RenderFragment)(b => b.AddContent(0, "Copy to Clipboard")));
                builder.CloseComponent();
                builder.CloseElement();
                
                builder.CloseElement();
            }),
            Footer = null,
            Width = 600
        });
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

    // GitHub Integration Methods
    
    private async Task LoadGitHubStatus()
    {
        try
        {
            var response = await AppsService.GetFromApiAsync<GitHubTokenStatusResponse>("github/token/status");
            hasGitHubToken = response?.HasToken ?? false;

            if (currentApp.Definition.GitHub != null)
            {
                githubSyncEnabled = currentApp.Definition.GitHub.SyncEnabled;
                githubRepoUrl = currentApp.Definition.GitHub.RepositoryUrl ?? "";
                githubBranch = currentApp.Definition.GitHub.Branch ?? "main";
                githubPath = currentApp.Definition.GitHub.Path;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load GitHub status");
        }
    }

    private async Task OnGitHubSyncToggle(bool enabled)
    {
        githubSyncEnabled = enabled;
        
        if (!enabled)
        {
            // Disable sync
            currentApp.Definition.GitHub = null;
            await Save();
        }
        else
        {
            // Enable sync - initialize settings
            currentApp.Definition.GitHub = new GitHubSettings
            {
                SyncEnabled = true,
                Branch = "main"
            };
        }
        
        await InvokeAsync(StateHasChanged);
    }

    private async Task PushToGitHub()
    {
        if (string.IsNullOrWhiteSpace(githubRepoUrl))
        {
            _ = MessageService.WarningAsync("Please enter a repository URL first");
            return;
        }

        githubPushing = true;
        githubStatusMessage = null;

        try
        {
            // Save current settings
            await SaveGitHubSettings();

            var response = await AppsService.PostToApiAsync<GitHubPushResponse>(
                $"apps/{currentApp.Id}/github/push",
                new { commitMessage = $"Update {currentApp.Name} from Automaton" });

            if (response?.Success == true)
            {
                githubStatusMessage = "Successfully pushed to GitHub!";
                githubStatusType = AlertType.Success;
                _ = MessageService.SuccessAsync("App pushed to GitHub successfully");
                
                // Reload app to get updated sync status
                currentApp = await AppsService.Load(Guid.Parse(AppId));
                await LoadGitHubStatus();
            }
            else
            {
                githubStatusMessage = response?.Message ?? "Failed to push to GitHub";
                githubStatusType = AlertType.Error;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to push to GitHub");
            githubStatusMessage = $"Error: {ex.Message}";
            githubStatusType = AlertType.Error;
            _ = MessageService.ErrorAsync($"Failed to push to GitHub: {ex.Message}");
        }
        finally
        {
            githubPushing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task PullFromGitHub()
    {
        if (string.IsNullOrWhiteSpace(githubRepoUrl))
        {
            _ = MessageService.WarningAsync("Please enter a repository URL first");
            return;
        }

        var confirmed = await ModalService.ConfirmAsync(new ConfirmOptions
        {
            Title = "Pull from GitHub?",
            Content = "This will overwrite your local changes with the version from GitHub. Are you sure?",
            OkText = "Pull",
            CancelText = "Cancel"
        });

        if (!confirmed) return;

        githubPulling = true;
        githubStatusMessage = null;

        try
        {
            // Save current settings first
            await SaveGitHubSettings();

            var response = await AppsService.PostToApiAsync<GitHubPullResponse>(
                $"apps/{currentApp.Id}/github/pull",
                null);

            if (response?.Success == true)
            {
                githubStatusMessage = "Successfully pulled from GitHub!";
                githubStatusType = AlertType.Success;
                _ = MessageService.SuccessAsync("App pulled from GitHub successfully");
                
                // Reload app to get updated content
                currentApp = await AppsService.Load(Guid.Parse(AppId));
                await LoadGitHubStatus();
                
                // Update editor
                if (codeEditor != null && !string.IsNullOrEmpty(currentApp.Definition.HtmlContent))
                {
                    await codeEditor.SetValue(currentApp.Definition.HtmlContent);
                }
            }
            else
            {
                githubStatusMessage = response?.Message ?? "Failed to pull from GitHub";
                githubStatusType = AlertType.Error;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to pull from GitHub");
            githubStatusMessage = $"Error: {ex.Message}";
            githubStatusType = AlertType.Error;
            _ = MessageService.ErrorAsync($"Failed to pull from GitHub: {ex.Message}");
        }
        finally
        {
            githubPulling = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task CheckGitHubStatus()
    {
        if (string.IsNullOrWhiteSpace(githubRepoUrl))
        {
            _ = MessageService.WarningAsync("Please enter a repository URL first");
            return;
        }

        githubCheckingStatus = true;
        githubStatusMessage = null;

        try
        {
            await SaveGitHubSettings();

            var response = await AppsService.GetFromApiAsync<GitHubStatusResponse>(
                $"apps/{currentApp.Id}/github/status");

            if (response != null)
            {
                githubStatusMessage = $"Status: {response.Status}";
                githubStatusType = response.Status == "Synced" ? AlertType.Success : AlertType.Warning;
                _ = MessageService.InfoAsync($"GitHub status: {response.Status}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to check GitHub status");
            githubStatusMessage = $"Error: {ex.Message}";
            githubStatusType = AlertType.Error;
        }
        finally
        {
            githubCheckingStatus = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task CreateGitHubRepo()
    {
        githubCreatingRepo = true;
        githubStatusMessage = null;

        try
        {
            var response = await AppsService.PostToApiAsync<CreateGitHubRepoResponse>(
                "github/repositories",
                new
                {
                    name = currentApp.Name.ToLowerInvariant().Replace(" ", "-"),
                    description = currentApp.Description ?? $"Web app created with Automaton",
                    isPrivate = true
                });

            if (response?.Success == true && !string.IsNullOrEmpty(response.RepositoryUrl))
            {
                githubRepoUrl = response.RepositoryUrl;
                githubStatusMessage = $"Repository created: {response.RepositoryUrl}";
                githubStatusType = AlertType.Success;
                _ = MessageService.SuccessAsync("GitHub repository created successfully!");
                
                await SaveGitHubSettings();
            }
            else
            {
                githubStatusMessage = response?.Message ?? "Failed to create repository";
                githubStatusType = AlertType.Error;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create GitHub repository");
            githubStatusMessage = $"Error: {ex.Message}";
            githubStatusType = AlertType.Error;
            _ = MessageService.ErrorAsync($"Failed to create repository: {ex.Message}");
        }
        finally
        {
            githubCreatingRepo = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task SaveGitHubSettings()
    {
        try
        {
            await AppsService.PutToApiAsync(
                $"apps/{currentApp.Id}/github/settings",
                new
                {
                    repositoryUrl = githubRepoUrl,
                    branch = githubBranch,
                    path = githubPath,
                    syncEnabled = githubSyncEnabled
                });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save GitHub settings");
            throw;
        }
    }

    // File tree and multi-file editor methods
    private async Task LoadFileTree()
    {
        loadingFiles = true;
        
        try
        {
            var files = await AppsService.GetAppFilesAsync(Guid.Parse(AppId));
            
            Logger.LogInformation("LoadFileTree: Received {FileCount} files from API", files.Count);
            
            // Normalize paths to use forward slashes
            var normalizedFiles = files
                .Select(f => f.Replace("\\", "/"))
                .ToList();
            
            // Build the tree
            var newTree = BuildFileTree(normalizedFiles);
            
            // Create a NEW list reference - this is critical for Blazor change detection
            fileTreeData = new List<FileTreeNode>(newTree);
            
            Logger.LogInformation("LoadFileTree: Set fileTreeData with {Count} nodes", fileTreeData.Count);

            // Auto-open index.html if it exists and no files are open
            if (!openFiles.Any() && normalizedFiles.Any(f => f.EndsWith("index.html")))
            {
                var indexFile = normalizedFiles.FirstOrDefault(f => f.EndsWith("index.html"));
                if (indexFile != null)
                {
                    await OpenFile("index.html");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load file tree");
            _ = MessageService.ErrorAsync("Failed to load files");
        }
        finally
        {
            loadingFiles = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private List<FileTreeNode> BuildFileTree(List<string> files)
    {
        var root = new List<FileTreeNode>();
        var folderMap = new Dictionary<string, FileTreeNode>();

        foreach (var file in files.Where(f => f != "_metadata.json").OrderBy(f => f))
        {
            var parts = file.Split('/');
            var currentLevel = root;
            var currentPath = "";

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";
                var isFile = i == parts.Length - 1;

                if (isFile)
                {
                    // Add file node
                    currentLevel.Add(new FileTreeNode
                    {
                        Key = currentPath,
                        Title = part,
                        Icon = GetFileIcon(part),
                        IsLeaf = true,
                        FullPath = currentPath
                    });
                }
                else
                {
                    // Add or get folder node
                    if (!folderMap.ContainsKey(currentPath))
                    {
                        var folderNode = new FileTreeNode
                        {
                            Key = currentPath,
                            Title = part,
                            Icon = "folder",
                            IsLeaf = false,
                            FullPath = currentPath,
                            Children = new List<FileTreeNode>()
                        };
                        currentLevel.Add(folderNode);
                        folderMap[currentPath] = folderNode;
                    }
                    currentLevel = folderMap[currentPath].Children!;
                }
            }
        }

        return root;
    }

    private string GetFileIcon(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".html" => "file-text",
            ".css" => "file-text",
            ".js" => "file-text",
            ".json" => "file-text",
            ".md" => "file-markdown",
            ".svg" => "file-image",
            ".png" or ".jpg" or ".jpeg" or ".gif" => "file-image",
            _ => "file"
        };
    }

    private string GetLanguageFromFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".html" => "html",
            ".css" => "css",
            ".js" => "javascript",
            ".json" => "json",
            ".md" => "markdown",
            ".svg" => "xml",
            _ => "plaintext"
        };
    }

    private Task OnTreeNodeLoad(TreeEventArgs<FileTreeNode> args)
    {
        // No lazy loading needed - we load all files upfront
        return Task.CompletedTask;
    }

    private async Task OnFileTreeNodeClick(TreeEventArgs<FileTreeNode> args)
    {
        if (args.Node?.DataItem?.IsLeaf == true)
        {
            await OpenFile(args.Node.DataItem.FullPath);
        }
    }

    private async Task OpenFile(string filePath)
    {
        try
        {
            // Check if file is already open
            var existingFile = openFiles.FirstOrDefault(f => f.Path == filePath);
            if (existingFile != null)
            {
                activeFileTab = filePath;
                await InvokeAsync(StateHasChanged);
                return;
            }

            // Load file content
            var content = await AppsService.ReadAppFileAsync(Guid.Parse(AppId), filePath);
            if (content == null)
            {
                _ = MessageService.WarningAsync($"File {filePath} not found");
                return;
            }

            // Create new open file
            var openFile = new OpenFileInfo
            {
                Path = filePath,
                Content = content,
                Language = GetLanguageFromFileName(filePath),
                IsDirty = false
            };

            openFiles.Add(openFile);
            activeFileTab = filePath;

            await InvokeAsync(StateHasChanged);

            // Set editor content after render
            await Task.Delay(100);
            if (openFile.Editor != null)
            {
                await openFile.Editor.SetValue(content);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to open file {FilePath}", filePath);
            _ = MessageService.ErrorAsync($"Failed to open file: {ex.Message}");
        }
    }

    private async Task CloseFileTab(string filePath)
    {
        var file = openFiles.FirstOrDefault(f => f.Path == filePath);
        if (file == null) return;

        // TODO: Check if file is dirty and prompt to save
        if (file.IsDirty)
        {
            var confirm = await ModalService.ConfirmAsync(new ConfirmOptions
            {
                Title = "Unsaved changes",
                Content = $"File {GetFileName(filePath)} has unsaved changes. Close anyway?",
                OkText = "Close",
                CancelText = "Cancel"
            });

            if (!confirm) return;
        }

        openFiles.Remove(file);

        // Select another tab if this was the active one
        if (activeFileTab == filePath)
        {
            activeFileTab = openFiles.Any() ? openFiles.Last().Path : null;
        }

        await InvokeAsync(StateHasChanged);
    }

    private string GetFileName(string path)
    {
        return Path.GetFileName(path);
    }

    private async Task OnEditorContentChanged(string filePath)
    {
        var file = openFiles.FirstOrDefault(f => f.Path == filePath);
        if (file != null)
        {
            file.IsDirty = true;
        }
    }

    private StandaloneEditorConstructionOptions GetEditorOptions(StandaloneCodeEditor editor, string language)
    {
        return new StandaloneEditorConstructionOptions
        {
            Language = language,
            AutomaticLayout = true,
            Theme = "vs",
            Minimap = new EditorMinimapOptions { Enabled = false },
            LineNumbers = "on",
            RenderLineHighlight = "all",
            ScrollBeyondLastLine = false,
            FontSize = 14
        };
    }

    private async Task ShowCreateFileDialog()
    {
        // TODO: Implement file creation dialog
        MessageService.Info("File creation dialog - to be implemented");
        await Task.CompletedTask;
    }

    private async Task ShowCreateFolderDialog()
    {
        // TODO: Implement folder creation dialog
        MessageService.Info("Folder creation dialog - to be implemented");
        await Task.CompletedTask;
    }

    private void NavigateToGitHubSettings()
    {
        // Navigate to a settings page where users can configure their GitHub token
        NavigationManager.NavigateTo("/settings?tab=github");
    }

    private class GitHubTokenStatusResponse
    {
        public bool HasToken { get; set; }
        public string? Username { get; set; }
    }

    private class GitHubPushResponse
    {
        public bool Success { get; set; }
        public string? CommitSha { get; set; }
        public string? Message { get; set; }
    }

    private class GitHubPullResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public bool IsMultiFile { get; set; }
    }

    private class GitHubStatusResponse
    {
        public bool SyncEnabled { get; set; }
        public string Status { get; set; } = "None";
        public string? RepositoryUrl { get; set; }
        public string? Branch { get; set; }
        public DateTime? LastSyncedAt { get; set; }
    }

    private class CreateGitHubRepoResponse
    {
        public bool Success { get; set; }
        public string? RepositoryUrl { get; set; }
        public string? Message { get; set; }
    }
}
