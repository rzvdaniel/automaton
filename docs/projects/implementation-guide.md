# Project/Organization Implementation Guide

## Overview

This guide provides detailed implementation instructions for developers working on the Project/Organization feature. It complements the main [Technical Approach Document](./project-organization-tad.md).

---

## Directory Structure

### Server-Side (Backend)

```
src/Automaton.Studio.Server/
├── Entities/
│   ├── Project.cs                          [NEW]
│   ├── ProjectMember.cs                    [NEW]
│   ├── ProjectApp.cs                       [NEW]
│   ├── ProjectFlow.cs                      [NEW]
│   ├── ProjectRunner.cs                    [NEW]
│   ├── ProjectCustomStep.cs                [NEW]
│   ├── ApplicationUser.cs                  [MODIFY]
│   ├── WebApp.cs                          [MODIFY]
│   └── Flow.cs                            [MODIFY]
│
├── Enums/
│   ├── ProjectRole.cs                      [NEW]
│   └── AccessScope.cs                      [NEW]
│
├── Models/
│   ├── ProjectModels.cs                    [NEW]
│   ├── ProjectMemberModels.cs              [NEW]
│   └── ProjectResourceModels.cs            [NEW]
│
├── Controllers/
│   ├── ProjectsController.cs               [NEW]
│   ├── ProjectMembersController.cs         [NEW]
│   ├── ProjectResourcesController.cs       [NEW]
│   ├── AppsController.cs                   [MODIFY]
│   └── FlowsController.cs                  [MODIFY]
│
├── Application/
│   ├── Commands/
│   │   └── Projects/
│   │       ├── CreateProjectCommand.cs     [NEW]
│   │       ├── AddMemberCommand.cs         [NEW]
│   │       └── LinkResourceCommand.cs      [NEW]
│   │
│   ├── Queries/
│   │   └── Projects/
│   │       ├── GetProjectsQuery.cs         [NEW]
│   │       ├── GetProjectMembersQuery.cs   [NEW]
│   │       └── GetProjectResourcesQuery.cs [NEW]
│   │
│   └── Services/
│       ├── ProjectService.cs               [NEW]
│       ├── ProjectAuthorizationService.cs  [NEW]
│       └── ProjectInvitationService.cs     [NEW]
│
├── Authorization/
│   ├── ProjectPolicyRequirements.cs        [NEW]
│   └── ProjectAuthorizationHandler.cs      [NEW]
│
├── Data/
│   └── ApplicationDbContext.cs             [MODIFY]
│
└── Migrations/
    └── YYYYMMDDHHMMSS_AddProjectEntities.cs [NEW]
```

### Client-Side (UI)

```
src/Automaton.Studio/
├── Pages/
│   ├── Projects/
│   │   ├── ProjectsPage.razor              [NEW]
│   │   ├── ProjectsPage.razor.cs           [NEW]
│   │   ├── ProjectSettingsPage.razor       [NEW]
│   │   ├── ProjectSettingsPage.razor.cs    [NEW]
│   │   ├── AcceptInvitationPage.razor      [NEW]
│   │   └── Components/
│   │       ├── ProjectCard.razor           [NEW]
│   │       ├── MembersPanel.razor          [NEW]
│   │       ├── AddMemberDialog.razor       [NEW]
│   │       └── ProjectSwitcher.razor       [NEW]
│   │
│   ├── Apps/
│   │   ├── AppsPage.razor                  [MODIFY]
│   │   └── AppDesignerPage.razor           [MODIFY]
│   │
│   ├── DashboardPage.razor                 [MODIFY]
│   └── Settings/
│       └── EnterpriseSettingsPage.razor    [NEW]
│
├── Shared/
│   ├── MainMenu.razor                      [MODIFY]
│   └── MainLayout.razor                    [MODIFY]
│
└── Services/
    ├── ProjectService.cs                   [NEW]
    └── ProjectContextService.cs            [NEW]
```

---

## Step-by-Step Implementation

### Phase 1: Database Schema

#### Step 1.1: Create Entity Classes

**File:** `src/Automaton.Studio.Server/Entities/Project.cs`

