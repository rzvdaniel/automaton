using AntDesign;
using Automaton.Studio.Models;
using Automaton.Studio.Pages.Apps.Components.NewAppDialog;
using Automaton.Studio.Shared.Components;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Apps;

partial class AppsPage : ComponentBase
{
    public bool loading;

    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private AppsViewModel AppsViewModel { get; set; } = default!;
    [Inject] private ModalService ModalService { get; set; }
    [Inject] private MessageService MessageService { get; set; }

    protected override async Task OnInitializedAsync()
    {
        loading = true;

        try
        {
            await AppsViewModel.GetApps();
        }
        catch
        {
            await MessageService.ErrorAsync("Failed to load apps list");
        }
        finally
        {
            loading = false;
        }

        await base.OnInitializedAsync();
    }

    private void EditApp(Guid id)
    {
        NavigationManager.NavigateTo($"appdesigner/{id}");
    }

    private void ViewApp(string publicUrl)
    {
        if (!string.IsNullOrEmpty(publicUrl))
        {
            NavigationManager.NavigateTo($"/apps/{publicUrl}", true);
        }
    }

    private async Task DeleteApp(Guid id)
    {
        await AppsViewModel.DeleteApp(id);
        StateHasChanged();
    }

    private async Task ShowPublishDialog(Guid id)
    {
        var publicUrl = "";
        var inputRef = "";

        // Create a simple string input modal
        var modalContent = "Enter a public URL slug for your app (e.g., 'my-dashboard'):";
        
        var confirmed = await ModalService.ConfirmAsync(new ConfirmOptions
        {
            Title = "Publish App",
            Content = modalContent,
            OnOk = async (e) =>
            {
                // Show input dialog
                var result = await ModalService.ConfirmAsync(new ConfirmOptions
                {
                    Title = "Enter URL Slug",
                    Content = "URL slug (lowercase, letters, numbers, hyphens only):"
                });
            }
        });

        // For now, use a simple approach - we'll enhance this later
        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            try
            {
                await AppsViewModel.PublishApp(id, publicUrl.ToLower().Trim());
                await MessageService.SuccessAsync($"App published at /apps/{publicUrl}");
                StateHasChanged();
            }
            catch
            {
                await MessageService.ErrorAsync("Failed to publish app. The URL might already be taken.");
            }
        }
    }

    private async Task UnpublishApp(Guid id)
    {
        try
        {
            await AppsViewModel.UnpublishApp(id);
            await MessageService.SuccessAsync("App unpublished successfully");
            StateHasChanged();
        }
        catch
        {
            await MessageService.ErrorAsync("Failed to unpublish app");
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
                await MessageService.ErrorAsync($"App {model.Name} could not be created");
            }
        };
    }

    private List<EnhancedEmpty.EmptyActionModel> GetAppSecondaryActions()
    {
        return new List<EnhancedEmpty.EmptyActionModel>
        {
            new() { Text = "Browse Templates", Icon = "eye", OnClick = () => Task.FromResult(0) }
        };
    }

    private List<string> GetAppActionCardBullets()
    {
        return new List<string>
        {
            "HTML, CSS, and JavaScript support",
            "Connect to existing Flows for backend logic",
            "Publish apps for public access",
            "PWA support for mobile deployment"
        };
    }

    private List<EnhancedEmpty.FeatureCardModel> GetAppFeatureCards()
    {
        return new List<EnhancedEmpty.FeatureCardModel>
        {
            new() 
            { 
                Title = "Create powerful web applications", 
                Description = "Build web apps using HTML, CSS, and JavaScript. Use the integrated code editor to develop and test your applications.", 
                Icon = "code",
                IconColor = "#1890ff"
            },
            new() 
            { 
                Title = "Connect to your Flows", 
                Description = "Use your existing Flows as backend services. Your apps can invoke Flows to retrieve data, process information, and more.", 
                Icon = "api",
                IconColor = "#52c41a"
            },
            new() 
            { 
                Title = "Publish and share", 
                Description = "Publish your apps with a public URL and share them with users. Apps can be accessed without authentication for external users.", 
                Icon = "share-alt",
                IconColor = "#fa8c16"
            }
        };
    }

    private List<EnhancedEmpty.HelpLinkModel> GetAppHelpLinks()
    {
        return new List<EnhancedEmpty.HelpLinkModel>
        {
            new() { Text = "Apps overview", Url = "#", Icon = "appstore", Description = "Learn about creating and managing web apps." },
            new() { Text = "Building your first app", Url = "#", Icon = "code", Description = "A guide to creating your first web application." },
            new() { Text = "Connecting to Flows", Url = "#", Icon = "api", Description = "Learn how to connect your apps to Flows." },
            new() { Text = "Publishing apps", Url = "#", Icon = "cloud-upload", Description = "Guide on publishing and sharing your apps." }
        };
    }
}
