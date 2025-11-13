using Automaton.Studio.Enums;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using AntDesign;
using Microsoft.AspNetCore.Components;
using Serilog;

namespace Automaton.Studio.Pages.Projects;

public class ProjectSettingsViewModel
{
    private readonly ProjectsClientService _projectsService;
    private readonly ProjectMembersClientService _membersService;
    private readonly ProjectResourcesClientService _resourcesService;
    private readonly IMessageService _messageService;
    private readonly ProjectContextService _projectContextService;

    public ProjectSettingsViewModel(
        ProjectsClientService projectsService,
        ProjectMembersClientService membersService,
        ProjectResourcesClientService resourcesService,
        IMessageService messageService,
        ProjectContextService projectContextService)
    {
        _projectsService = projectsService;
        _membersService = membersService;
        _resourcesService = resourcesService;
        _messageService = messageService;
        _projectContextService = projectContextService;
    }

    public bool IsLoading { get; private set; }
    public ProjectDto? Project { get; private set; }
    public ProjectMembershipDto? Membership { get; private set; }
    public List<ProjectMemberDto> Members { get; private set; } = new();
    public List<ProjectAppDto> Apps { get; private set; } = new();
    public List<ProjectFlowDto> Flows { get; private set; } = new();
    public List<ProjectRunnerDto> Runners { get; private set; } = new();
    public List<ProjectCustomStepDto> CustomSteps { get; private set; } = new();

    public bool IsProjectAdmin => Membership?.Role == ProjectRole.ProjectAdmin;
    public bool IsResourceManager => Membership?.Role == ProjectRole.ProjectAdmin || Membership?.Role == ProjectRole.Developer;

    // Expose project context information for UI
    public ProjectRole? CurrentRole => _projectContextService?.CurrentProject?.CurrentUserRole ?? Membership?.Role;

    public bool CanManageResources => CurrentRole == ProjectRole.ProjectAdmin || CurrentRole == ProjectRole.Developer || IsResourceManager;