```csharp
#nullable disable

namespace Automaton.Studio.Server.Entities
{
    public class Project
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        
        public Guid OwnerId { get; set; }
        public virtual ApplicationUser Owner { get; set; }
        
        public string LogoUrl { get; set; }
        public string Theme { get; set; }
        
        public string SubscriptionTier { get; set; }
        public DateTime? SubscriptionExpiresAt { get; set; }
        
        public DateTime Created { get; set; }
        public DateTime Updated { get; set; }
        public bool IsActive { get; set; } = true;
        
        public virtual ICollection<ProjectMember> Members { get; set; }
        public virtual ICollection<ProjectApp> ProjectApps { get; set; }
        public virtual ICollection<ProjectFlow> ProjectFlows { get; set; }
        public virtual ICollection<ProjectRunner> ProjectRunners { get; set; }
        public virtual ICollection<ProjectCustomStep> ProjectCustomSteps { get; set; }
    }
}
```

**File:** `src/Automaton.Studio.Server/Entities/ProjectMember.cs`

```csharp
#nullable disable

namespace Automaton.Studio.Server.Entities
{
    public class ProjectMember
    {
        public Guid ProjectId { get; set; }
        public virtual Project Project { get; set; }
        
        public Guid UserId { get; set; }
        public virtual ApplicationUser User { get; set; }
        
        public string Role { get; set; }
        
        public DateTime JoinedAt { get; set; }
        public Guid? InvitedBy { get; set; }
        public virtual ApplicationUser Inviter { get; set; }
        
        public bool IsActive { get; set; } = true;
    }
}
```

**File:** `src/Automaton.Studio.Server/Entities/ProjectApp.cs`

```csharp
#nullable disable

namespace Automaton.Studio.Server.Entities
{
    public class ProjectApp
    {
        public Guid ProjectId { get; set; }
        public virtual Project Project { get; set; }
        
        public Guid WebAppId { get; set; }
        public virtual WebApp WebApp { get; set; }
        
        public DateTime AddedAt { get; set; }
        public Guid AddedBy { get; set; }
    }
}
```

Create similar classes for `ProjectFlow`, `ProjectRunner`, `ProjectCustomStep`.

#### Step 1.2: Create Enums

**File:** `src/Automaton.Studio.Server/Enums/ProjectRole.cs`

```csharp
namespace Automaton.Studio.Server.Enums;

public static class ProjectRole
{
    public const string ProjectAdmin = "ProjectAdmin";
    public const string Developer = "Developer";
    public const string AppUser = "AppUser";
    public const string Viewer = "Viewer";
    
    public const string Admins = ProjectAdmin;
    public const string Creators = $"{ProjectAdmin},{Developer}";
    public const string AllMembers = $"{ProjectAdmin},{Developer},{AppUser},{Viewer}";
    
    public static readonly string[] AllRoles = 
    {
        ProjectAdmin,
        Developer,
        AppUser,
        Viewer
    };
    
    public static bool IsValid(string role)
    {
        return AllRoles.Contains(role);
    }
}
```

**File:** `src/Automaton.Studio.Server/Enums/AccessScope.cs`

```csharp
namespace Automaton.Studio.Server.Enums;

public enum AccessScope
{
    Personal = 0,
    Project = 1,
    Public = 2
}
```

#### Step 1.3: Update ApplicationDbContext

**File:** `src/Automaton.Studio.Server/Data/ApplicationDbContext.cs`

Add DbSets:

```csharp
public virtual DbSet<Project> Projects { get; set; }
public virtual DbSet<ProjectMember> ProjectMembers { get; set; }
public virtual DbSet<ProjectApp> ProjectApps { get; set; }
public virtual DbSet<ProjectFlow> ProjectFlows { get; set; }
public virtual DbSet<ProjectRunner> ProjectRunners { get; set; }
public virtual DbSet<ProjectCustomStep> ProjectCustomSteps { get; set; }
```

Add to `OnModelCreating`:

