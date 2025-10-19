using AntDesign;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Automaton.Studio.Pages.Apps;

partial class AppDesignerPage : ComponentBase, IAsyncDisposable
{
    private bool loading = false;
    private bool saving = false;
    private AppModel currentApp = new AppModel
    {
        Name = "Loading...",
        DisplayName = "",
        Description = "",
        Definition = new AppDefinition()
    };

    [Inject] private IJSRuntime JSRuntime { get; set; }
    [Inject] private AppsService AppsService { get; set; }
    [Inject] private MessageService MessageService { get; set; }
    [Inject] private NavigationManager NavigationManager { get; set; }

    [Parameter] public string AppId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        loading = true;

        try
        {
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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                // Initialize Monaco editor with the current HTML content
                var initialValue = currentApp.Definition?.HtmlContent ?? GetDefaultHtmlContent();
                await JSRuntime.InvokeVoidAsync("MonacoEditorInterop.initialize", 
                    "app-monaco-editor", initialValue, "html");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing Monaco editor: {ex.Message}");
                await MessageService.WarningAsync("Monaco editor failed to initialize. Please refresh the page.");
            }
        }
    }

    private async Task Save()
    {
        saving = true;

        try
        {
            // Get the current code from Monaco editor
            var code = await JSRuntime.InvokeAsync<string>("MonacoEditorInterop.getValue");
            currentApp.Definition.HtmlContent = code;

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

    private async Task Preview()
    {
        try
        {
            // Get the current code from Monaco editor
            var code = await JSRuntime.InvokeAsync<string>("MonacoEditorInterop.getValue");
            
            // Open preview in new tab
            // We'll implement a preview page later
            await MessageService.InfoAsync("Preview feature coming soon!");
        }
        catch
        {
            await MessageService.ErrorAsync("Failed to open preview");
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
        try
        {
            await JSRuntime.InvokeVoidAsync("MonacoEditorInterop.dispose");
        }
        catch
        {
            // Editor already disposed or not initialized
        }
    }
}
