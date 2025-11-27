using AntDesign;
using Automaton.Studio.Models;
using Automaton.Studio.Pages.Apps.Components.NewAppDialog;
using Automaton.Studio.Pages.Apps.Components.PublishAppDialog;
using Automaton.Studio.Shared.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Automaton.Studio.Pages.Apps;

partial class AppsPage : ComponentBase
{
    public bool loading;
    private bool _isGridView;

    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private AppsViewModel AppsViewModel { get; set; } = default!;
    [Inject] private ModalService ModalService { get; set; }
    [Inject] private MessageService MessageService { get; set; }
    [Inject] private IJSRuntime JSRuntime { get; set; }

    protected override async Task OnInitializedAsync()
    {
        loading = true;

        // Set default view mode based on user permissions
        // If user can manage apps (Personal mode or Project mode with manage rights), default to List view
        // Otherwise, default to Grid view
        _isGridView = !AppsViewModel.CanManageApps;

        try
        {
            await AppsViewModel.GetApps();
        }
        catch
        {
            _ = MessageService.ErrorAsync("Failed to load apps list");
        }
        finally
        {
            loading = false;
        }

        await base.OnInitializedAsync();
    }

    private void ToggleViewMode()
    {
        _isGridView = !_isGridView;
    }

    private void EditApp(Guid id)
    {
        NavigationManager.NavigateTo($"app/{id}");
    }

    private async Task ViewApp(string publicUrl)
    {
        if (!string.IsNullOrEmpty(publicUrl))
        {
            // Open in new tab to avoid disposing the Blazor circuit
            await JSRuntime.InvokeVoidAsync("open", $"/web/{publicUrl}", "_blank");
        }
    }

    private async Task DeleteApp(Guid id)
    {
        await AppsViewModel.DeleteApp(id);
        StateHasChanged();
    }