```csharp
modelBuilder.Entity<Project>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.HasIndex(e => e.Name).IsUnique();
    entity.HasIndex(e => e.OwnerId);
    
    entity.HasOne(e => e.Owner)
        .WithMany(u => u.OwnedProjects)
        .HasForeignKey(e => e.OwnerId)
        .OnDelete(DeleteBehavior.Restrict);
});

modelBuilder.Entity<ProjectMember>(entity =>
{
    entity.HasKey(e => new { e.ProjectId, e.UserId });
    entity.HasIndex(e => e.UserId);
    entity.HasIndex(e => e.Role);
    
    entity.HasOne(e => e.Project)
        .WithMany(p => p.Members)
        .HasForeignKey(e => e.ProjectId)
        .OnDelete(DeleteBehavior.Cascade);
    
    entity.HasOne(e => e.User)
        .WithMany(u => u.ProjectMemberships)
        .HasForeignKey(e => e.UserId)
        .OnDelete(DeleteBehavior.Restrict);
});

modelBuilder.Entity<ProjectApp>(entity =>
{
    entity.HasKey(e => new { e.ProjectId, e.WebAppId });
    entity.HasIndex(e => e.WebAppId);
    
    entity.HasOne(e => e.Project)
        .WithMany(p => p.ProjectApps)
        .HasForeignKey(e => e.ProjectId)
        .OnDelete(DeleteBehavior.Cascade);
    
    entity.HasOne(e => e.WebApp)
        .WithMany(a => a.ProjectApps)
        .HasForeignKey(e => e.WebAppId)
        .OnDelete(DeleteBehavior.Cascade);
});

// Similar for ProjectFlow, ProjectRunner, ProjectCustomStep
```

#### Step 1.4: Modify Existing Entities

**File:** `src/Automaton.Studio.Server/Entities/ApplicationUser.cs`

```csharp
public class ApplicationUser : IdentityUser<Guid>
{
    public string Name { get; set; }
    public ICollection<ApplicationUserRole> UserRoles { get; set; }
    
    // NEW: Enterprise feature flag
    public bool IsEnterpriseEnabled { get; set; } = false;
    
    // NEW: Current project context
    public Guid? CurrentProjectId { get; set; }
    
    // NEW: Project ownership
    public virtual ICollection<Project> OwnedProjects { get; set; }
    
    // NEW: Project memberships
    public virtual ICollection<ProjectMember> ProjectMemberships { get; set; }
    
    // EXISTING: Keep for backward compatibility
    public ICollection<FlowUser> FlowUsers { get; set; }
    public ICollection<WebAppUser> WebAppUsers { get; set; }
    // ... other collections
}
```

**File:** `src/Automaton.Studio.Server/Entities/WebApp.cs`

```csharp
public class WebApp
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string Definition { get; set; }
    
    public bool IsPublished { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Version { get; set; } = 1;
    public Guid? ExecutionRunnerId { get; set; }
    
    public bool RequiresAuthentication { get; set; } = false;
    public string? AllowedRoles { get; set; }
    
    // NEW: Project-level access control
    public AccessScope Scope { get; set; } = AccessScope.Personal;
    public Guid? ProjectId { get; set; }
    
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    
    // EXISTING: Personal access
    public virtual ICollection<WebAppUser> WebAppUsers { get; set; }
    
    // NEW: Project links
    public virtual ICollection<ProjectApp> ProjectApps { get; set; }
}
```

#### Step 1.5: Generate Migration

```bash
cd src/Automaton.Studio.Server
dotnet ef migrations add AddProjectEntities
```

Review the generated migration file. Ensure:
- Tables are created in correct order (no FK constraint issues)
- Indexes are added
- Default values are set

Apply migration to local database:

```bash
dotnet ef database update
```

---

### Phase 2: Backend Services

#### Step 2.1: Create Project Service

**File:** `src/Automaton.Studio.Server/Application/Services/ProjectService.cs`

