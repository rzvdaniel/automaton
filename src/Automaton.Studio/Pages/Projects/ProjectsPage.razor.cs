using AntDesign;
using Automaton.Studio.Enums;
using Automaton.Studio.Models;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Projects;

partial class ProjectsPage : ComponentBase
{
    public bool loading;
    private bool createProjectVisible = false;
    private bool createProjectLoading = false;
    private CreateProjectRequest createProjectModel = new();
    
    private bool editProjectVisible = false;
    private bool editProjectLoading = false;
    private UpdateProjectRequest editProjectModel = new();
    private Guid editingProjectId;

    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private ProjectsViewModel ProjectsViewModel { get; set; } = default!;
    [Inject] private MessageService MessageService { get; set; } = default!;
    [Inject] private ModalService ModalService { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        loading = true;

        try
        {
            await ProjectsViewModel.LoadProjects();
        }
        catch
        {
            _ = MessageService.ErrorAsync("Failed to load projects list");
        }
        finally
        {
            loading = false;
        }

        await base.OnInitializedAsync();
    }

    private void ShowCreateProjectDialog()
    {
        createProjectModel = new CreateProjectRequest();
        createProjectVisible = true;
    }

    private async Task HandleCreateProject()
    {
        if (string.IsNullOrWhiteSpace(createProjectModel.Name) || 
            string.IsNullOrWhiteSpace(createProjectModel.DisplayName))
        {
            _ = MessageService.WarningAsync("Please fill in required fields");
            return;
        }

        createProjectLoading = true;
        try
        {
            await ProjectsViewModel.CreateProject(createProjectModel);
            _ = MessageService.SuccessAsync("Project created successfully");
            createProjectVisible = false;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            _ = MessageService.ErrorAsync($"Failed to create project: {ex.Message}");
        }
        finally
        {
            createProjectLoading = false;
        }
    }

    private void ViewProject(Guid projectId)
    {
        // Navigate to project details page (to be implemented in Stage 7)
        NavigationManager.NavigateTo($"/projects/{projectId}");
    }

    private async Task EditProject(Guid projectId)
    {
        var project = ProjectsViewModel.Projects?.FirstOrDefault(p => p.Id == projectId);
        if (project == null)
        {
            _ = MessageService.ErrorAsync("Project not found");
            return;
        }

        editingProjectId = projectId;
        editProjectModel = new UpdateProjectRequest
        {
            DisplayName = project.DisplayName,
            Description = project.Description
        };
        editProjectVisible = true;
    }

    private async Task HandleEditProject()
    {
        if (string.IsNullOrWhiteSpace(editProjectModel.DisplayName))
        {
            _ = MessageService.WarningAsync("Display name is required");
            return;
        }

        editProjectLoading = true;
        try
        {
            await ProjectsViewModel.UpdateProject(editingProjectId, editProjectModel);
            _ = MessageService.SuccessAsync("Project updated successfully");
            editProjectVisible = false;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            _ = MessageService.ErrorAsync($"Failed to update project: {ex.Message}");
        }
        finally
        {
            editProjectLoading = false;
        }
    }

    private async Task DeleteProject(Guid projectId, string displayName)
    {
        var confirmed = await ModalService.ConfirmAsync(new ConfirmOptions
        {
            Title = "Delete Project",
            Content = $"Are you sure you want to delete '{displayName}'? This action cannot be undone.",
            OkText = "Delete",
            OkType = ButtonType.Default,
            CancelText = "Cancel"
        });

        if (confirmed)
        {
            try
            {
                await ProjectsViewModel.DeleteProject(projectId);
                _ = MessageService.SuccessAsync("Project deleted successfully");
                StateHasChanged();
            }
            catch (Exception ex)
            {
                _ = MessageService.ErrorAsync($"Failed to delete project: {ex.Message}");
            }
        }
    }

    private string GetRoleColor(ProjectRole role)
    {
        return role switch
        {
            ProjectRole.ProjectAdmin => "red",
            ProjectRole.Developer => "blue",
            ProjectRole.AppUser => "green",
            ProjectRole.Viewer => "default",
            _ => "default"
        };
    }
}