    private async Task ShowPublishDialog(Guid id)
    {
        // Get the app to retrieve its display name
        var app = AppsViewModel.Apps?.FirstOrDefault(a => a.Id == id);
        if (app == null)
        {
            _ = MessageService.ErrorAsync("App not found");
            return;
        }

        var model = new PublishAppModel
        {
            AppId = id,
            DisplayName = app.DisplayName,
            PublicUrl = app.PublicUrl ?? string.Empty,
            RequiresAuthentication = true, // Default to protected
            ExecutionRunnerId = app.ExecutionRunnerId
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
                _ = MessageService.WarningAsync("Please enter a URL slug");
                return;
            }

            try
            {
                await AppsViewModel.PublishApp(id, model.PublicUrl, model.RequiresAuthentication, model.ExecutionRunnerId);

                var accessType = model.RequiresAuthentication ? "Protected" : "Public";
                _ = MessageService.SuccessAsync($"App published! ({accessType}) Access it at /web/{model.PublicUrl}");
                StateHasChanged();
            }
            catch (Exception ex)
            {
                _ = MessageService.ErrorAsync($"Failed to publish app: {ex.Message}");
            }
        };
    }

    private async Task UnpublishApp(Guid id)
    {
        var confirmed = await ModalService.ConfirmAsync(new ConfirmOptions
        {
            Title = "Unpublish App?",
            Content = "The app will no longer be accessible via public URL. You can republish it later.",
            OkText = "Unpublish",
            CancelText = "Cancel"
        });

        if (confirmed)
        {
            try
            {
                await AppsViewModel.UnpublishApp(id);
                _ = MessageService.SuccessAsync("App unpublished successfully");
                StateHasChanged();
            }
            catch
            {
                _ = MessageService.ErrorAsync("Failed to unpublish app");
            }
        }
    }

    private async Task ShowNewAppDialog()
    {
        var model = new NewApp();

        var modalRef = await ModalService.CreateModalAsync<NewAppDialog, NewApp>(
            new ModalOptions { Title = "New App" }, model
        );

        modalRef.OnOk = async () =>
        {
            try
            {
                await AppsViewModel.CreateApp(model);
                StateHasChanged();
            }
            catch
            {
                _ = MessageService.ErrorAsync($"App {model.Name} could not be created");
            }
        };
    }

    private async Task ExecuteApp(AppListItem app)
    {
        if (app.IsPublished)
        {
            // Open published app in new tab to avoid disposing the Blazor circuit
            await JSRuntime.InvokeVoidAsync("open", $"/web/{app.PublicUrl}", "_blank");
        }
        else
        {
            _ = MessageService.InfoAsync("This app has not been published yet. Please publish it first to execute.");
        }
    }

    private string GetCardBackgroundColor(AppListItem app)
    {
        // Only apply background color to the card itself
        if (!string.IsNullOrEmpty(app.Appearance?.BackgroundColor))
        {
            return $"background-color: {app.Appearance.BackgroundColor};";
        }
        
        return "background-color: #ffffff;";
    }

    private string GetCardBackgroundImage(AppListItem app)
    {
        // Apply background image with opacity to the separate background layer
        var styles = new List<string>();
        
        if (!string.IsNullOrEmpty(app.Appearance?.BackgroundImageUrl))
        {
            styles.Add($"background-image: url('{app.Appearance.BackgroundImageUrl}')");
            styles.Add($"background-size: cover");
            styles.Add($"background-position: center");
            styles.Add($"opacity: {app.Appearance.BackgroundImageOpacity}");
        }
        
        return string.Join("; ", styles);
    }

    private string GetTitleStyle(AppListItem app)
    {
        var styles = new List<string>();
        
        if (!string.IsNullOrEmpty(app.Appearance?.TitleFontColor))
        {
            styles.Add($"color: {app.Appearance.TitleFontColor}");
        }
        else
        {
            styles.Add("color: #ffffff");
        }
        
        return string.Join("; ", styles);
    }

    private string GetAppDisplayTitle(AppListItem app)
    {
        return !string.IsNullOrEmpty(app.DisplayName) ? app.DisplayName : app.Name;
    }

    private List<string> GetAppActionCardBullets()
    {
        return new List<string>
        {
            "Create interactive web applications",
            "Connect to existing Flows",
            "Publish to public URLs",
            "Beautiful UI with custom styling"
        };
    }

    private List<EnhancedEmpty.EmptyActionModel> GetAppSecondaryActions()
    {
        return new List<EnhancedEmpty.EmptyActionModel>();
    }

    private List<EnhancedEmpty.FeatureCardModel> GetAppFeatureCards()
    {
        return new List<EnhancedEmpty.FeatureCardModel>
        {
            new() 
            { 
                Title = "Build web apps instantly", 
                Description = "Create interactive applications with HTML, CSS, and JavaScript without complex infrastructure.", 
                Icon = "rocket",
                IconColor = "#1890ff"
            },
            new() 
            { 
                Title = "Connect to your Flows", 
                Description = "Execute your automation flows from web applications and get results in real-time.", 
                Icon = "api",
                IconColor = "#52c41a"
            },
            new() 
            { 
                Title = "Publish and share", 
                Description = "Deploy your apps publicly with simple URLs and optional authentication.", 
                Icon = "cloud-upload",
                IconColor = "#fa8c16"
            }
        };
    }

    private List<EnhancedEmpty.HelpLinkModel> GetAppHelpLinks()
    {
        return new List<EnhancedEmpty.HelpLinkModel>
        {
            new() { Text = "Apps overview", Url = "#", Icon = "file-text", Description = "Learn about building web apps" },
            new() { Text = "Getting started", Url = "#", Icon = "play-circle", Description = "Your first app in 5 minutes" },
            new() { Text = "Flow integration", Url = "#", Icon = "link", Description = "Connect apps to your workflows" },
            new() { Text = "Publishing guide", Url = "#", Icon = "cloud", Description = "Deploy apps publicly" }
        };
    }
}
