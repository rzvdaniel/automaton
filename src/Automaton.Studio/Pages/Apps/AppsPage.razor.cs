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

    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private AppsViewModel AppsViewModel { get; set; } = default!;
    [Inject] private ModalService ModalService { get; set; }
    [Inject] private MessageService MessageService { get; set; }
    [Inject] private IJSRuntime JSRuntime { get; set; }

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
            await MessageService.ErrorAsync("App not found");
            return;
        }

        var model = new PublishAppModel
        {
            AppId = id,
            DisplayName = app.DisplayName,
            PublicUrl = app.PublicUrl ?? string.Empty
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
                await AppsViewModel.PublishApp(id, model.PublicUrl);
                await MessageService.SuccessAsync($"App published! Access it at /web/{model.PublicUrl}");
                StateHasChanged();
            }
            catch (Exception ex)
            {
                await MessageService.ErrorAsync($"Failed to publish app: {ex.Message}");
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
                await MessageService.SuccessAsync("App unpublished successfully");
                StateHasChanged();
            }
            catch
            {
                await MessageService.ErrorAsync("Failed to unpublish app");
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