```csharp
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Application.Services;

public class ProjectService
{
    private readonly IDataContext _context;
    
    public ProjectService(IDataContext context)
    {
        _context = context;
    }
    
    public async Task<Project> CreateProjectAsync(Guid ownerId, string name, string displayName, string? description)
    {
        // Validate name uniqueness
        if (await _context.Projects.AnyAsync(p => p.Name == name))
        {
            throw new InvalidOperationException($"Project with name '{name}' already exists.");
        }
        
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            DisplayName = displayName,
            Description = description,
            OwnerId = ownerId,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            IsActive = true
        };
        
        _context.Projects.Add(project);
        
        // Add owner as ProjectAdmin
        var member = new ProjectMember
        {
            ProjectId = project.Id,
            UserId = ownerId,
            Role = ProjectRole.ProjectAdmin,
            JoinedAt = DateTime.UtcNow,
            IsActive = true
        };
        
        _context.ProjectMembers.Add(member);
        
        await _context.SaveChangesAsync();
        
        return project;
    }
    
    public async Task<List<Project>> GetUserProjectsAsync(Guid userId)
    {
        return await _context.ProjectMembers
            .Where(pm => pm.UserId == userId && pm.IsActive)
            .Select(pm => pm.Project)
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.Updated)
            .ToListAsync();
    }
    
    public async Task<ProjectMember?> GetMemberAsync(Guid projectId, Guid userId)
    {
        return await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => 
                pm.ProjectId == projectId && 
                pm.UserId == userId && 
                pm.IsActive);
    }
    
    public async Task<ProjectMember> AddMemberAsync(Guid projectId, Guid userId, string role, Guid invitedBy)
    {
        if (!ProjectRole.IsValid(role))
        {
            throw new ArgumentException($"Invalid role: {role}");
        }
        
        // Check if already member
        var existing = await GetMemberAsync(projectId, userId);
        if (existing != null)
        {
            throw new InvalidOperationException("User is already a member of this project.");
        }
        
        var member = new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTime.UtcNow,
            InvitedBy = invitedBy,
            IsActive = true
        };
        
        _context.ProjectMembers.Add(member);
        await _context.SaveChangesAsync();
        
        return member;
    }
    
    public async Task<bool> UpdateMemberRoleAsync(Guid projectId, Guid userId, string newRole)
    {
        var member = await GetMemberAsync(projectId, userId);
        if (member == null) return false;
        
        if (!ProjectRole.IsValid(newRole))
        {
            throw new ArgumentException($"Invalid role: {role}");
        }
        
        member.Role = newRole;
        await _context.SaveChangesAsync();
        
        return true;
    }
    
    public async Task<bool> RemoveMemberAsync(Guid projectId, Guid userId)
    {
        var member = await GetMemberAsync(projectId, userId);
        if (member == null) return false;
        
        // Prevent removing last admin
        var adminCount = await _context.ProjectMembers
            .CountAsync(pm => 
                pm.ProjectId == projectId && 
                pm.Role == ProjectRole.ProjectAdmin && 
                pm.IsActive);
        
        if (member.Role == ProjectRole.ProjectAdmin && adminCount == 1)
        {
            throw new InvalidOperationException("Cannot remove the last ProjectAdmin.");
        }
        
        member.IsActive = false;
        await _context.SaveChangesAsync();
        
        return true;
    }
}
```

#### Step 2.2: Create Authorization Service

**File:** `src/Automaton.Studio.Server/Application/Services/ProjectAuthorizationService.cs`