    public async Task InitializeAsync(Guid projectId)
    {
        IsLoading = true;
        
        try
        {
            // Load project details and membership in parallel
            var projectTask = _projectsService.GetProjectAsync(projectId);
            var membershipTask = _projectsService.GetMembershipAsync(projectId);
            
            await Task.WhenAll(projectTask, membershipTask);
            
            Project = await projectTask;
            Membership = await membershipTask;
            
            // Load other data
            await LoadMembersAsync(projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading project {ProjectId}", projectId);
            _messageService.Error($"Error loading project: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadMembersAsync(Guid projectId)
    {
        try
        {
            var members = await _membersService.ListMembersAsync(projectId);
            Members = members?.ToList() ?? new List<ProjectMemberDto>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading members for project {ProjectId}", projectId);
            _messageService.Error($"Error loading members: {ex.Message}");
        }
    }

    public async Task LoadAppsAsync(Guid projectId)
    {
        try
        {
            var apps = await _resourcesService.ListAppsAsync(projectId);
            Apps = apps?.ToList() ?? new List<ProjectAppDto>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading apps for project {ProjectId}", projectId);
            _messageService.Error($"Error loading apps: {ex.Message}");
        }
    }

    public async Task LoadFlowsAsync(Guid projectId)
    {
        try
        {
            var flows = await _resourcesService.ListFlowsAsync(projectId);
            Flows = flows?.ToList() ?? new List<ProjectFlowDto>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading flows for project {ProjectId}", projectId);
            _messageService.Error($"Error loading flows: {ex.Message}");
        }
    }

    public async Task LoadRunnersAsync(Guid projectId)
    {
        try
        {
            var runners = await _resourcesService.ListRunnersAsync(projectId);
            Runners = runners?.ToList() ?? new List<ProjectRunnerDto>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading runners for project {ProjectId}", projectId);
            _messageService.Error($"Error loading runners: {ex.Message}");
        }
    }

    public async Task LoadCustomStepsAsync(Guid projectId)
    {
        try
        {
            var steps = await _resourcesService.ListCustomStepsAsync(projectId);
            CustomSteps = steps?.ToList() ?? new List<ProjectCustomStepDto>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading custom steps for project {ProjectId}", projectId);
            _messageService.Error($"Error loading custom steps: {ex.Message}");
        }
    }

    public async Task AddMemberAsync(Guid projectId, AddProjectMemberRequest request)
    {
        try
        {
            await _membersService.AddMemberAsync(projectId, request);
            _messageService.Success("Member added successfully");
            await LoadMembersAsync(projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding member to project {ProjectId}", projectId);
            _messageService.Error($"Error adding member: {ex.Message}");
        }
    }

    public async Task UpdateMemberRoleAsync(Guid projectId, string userId, ProjectRole newRole)
    {
        try
        {
            var request = new UpdateProjectMemberRequest { Role = newRole };
            await _membersService.UpdateMemberRoleAsync(projectId, userId, request);
            _messageService.Success("Member role updated successfully");
            await LoadMembersAsync(projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating member role in project {ProjectId}", projectId);
            _messageService.Error($"Error updating member role: {ex.Message}");
        }
    }

    public async Task RemoveMemberAsync(Guid projectId, string userId)
    {
        try
        {
            await _membersService.RemoveMemberAsync(projectId, userId);
            _messageService.Success("Member removed successfully");
            await LoadMembersAsync(projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error removing member from project {ProjectId}", projectId);
            _messageService.Error($"Error removing member: {ex.Message}");
        }
    }

    public async Task AddAppAsync(Guid projectId, Guid appId)
    {
        try
        {
            await _resourcesService.AddAppAsync(projectId, appId);
            _messageService.Success("App added to project successfully");
            await LoadAppsAsync(projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding app {AppId} to project {ProjectId}", appId, projectId);
            _messageService.Error($"Error adding app: {ex.Message}");
        }
    }

    public async Task RemoveAppAsync(Guid projectId, Guid appId)
    {
        try
        {
            await _resourcesService.RemoveAppAsync(projectId, appId);
            _messageService.Success("App removed from project successfully");
            await LoadAppsAsync(projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error removing app {AppId} from project {ProjectId}", appId, projectId);
            _messageService.Error($"Error removing app: {ex.Message}");
        }
    }

    public async Task AddFlowAsync(Guid projectId, Guid flowId)
    {
        try
        {
            await _resourcesService.AddFlowAsync(projectId, flowId);
            _messageService.Success("Flow added to project successfully");
            await LoadFlowsAsync(projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding flow {FlowId} to project {ProjectId}", flowId, projectId);
            _messageService.Error($"Error adding flow: {ex.Message}");
        }
    }

    public async Task RemoveFlowAsync(Guid projectId, Guid flowId)
    {
        try
        {
            await _resourcesService.RemoveFlowAsync(projectId, flowId);
            _messageService.Success("Flow removed from project successfully");
            await LoadFlowsAsync(projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error removing flow {FlowId} from project {ProjectId}", flowId, projectId);
            _messageService.Error($"Error removing flow: {ex.Message}");
        }
    }

    public async Task AddRunnerAsync(Guid projectId, Guid runnerId)
    {
        try
        {
            await _resourcesService.AddRunnerAsync(projectId, runnerId);
            _messageService.Success("Runner added to project successfully");
            await LoadRunnersAsync(projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding runner {RunnerId} to project {ProjectId}", runnerId, projectId);
            _messageService.Error($"Error adding runner: {ex.Message}");
        }
    }

    public async Task RemoveRunnerAsync(Guid projectId, Guid runnerId)
    {
        try
        {
            await _resourcesService.RemoveRunnerAsync(projectId, runnerId);
            _messageService.Success("Runner removed from project successfully");
            await LoadRunnersAsync(projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error removing runner {RunnerId} from project {ProjectId}", runnerId, projectId);
            _messageService.Error($"Error removing runner: {ex.Message}");
        }
    }

    public async Task AddCustomStepAsync(Guid projectId, Guid stepId)
    {
        try
        {
            await _resourcesService.AddCustomStepAsync(projectId, stepId);
            _messageService.Success("Custom step added to project successfully");
            await LoadCustomStepsAsync(projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding custom step {StepId} to project {ProjectId}", stepId, projectId);
            _messageService.Error($"Error adding custom step: {ex.Message}");
        }
    }

    public async Task RemoveCustomStepAsync(Guid projectId, Guid stepId)
    {
        try
        {
            await _resourcesService.RemoveCustomStepAsync(projectId, stepId);
            _messageService.Success("Custom step removed from project successfully");
            await LoadCustomStepsAsync(projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error removing custom step {StepId} from project {ProjectId}", stepId, projectId);
            _messageService.Error($"Error removing custom step: {ex.Message}");
        }
    }

    public async Task UpdateProjectAsync(Guid projectId, UpdateProjectRequest request)
    {
        try
        {
            await _projectsService.UpdateProjectAsync(projectId, request);
            _messageService.Success("Project updated successfully");
            
            // Reload project
            Project = await _projectsService.GetProjectAsync(projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating project {ProjectId}", projectId);
            _messageService.Error($"Error updating project: {ex.Message}");
        }
    }
}
