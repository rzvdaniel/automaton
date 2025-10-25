using AntDesign;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Automaton.Studio.Pages.Apps.Components.PublishAppDialog;
using BlazorMonaco.Editor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text;

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

    [Inject] private AppsService AppsService { get; set; }
    [Inject] private MessageService MessageService { get; set; }
 [Inject] private NavigationManager NavigationManager { get; set; }
    [Inject] private IJSRuntime JSRuntime { get; set; }
    [Inject] private ModalService ModalService { get; set; }

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
       PublicUrl = currentApp.PublicUrl ?? AppsService.GenerateSlugPreview(currentApp.DisplayName)
            };

    var modalRef = await ModalService.CreateModalAsync<PublishAppDialog, PublishAppModel>(
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
         await AppsService.PublishAsync(currentApp.Id, model.PublicUrl);
    
         // Update local model
     currentApp.IsPublished = true;
                 currentApp.PublicUrl = model.PublicUrl;
          currentApp.PublishedAt = DateTime.UtcNow;
currentApp.Version++;

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

    // Preview Methods - Phase 1 Implementation

    /// <summary>
    /// Get HTML content for preview with Automaton bridge injected
    /// </summary>
    private string GetPreviewHtml()
    {
        try
        {
            var html = currentApp.Definition?.HtmlContent ?? GetDefaultHtmlContent();
            return InjectAutomatonBridge(html, isPreview: true);
        }
   catch
      {
            return GetDefaultHtmlContent();
}
    }

    /// <summary>
    /// Inject Automaton JavaScript bridge into HTML content
    /// </summary>
    private string InjectAutomatonBridge(string htmlContent, bool isPreview = false)
    {
        var bridge = isPreview ? GetPreviewBridge() : GetProductionBridge();

        // Try to insert before </head>
  if (htmlContent.Contains("</head>", StringComparison.OrdinalIgnoreCase))
        {
     return htmlContent.Replace("</head>", $"{bridge}</head>", StringComparison.OrdinalIgnoreCase);
      }

        // Try to insert before </body>
        if (htmlContent.Contains("</body>", StringComparison.OrdinalIgnoreCase))
  {
  return htmlContent.Replace("</body>", $"{bridge}</body>", StringComparison.OrdinalIgnoreCase);
        }

// If no head or body tags, prepend to content
        return bridge + htmlContent;
    }

    /// <summary>
    /// Get preview mode Automaton bridge (mock responses)
    /// </summary>
    private string GetPreviewBridge()
    {
     return @"
<script>
    // Automaton Bridge - Preview Mode
  window.Automaton = {
        executeFlow: async function(flowId, inputs) {
            console.log('🔧 Preview Mode: Automaton.executeFlow called');
       console.log('Flow ID:', flowId);
   console.log('Inputs:', inputs);
      
            // Return mock response in preview mode
     return {
         success: true,
                message: 'Preview mode - flow not executed',
 data: {
       timestamp: new Date().toISOString(),
        previewMode: true
        }
            };
    },
        version: '1.0.0',
        mode: 'preview'
    };
    
    console.log('✅ Automaton bridge loaded (Preview Mode)');
</script>";
    }

    /// <summary>
    /// Get production mode Automaton bridge (real API calls)
    /// </summary>
    private string GetProductionBridge()
    {
        return @"
<script>
    // Automaton Bridge - Production Mode
    window.Automaton = {
        executeFlow: async function(flowId, inputs = {}) {
            try {
              console.log('🚀 Executing flow:', flowId);
         
       const response = await fetch('/api/flows/execute', {
 method: 'POST',
       headers: {
          'Content-Type': 'application/json'
    },
       body: JSON.stringify({
           flowId: flowId,
          inputs: inputs
         })
      });
  
              if (!response.ok) {
        const error = await response.json();
            throw new Error(error.message || 'Flow execution failed');
          }
       
          const result = await response.json();
      console.log('✅ Flow executed successfully:', result);
                return result;
            } catch (error) {
    console.error('❌ Automaton.executeFlow error:', error);
      throw error;
            }
        },
        version: '1.0.0',
        mode: 'production'
    };
    
    console.log('✅ Automaton bridge loaded (Production Mode)');
</script>";
    }

    /// <summary>
    /// Refresh the preview iframe
    /// </summary>
    private async Task RefreshPreview()
    {
        try
        {
        // Get latest code from Monaco editor
  var code = await codeEditor.GetValue();
  currentApp.Definition.HtmlContent = code;
    
    // Force re-render by updating state
       await InvokeAsync(StateHasChanged);
        
  await MessageService.SuccessAsync("Preview refreshed", 1);
  }
      catch (Exception ex)
        {
            await MessageService.ErrorAsync($"Failed to refresh preview: {ex.Message}");
        }
    }

    /// <summary>
    /// Open preview in new browser tab
 /// </summary>
    private async Task OpenPreviewInNewTab()
    {
        try
        {
            // Get current code from editor
            var code = await codeEditor.GetValue();
       var htmlWithBridge = InjectAutomatonBridge(code, isPreview: true);
       
            // Convert to base64 data URL
  var bytes = Encoding.UTF8.GetBytes(htmlWithBridge);
   var base64 = Convert.ToBase64String(bytes);
          var dataUrl = $"data:text/html;base64,{base64}";
        
     // Open in new tab
            await JSRuntime.InvokeVoidAsync("open", dataUrl, "_blank");
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync($"Failed to open preview: {ex.Message}");
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

    // AI Chat Panel Methods

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
 // BlazorMonaco handles disposal internally
        await Task.CompletedTask;
    }
}