```csharp
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Application.Services;

public class ProjectAuthorizationService
{
    private readonly IDataContext _context;
    
    public ProjectAuthorizationService(IDataContext context)
    {
        _context = context;
    }
    
    public async Task<bool> CanAccessProject(Guid userId, Guid projectId)
    {
        return await _context.ProjectMembers
            .AnyAsync(pm => 
                pm.ProjectId == projectId && 
                pm.UserId == userId && 
                pm.IsActive);
    }
    
    public async Task<bool> HasRole(Guid userId, Guid projectId, params string[] allowedRoles)
    {
        var member = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => 
                pm.ProjectId == projectId && 
                pm.UserId == userId && 
                pm.IsActive);
        
        return member != null && allowedRoles.Contains(member.Role);
    }
    
    public async Task<bool> IsProjectAdmin(Guid userId, Guid projectId)
    {
        return await HasRole(userId, projectId, ProjectRole.ProjectAdmin);
    }
    
    public async Task<bool> CanCreateResources(Guid userId, Guid projectId)
    {
        return await HasRole(userId, projectId, ProjectRole.ProjectAdmin, ProjectRole.Developer);
    }
    
    public async Task<bool> CanAccessApp(Guid userId, Guid appId)
    {
        var app = await _context.WebApps
            .Include(a => a.ProjectApps)
            .FirstOrDefaultAsync(a => a.Id == appId);
        
        if (app == null) return false;
        
        // Personal apps: check WebAppUsers
        if (app.Scope == AccessScope.Personal)
        {
            return await _context.WebAppUsers
                .AnyAsync(wu => wu.WebAppId == appId && wu.UserId == userId);
        }
        
        // Public apps: everyone
        if (app.Scope == AccessScope.Public)
        {
            return true;
        }
        
        // Project apps: check project membership + role
        if (app.Scope == AccessScope.Project && app.ProjectId.HasValue)
        {
            var member = await _context.ProjectMembers
                .FirstOrDefaultAsync(pm => 
                    pm.ProjectId == app.ProjectId.Value && 
                    pm.UserId == userId && 
                    pm.IsActive);
            
            if (member == null) return false;
            
            // Check if role allows app access
            return member.Role switch
            {
                ProjectRole.ProjectAdmin => true,
                ProjectRole.Developer => true,
                ProjectRole.AppUser => true,
                _ => false
            };
        }
        
        return false;
    }
}
```

#### Step 2.3: Register Services

**File:** `src/Automaton.Studio.Server/Program.cs`

```csharp
// Add these registrations
services.AddScoped<ProjectService>();
services.AddScoped<ProjectAuthorizationService>();
```

---

### Phase 3: API Controllers

#### Step 3.1: Create Projects Controller

**File:** `src/Automaton.Studio.Server/Controllers/ProjectsController.cs`

```csharp
using Automaton.Studio.Server.Application.Services;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProjectsController : BaseController
{
    private readonly ProjectService _projectService;
    private readonly ProjectAuthorizationService _authService;
    
    public ProjectsController(
        ProjectService projectService,
        ProjectAuthorizationService authService)
    {
        _projectService = projectService;
        _authService = authService;
    }
    
    [HttpGet]
    public async Task<IActionResult> GetProjects()
    {
        var userId = UserId;
        var projects = await _projectService.GetUserProjectsAsync(userId);
        
        var projectDtos = projects.Select(p => new ProjectDto
        {
            Id = p.Id,
            Name = p.Name,
            DisplayName = p.DisplayName,
            Description = p.Description,
            Created = p.Created,
            Updated = p.Updated,
            MemberCount = p.Members?.Count ?? 0
        });
        
        return Ok(projectDtos);
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetProject(Guid id)
    {
        if (!await _authService.CanAccessProject(UserId, id))
        {
            return Forbid();
        }
        
        var project = await _projectService.GetProjectDetailAsync(id);
        if (project == null) return NotFound();
        
        return Ok(project);
    }
    
    [HttpPost]
    public async Task<IActionResult> CreateProject([FromBody] CreateProjectRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        
        var project = await _projectService.CreateProjectAsync(
            UserId,
            request.Name,
            request.DisplayName,
            request.Description);
        
        return CreatedAtAction(nameof(GetProject), new { id = project.Id }, project);
    }
    
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProject(Guid id, [FromBody] UpdateProjectRequest request)
    {
        if (!await _authService.IsProjectAdmin(UserId, id))
        {
            return Forbid();
        }
        
        var updated = await _projectService.UpdateProjectAsync(id, request);
        return Ok(updated);
    }
    
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProject(Guid id)
    {
        if (!await _authService.IsProjectAdmin(UserId, id))
        {
            return Forbid();
        }
        
        await _projectService.DeleteProjectAsync(id);
        return NoContent();
    }
}
```

#### Step 3.2: Create Models

**File:** `src/Automaton.Studio.Server/Models/ProjectModels.cs`

```csharp
namespace Automaton.Studio.Server.Models;

public class ProjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string? Description { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public int MemberCount { get; set; }
}

public class CreateProjectRequest
{
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string? Description { get; set; }
}

public class UpdateProjectRequest
{
    public string DisplayName { get; set; }
    public string? Description { get; set; }
}
```

---

### Phase 4: Client-Side UI

#### Step 4.1: Create Project Service (Client)

**File:** `src/Automaton.Studio/Services/ProjectService.cs`

```csharp
using Automaton.Studio.Models;
using System.Net.Http.Json;

namespace Automaton.Studio.Services;

public class ProjectService
{
    private readonly HttpClient _httpClient;
    
    public ProjectService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }
    
    public async Task<List<ProjectDto>> GetProjectsAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<ProjectDto>>("api/projects") 
               ?? new List<ProjectDto>();
    }
    
    public async Task<ProjectDto?> GetProjectAsync(Guid id)
    {
        return await _httpClient.GetFromJsonAsync<ProjectDto>($"api/projects/{id}");
    }
    
    public async Task<ProjectDto> CreateProjectAsync(CreateProjectRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/projects", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProjectDto>();
    }
    
    public async Task UpdateProjectAsync(Guid id, UpdateProjectRequest request)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/projects/{id}", request);
        response.EnsureSuccessStatusCode();
    }
    
    public async Task DeleteProjectAsync(Guid id)
    {
        var response = await _httpClient.DeleteAsync($"api/projects/{id}");
        response.EnsureSuccessStatusCode();
    }
}
```

Register in `Program.cs`:

```csharp
builder.Services.AddScoped<ProjectService>();
```

#### Step 4.2: Create Projects Page

**File:** `src/Automaton.Studio/Pages/Projects/ProjectsPage.razor`

```razor
@page "/projects"
@using Automaton.Studio.Services
@using Automaton.Studio.Models
@inject ProjectService ProjectService
@inject NavigationManager Navigation

<PageTitle>Projects</PageTitle>

<div class="projects-page">
    <div class="page-header">
        <h1>Projects</h1>
        <Button Type="primary" Icon="plus" OnClick="ShowCreateDialog">
            Create Project
        </Button>
    </div>
    
    @if (isLoading)
    {
        <Spin />
    }
    else if (projects.Count == 0)
    {
        <Empty Description="No projects yet. Create your first project to get started." />
    }
    else
    {
        <div class="project-grid">
            @foreach (var project in projects)
            {
                <Card Class="project-card" 
                      Hoverable 
                      OnClick="@(() => OpenProject(project.Id))">
                    <h3>@project.DisplayName</h3>
                    <p>@project.Description</p>
                    <div class="project-meta">
                        <Tag>@project.MemberCount Members</Tag>
                        <Text Type="secondary">Updated @project.Updated.ToRelativeTimeString()</Text>
                    </div>
                </Card>
            }
        </div>
    }
</div>

<Modal Title="Create Project"
       @bind-Visible="showCreateDialog"
       OnOk="CreateProject"
       OnCancel="() => showCreateDialog = false">
    <Form Model="newProject">
        <FormItem Label="Name">
            <Input @bind-Value="newProject.Name" Placeholder="project-name" />
        </FormItem>
        <FormItem Label="Display Name">
            <Input @bind-Value="newProject.DisplayName" Placeholder="My Project" />
        </FormItem>
        <FormItem Label="Description">
            <TextArea @bind-Value="newProject.Description" Rows="3" />
        </FormItem>
    </Form>
</Modal>

@code {
    private List<ProjectDto> projects = new();
    private bool isLoading = true;
    private bool showCreateDialog = false;
    private CreateProjectRequest newProject = new();
    
    protected override async Task OnInitializedAsync()
    {
        await LoadProjects();
    }
    
    private async Task LoadProjects()
    {
        isLoading = true;
        try
        {
            projects = await ProjectService.GetProjectsAsync();
        }
        finally
        {
            isLoading = false;
        }
    }
    
    private void ShowCreateDialog()
    {
        newProject = new CreateProjectRequest();
        showCreateDialog = true;
    }
    
    private async Task CreateProject()
    {
        await ProjectService.CreateProjectAsync(newProject);
        showCreateDialog = false;
        await LoadProjects();
    }
    
    private void OpenProject(Guid projectId)
    {
        Navigation.NavigateTo($"/projects/{projectId}/settings");
    }
}
```

**File:** `src/Automaton.Studio/Pages/Projects/ProjectsPage.razor.css`

```css
.projects-page {
    padding: 24px;
}

.page-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 24px;
}

.project-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
    gap: 16px;
}

.project-card {
    cursor: pointer;
    transition: all 0.3s ease;
}

.project-card:hover {
    transform: translateY(-4px);
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

.project-meta {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-top: 12px;
}
```

#### Step 4.3: Update Main Menu

**File:** `src/Automaton.Studio/Shared/MainMenu.razor`

Add after Home menu item:

```razor
@if (isEnterpriseEnabled)
{
    <MenuItem Key="1b" Icon="apartment" RouterLink="/projects">
        <b>Projects</b>
    </MenuItem>
}
```

In `@code` section:

```csharp
private bool isEnterpriseEnabled = false;

protected override async Task OnInitializedAsync()
{
    await base.OnInitializedAsync();
    await CheckEnterpriseStatus();
}

private async Task CheckEnterpriseStatus()
{
    try
    {
        // Call API to check if user has enterprise enabled
        isEnterpriseEnabled = await UserService.IsEnterpriseEnabledAsync();
        StateHasChanged();
    }
    catch
    {
        isEnterpriseEnabled = false;
    }
}
```

---

## Testing Checklist

### Unit Tests
- [ ] ProjectService.CreateProjectAsync creates project + owner membership
- [ ] ProjectService.AddMemberAsync prevents duplicate memberships
- [ ] ProjectService.RemoveMemberAsync prevents removing last admin
- [ ] ProjectAuthorizationService.CanAccessApp checks project membership
- [ ] ProjectAuthorizationService.HasRole validates roles correctly

### Integration Tests
- [ ] POST /api/projects creates project
- [ ] GET /api/projects returns user's projects
- [ ] POST /api/projects/{id}/members adds member
- [ ] PUT /api/projects/{id}/members/{userId} updates role
- [ ] DELETE /api/projects/{id}/members/{userId} removes member
- [ ] Non-member cannot access project resources (403)

### E2E Tests
- [ ] User can create project
- [ ] User can invite member
- [ ] Invited user receives email
- [ ] Invited user can accept invitation
- [ ] Project admin can change member role
- [ ] AppUser sees only app cards on dashboard
- [ ] Developer sees full tools + project apps

---

## Common Pitfalls & Solutions

### 1. Circular Reference in Navigation Properties
**Problem:** EF Core throws "A possible object cycle was detected"

**Solution:** Use `[JsonIgnore]` or configure JSON serializer:

```csharp
// In Program.cs
services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});
```

### 2. N+1 Query Problem
**Problem:** Loading projects with members causes multiple DB queries

**Solution:** Use `.Include()`:

```csharp
var projects = await _context.Projects
    .Include(p => p.Members)
        .ThenInclude(m => m.User)
    .Where(p => p.Members.Any(m => m.UserId == userId))
    .ToListAsync();
```

### 3. Authorization Not Working
**Problem:** User can access resources they shouldn't

**Solution:** Always validate in BOTH controller AND service:

```csharp
// Controller
[HttpGet("api/projects/{id}/apps")]
public async Task<IActionResult> GetProjectApps(Guid id)
{
    if (!await _authService.CanAccessProject(UserId, id))
        return Forbid();
    
    // Service layer also validates
    var apps = await _projectService.GetProjectAppsAsync(id, UserId);
    return Ok(apps);
}
```

### 4. Migration Fails on Existing Data
**Problem:** Adding `ProjectId` column to WebApps fails if data exists

**Solution:** Make column nullable, migrate data, then enforce:

```csharp
// Migration Up()
migrationBuilder.AddColumn<Guid?>(
    name: "ProjectId",
    table: "WebApps",
    nullable: true);

// Run data migration script separately

// Later migration to enforce NOT NULL (if needed)
```

---

## Performance Optimization

### 1. Index Strategy
Critical indexes:
- `ProjectMembers (UserId, IsActive)`
- `ProjectMembers (ProjectId, Role)`
- `ProjectApps (WebAppId)`
- `WebApps (Scope, ProjectId)`

### 2. Caching
Cache user's project list:

```csharp
private readonly IMemoryCache _cache;

public async Task<List<Project>> GetUserProjectsAsync(Guid userId)
{
    var cacheKey = $"user_projects_{userId}";
    
    if (_cache.TryGetValue(cacheKey, out List<Project> projects))
    {
        return projects;
    }
    
    projects = await _context.ProjectMembers
        .Where(pm => pm.UserId == userId && pm.IsActive)
        .Select(pm => pm.Project)
        .ToListAsync();
    
    _cache.Set(cacheKey, projects, TimeSpan.FromMinutes(5));
    
    return projects;
}
```

Invalidate on changes:

```csharp
public async Task AddMemberAsync(...)
{
    // ...
    await _context.SaveChangesAsync();
    
    // Invalidate cache
    _cache.Remove($"user_projects_{userId}");
}
```

### 3. Pagination
For large projects:

```csharp
public async Task<PagedResult<ProjectMemberDto>> GetMembersAsync(
    Guid projectId,
    int page = 1,
    int pageSize = 20)
{
    var query = _context.ProjectMembers
        .Where(pm => pm.ProjectId == projectId && pm.IsActive);
    
    var total = await query.CountAsync();
    
    var members = await query
        .OrderBy(pm => pm.JoinedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();
    
    return new PagedResult<ProjectMemberDto>
    {
        Items = members.Select(m => new ProjectMemberDto { ... }),
        TotalCount = total,
        Page = page,
        PageSize = pageSize
    };
}
```

---

## Security Best Practices

### 1. Always Validate Project Membership
```csharp
public async Task<IActionResult> ModifyResource(Guid projectId, Guid resourceId)
{
    // STEP 1: Verify project access
    if (!await _authService.CanAccessProject(UserId, projectId))
        return Forbid();
    
    // STEP 2: Verify role
    if (!await _authService.CanCreateResources(UserId, projectId))
        return Forbid();
    
    // STEP 3: Verify resource belongs to project
    var resource = await GetResource(resourceId);
    if (resource.ProjectId != projectId)
        return BadRequest("Resource does not belong to this project");
    
    // Now safe to modify
    // ...
}
```

### 2. Prevent Enumeration Attacks
Don't reveal whether a project exists:

```csharp
// BAD
if (project == null) return NotFound();
if (!CanAccess(userId, projectId)) return Forbid();

// GOOD
if (project == null || !CanAccess(userId, projectId))
    return NotFound(); // Same response for both
```

### 3. Rate Limit Invitations
```csharp
// In ProjectService
private readonly IRateLimiter _rateLimiter;

public async Task SendInvitationAsync(Guid projectId, string email)
{
    var key = $"invite_{projectId}";
    if (!_rateLimiter.TryAcquire(key, 10, TimeSpan.FromHours(1)))
    {
        throw new RateLimitExceededException("Too many invitations. Try again later.");
    }
    
    // Send email
    // ...
}
```

---

## Deployment Checklist

Before deploying to production:

- [ ] Run all migrations on staging environment
- [ ] Verify existing data is not affected
- [ ] Test rollback scenario
- [ ] Update API documentation (Swagger)
- [ ] Enable feature flag for beta users only
- [ ] Set up monitoring/alerts for new endpoints
- [ ] Prepare rollback plan
- [ ] Update user documentation
- [ ] Train support team on new features

---

## Next Steps

After completing Phase 1-4:

1. **Monitor usage**: Track adoption, performance, errors
2. **Gather feedback**: Beta users provide insights
3. **Iterate**: Fix bugs, improve UX
4. **Plan Phase 5**: Advanced features (custom roles, project templates, etc.)

---

**Document Version:** 1.0  
**Last Updated:** November 9, 2024  
**Maintainer:** Automaton Development Team
