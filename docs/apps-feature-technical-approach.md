# Apps Feature - Technical Approach Document

## Overview

This document outlines the technical approach for implementing the **Apps** feature in Automaton.Studio. The Apps feature will enable users to create, edit, publish, and deploy web applications (HTML/CSS/JavaScript) that can interact with existing Flows to provide backend functionality.

## Goals

1. Allow users to create web applications using HTML/CSS/JavaScript in a Monaco editor
2. Enable AI-assisted app generation similar to Custom Steps
3. Provide preview and testing capabilities within the Studio
4. Publish apps for external access without requiring authentication
5. Enable apps to connect to existing Flows for backend functionality
6. Support Progressive Web App (PWA) capabilities for mobile deployment

## Architecture Overview

The Apps feature will follow the same architectural patterns as Custom Steps, ensuring consistency across the platform:

```
┌─────────────────────────────────────────────────────────────┐
│                    Automaton.Studio (Blazor)                 │
│  ┌──────────────┐           ┌─────────────────────────┐    │
│  │  AppsPage    │           │    AppDesignerPage      │    │
│  │  (List/CRUD) │◄─────────►│  (Editor/Preview/AI)    │    │
│  └──────────────┘           └─────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│              Automaton.Studio.Server (Backend)              │
│  ┌──────────────────┐        ┌──────────────────────┐     │
│  │  AppsController  │◄──────►│    AppsService       │     │
│  └──────────────────┘        └──────────────────────┘     │
│           │                            │                    │
│           ▼                            ▼                    │
│  ┌──────────────────┐        ┌──────────────────────┐     │
│  │ AppPublicAccess  │        │  ApplicationDbContext │     │
│  │   Controller     │        │  (App Entity/AppUser) │     │
│  └──────────────────┘        └──────────────────────┘     │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                      Database (EF Core)                      │
│  ┌──────────────────┐        ┌──────────────────────┐     │
│  │   App Entity     │        │    AppUser Entity    │     │
│  └──────────────────┘        └──────────────────────┘     │
└─────────────────────────────────────────────────────────────┘
```

## Data Model

### Database Entities

#### App Entity (`Automaton.Studio.Server/Entities/App.cs`)

```csharp
public class App
{
    public Guid Id { get; set; }
    public string Name { get; set; }                    // Unique app identifier
    public string DisplayName { get; set; }             // Friendly display name
    public string Description { get; set; }             // App description
    public string Definition { get; set; }              // JSON serialized AppDefinition
    public bool IsPublished { get; set; }               // Whether app is publicly accessible
    public string? PublicUrl { get; set; }              // Public URL slug (e.g., /apps/my-dashboard)
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    
    public virtual ICollection<AppUser> AppUsers { get; set; }
}
```

#### AppUser Entity (`Automaton.Studio.Server/Entities/AppUser.cs`)

```csharp
public class AppUser
{
    public Guid AppId { get; set; }
    public Guid UserId { get; set; }
}
```

### Models

#### AppDefinition Model (`Automaton.Studio.Server/Models/AppDefinition.cs`)

```csharp
public class AppDefinition
{
    public string? HtmlContent { get; set; }            // Combined HTML/CSS/JavaScript code
    public IList<string>? ConnectedFlowIds { get; set; } // Flows this app can invoke
    public AppSettings Settings { get; set; }           // App-specific settings
    public int Version { get; set; } = 1;               // Version tracking
}

public class AppSettings
{
    public bool EnablePwa { get; set; } = false;        // PWA capabilities
    public string? PwaName { get; set; }                // PWA app name
    public string? PwaShortName { get; set; }           // PWA short name
    public string? PwaIconUrl { get; set; }             // PWA icon URL
    public string? ThemeColor { get; set; }             // PWA theme color
    public string? BackgroundColor { get; set; }        // PWA background color
}
```

#### App Model (`Automaton.Studio.Server/Models/App.cs`)

```csharp
public class App
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public bool IsPublished { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    
    public AppDefinition Definition { get; set; } = new AppDefinition();
}
```

#### NewApp Model (`Automaton.Studio.Server/Models/NewApp.cs`)

```csharp
public class NewApp
{
    public string Name { get; set; }
}
```

## Backend Implementation

### 1. Database Migration

Create a new migration to add the `Apps` and `AppUsers` tables to the database:

```bash
cd src/Automaton.Studio.Server
dotnet ef migrations add AddAppsFeature
dotnet ef database update
```

The migration will:
- Create `Apps` table with columns matching the `App` entity
- Create `AppUsers` junction table for user-app associations
- Add appropriate indexes and foreign key constraints
- Configure the `Definition` column as JSON/text

### 2. AppsController (`Automaton.Studio.Server/Controllers/AppsController.cs`)

Following the pattern from `CustomStepsController`:

```csharp
public class AppsController : BaseController
{
    private readonly AppsService appsService;

    public AppsController(AppsService appsService)
    {
        this.appsService = appsService;
    }

    [HttpGet]
    public IEnumerable<Models.App> Get()
    {
        return appsService.List();
    }

    [HttpGet("{id}")]
    public ActionResult<Models.App> Get(Guid id)
    {
        var app = appsService.Get(id);
        
        if (app is null)
        {
            return NotFound();
        }
        
        return Ok(app);
    }

    [HttpPost]
    public async Task<IActionResult> Post(Models.NewApp app)
    {
        var appId = await appsService.Create(app);
        var newApp = appsService.Get(appId);
        
        return CreatedAtAction(nameof(Get), new { id = newApp.Id }, newApp);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(Guid id, Models.App app)
    {
        await appsService.Update(id, app);
        
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await appsService.Remove(id);
        
        return NoContent();
    }

    [HttpPost("{id}/publish")]
    public async Task<IActionResult> Publish(Guid id, [FromBody] PublishAppRequest request)
    {
        await appsService.Publish(id, request.PublicUrl);
        
        return NoContent();
    }

    [HttpPost("{id}/unpublish")]
    public async Task<IActionResult> Unpublish(Guid id)
    {
        await appsService.Unpublish(id);
        
        return NoContent();
    }
}

public class PublishAppRequest
{
    public string PublicUrl { get; set; }
}
```

### 3. AppsService (`Automaton.Studio.Server/Services/AppsService.cs`)

Following the pattern from `CustomStepsService`:

```csharp
public class AppsService
{
    private readonly ApplicationDbContext dataContext;
    private readonly RecentActivityService recentActivityService;
    private readonly Guid userId;
    private readonly ILogger logger;

    public AppsService(
        ApplicationDbContext dataContext,
        UserContextService userContextService,
        RecentActivityService recentActivityService
    )
    {
        this.dataContext = dataContext;
        this.recentActivityService = recentActivityService;
        this.userId = userContextService.GetUserId();
        this.logger = Log.ForContext<AppsService>();
    }

    public IEnumerable<App> List()
    {
        var entities = dataContext.Apps.Where(x => x.AppUsers.Any(x => x.UserId == userId));
        var apps = entities.Select(MapToModel);
        
        return apps;
    }

    public App Get(Guid id)
    {
        var appEntity = dataContext.Apps.SingleOrDefault(x => x.Id == id && x.AppUsers.Any(x => x.UserId == userId));
        var app = MapToModel(appEntity);
        
        return app;
    }

    public async Task<Guid> Create(NewApp app)
    {
        var entity = new Entities.App
        {
            Id = Guid.NewGuid(),
            Name = app.Name,
            DisplayName = string.Empty,
            Description = string.Empty,
            IsPublished = false,
            PublicUrl = null,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            Definition = JsonSerializer.Serialize(new AppDefinition())
        };

        dataContext.Apps.Add(entity);

        var appUser = new Entities.AppUser
        {
            AppId = entity.Id,
            UserId = userId
        };

        dataContext.AppUsers.Add(appUser);
        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppCreated, app.Name, entity.Id);

        return entity.Id;
    }

    public async Task Update(Guid id, App app)
    {
        var entity = dataContext.Apps.SingleOrDefault(x => x.Id == id && x.AppUsers.Any(x => x.UserId == userId));

        entity.Name = app.Name;
        entity.DisplayName = app.DisplayName;
        entity.Description = app.Description;
        entity.IsPublished = app.IsPublished;
        entity.PublicUrl = app.PublicUrl;
        entity.Updated = DateTime.UtcNow;
        entity.Definition = JsonSerializer.Serialize(app.Definition);

        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppUpdated, app.Name, id);
    }

    public async Task Remove(Guid id)
    {
        var app = dataContext.Apps.SingleOrDefault(x => x.Id == id && x.AppUsers.Any(x => x.UserId == userId));
        var appName = app.Name;

        dataContext.Apps.Remove(app);
        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppDeleted, appName, id);
    }

    public async Task Publish(Guid id, string publicUrl)
    {
        var entity = dataContext.Apps.SingleOrDefault(x => x.Id == id && x.AppUsers.Any(x => x.UserId == userId));
        
        // Validate URL slug format
        if (!IsValidUrlSlug(publicUrl))
        {
            throw new InvalidOperationException("Invalid URL slug format");
        }
        
        // Check if URL is already taken
        if (dataContext.Apps.Any(x => x.PublicUrl == publicUrl && x.Id != id))
        {
            throw new InvalidOperationException("This URL is already taken");
        }

        entity.IsPublished = true;
        entity.PublicUrl = publicUrl;
        entity.Updated = DateTime.UtcNow;
        
        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppPublished, entity.Name, id);
    }

    public async Task Unpublish(Guid id)
    {
        var entity = dataContext.Apps.SingleOrDefault(x => x.Id == id && x.AppUsers.Any(x => x.UserId == userId));

        entity.IsPublished = false;
        entity.PublicUrl = null;
        entity.Updated = DateTime.UtcNow;
        
        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppUnpublished, entity.Name, id);
    }

    private static bool IsValidUrlSlug(string slug)
    {
        // URL slug validation: lowercase letters, numbers, hyphens
        return Regex.IsMatch(slug, @"^[a-z0-9\-]+$");
    }

    private static App MapToModel(Entities.App entity)
    {
        if (entity == null) return null;

        return new App
        {
            Id = entity.Id,
            Name = entity.Name,
            DisplayName = entity.DisplayName,
            Description = entity.Description,
            IsPublished = entity.IsPublished,
            PublicUrl = entity.PublicUrl,
            Created = entity.Created,
            Updated = entity.Updated,
            Definition = JsonSerializer.Deserialize<AppDefinition>(entity.Definition ?? "{}")
        };
    }
}
```

### 4. Public Access Controller (`Automaton.Studio.Server/Controllers/AppPublicAccessController.cs`)

This controller handles public access to published apps without authentication:

```csharp
[AllowAnonymous]
[Route("apps")]
public class AppPublicAccessController : ControllerBase
{
    private readonly ApplicationDbContext dataContext;
    private readonly ILogger logger;

    public AppPublicAccessController(ApplicationDbContext dataContext)
    {
        this.dataContext = dataContext;
        this.logger = Log.ForContext<AppPublicAccessController>();
    }

    [HttpGet("{publicUrl}")]
    public ActionResult<PublicAppModel> Get(string publicUrl)
    {
        var app = dataContext.Apps.SingleOrDefault(x => x.PublicUrl == publicUrl && x.IsPublished);
        
        if (app == null)
        {
            return NotFound();
        }

        var definition = JsonSerializer.Deserialize<AppDefinition>(app.Definition ?? "{}");

        return Ok(new PublicAppModel
        {
            Id = app.Id,
            DisplayName = app.DisplayName,
            Description = app.Description,
            HtmlContent = definition?.HtmlContent,
            Settings = definition?.Settings
        });
    }
}

public class PublicAppModel
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string HtmlContent { get; set; }
    public AppSettings Settings { get; set; }
}
```

### 5. Flow Invocation API

Apps need to invoke Flows for backend functionality. Add a new endpoint to `FlowsController`:

```csharp
[HttpPost("{id}/execute-from-app")]
[AllowAnonymous]
public async Task<ActionResult<FlowExecutionResult>> ExecuteFromApp(Guid id, [FromBody] AppFlowExecutionRequest request)
{
    // Validate that the app is allowed to invoke this flow
    var app = dataContext.Apps.SingleOrDefault(x => x.Id == request.AppId && x.IsPublished);
    
    if (app == null)
    {
        return Unauthorized("App not found or not published");
    }
    
    var definition = JsonSerializer.Deserialize<AppDefinition>(app.Definition ?? "{}");
    
    if (definition?.ConnectedFlowIds?.Contains(id.ToString()) != true)
    {
        return Unauthorized("App is not authorized to execute this flow");
    }

    // Execute the flow
    // (Reuse existing flow execution logic)
    
    return Ok(result);
}

public class AppFlowExecutionRequest
{
    public Guid AppId { get; set; }
    public Dictionary<string, object> InputVariables { get; set; }
}
```

### 6. Update ApplicationDbContext

Add the new entities to `ApplicationDbContext`:

```csharp
public virtual DbSet<App> Apps { get; set; }
public virtual DbSet<AppUser> AppUsers { get; set; }

// In OnModelCreating:
modelBuilder.Entity<App>(entity =>
{
    entity.HasKey(e => new { e.Id });
    entity.HasIndex(e => e.PublicUrl).IsUnique();
});

modelBuilder.Entity<AppUser>(entity =>
{
    entity.HasKey(e => new { e.AppId, e.UserId });
    entity.HasIndex(e => e.AppId, "IX_AppUser_AppId");
    entity.Property(e => e.UserId).IsRequired();
    entity.Property(e => e.AppId).IsRequired();
});
```

### 7. Recent Activity Support

Add new activity types to the `ActivityType` enum:

```csharp
AppCreated,
AppUpdated,
AppDeleted,
AppPublished,
AppUnpublished
```

Update `RecentActivityService` to include:

```csharp
public async Task LogAppActivityAsync(ActivityType activityType, string appName, Guid appId)
{
    var description = activityType switch
    {
        ActivityType.AppCreated => $"Created app '{appName}'",
        ActivityType.AppUpdated => $"Updated app '{appName}'",
        ActivityType.AppDeleted => $"Deleted app '{appName}'",
        ActivityType.AppPublished => $"Published app '{appName}'",
        ActivityType.AppUnpublished => $"Unpublished app '{appName}'",
        _ => $"App activity for '{appName}'"
    };

    await LogActivityAsync(activityType, "App", appName, description, appId);
}
```

## Frontend Implementation

### 1. AppsPage (`Automaton.Studio/Pages/Apps/AppsPage.razor`)

Following the pattern from `CustomStepsPage.razor`:

```razor
@page "/apps"
@using AntDesign

<Layout>
    <Header Class="header">
        <Row>
            <Col Flex=@("none") Class="page-title">
                <Space Size="SpaceSize.Large">
                    <SpaceItem Class="header-item">
                        <Breadcrumb>
                            <BreadcrumbItem Href="/">
                                <Icon Type="home" Width="16" Height="16" />
                                <span>Home</span>
                            </BreadcrumbItem>
                            <BreadcrumbItem>
                                <Icon Type="appstore" Width="16" Height="16" />
                                <span>Apps</span>
                            </BreadcrumbItem>
                        </Breadcrumb>
                    </SpaceItem>
                </Space>
            </Col>

            <Col Class="right-actions">
                <Space Class="right-header-items">
                    <SpaceItem>
                        <Tooltip Title="Create new app">
                            <Button Type="@ButtonType.Primary" Icon="plus" OnClick="@(() => ShowNewAppDialog())">
                                Create App
                            </Button>
                        </Tooltip>
                    </SpaceItem>
                </Space>
            </Col>
        </Row>
    </Header>

    <Content Class="content-padding content-background">
        @if (!loading && AppsViewModel.Apps?.Any() != true)
        {
            <EnhancedEmpty Title="Web Apps in minutes." 
                          Description="Create web applications that connect to your existing Flows for backend functionality."
                          BackgroundImageUrl="..."
                          GradientOverlay="..."
                          ActionCardTitle="Build Web Apps"
                          ActionCardDescription="Create powerful web applications using HTML, CSS, and JavaScript."
                          // ... other properties
                          />
        }
        else
        {
            <Table TItem="AppListItem" DataSource="@AppsViewModel.Apps" Class="autoscroll-list" Loading="@loading">
                <Column Title="Name" @bind-Field="@context.Name">
                    @context.Name
                </Column>
                
                <Column Title="Status" @bind-Field="@context.IsPublished">
                    @if (context.IsPublished)
                    {
                        <Tag Color="green">Published</Tag>
                    }
                    else
                    {
                        <Tag Color="default">Draft</Tag>
                    }
                </Column>

                <Column Title="Created" @bind-Field="@context.Created">
                    @context.Created
                </Column>

                <Column Title="Updated" @bind-Field="@context.Updated">
                    @context.Updated
                </Column>

                <ActionColumn Title="Action">
                    <div>
                        <Space>
                            <SpaceItem>
                                <Tooltip Title="Edit app">
                                    <Button Icon="edit" @onclick="@(() => EditApp(context.Id))">Edit</Button>
                                </Tooltip>
                            </SpaceItem>
                            @if (context.IsPublished)
                            {
                                <SpaceItem>
                                    <Tooltip Title="View published app">
                                        <Button Icon="eye" @onclick="@(() => ViewApp(context.PublicUrl))">View</Button>
                                    </Tooltip>
                                </SpaceItem>
                            }
                        </Space>

                        <Dropdown Placement="@Placement.Bottom" Trigger="@(new Trigger[] { Trigger.Click })">
                            <Overlay>
                                <Menu>
                                    @if (!context.IsPublished)
                                    {
                                        <MenuItem Icon="cloud-upload" @onclick="@(() => PublishApp(context.Id))">
                                            Publish
                                        </MenuItem>
                                    }
                                    else
                                    {
                                        <MenuItem Icon="cloud-download" @onclick="@(() => UnpublishApp(context.Id))">
                                            Unpublish
                                        </MenuItem>
                                    }
                                    <Popconfirm Title="Sure to delete?"
                                                OnConfirm="()=> DeleteApp(context.Id)"
                                                OkText="Yes"
                                                CancelText="No">
                                        <MenuItem Icon="delete">                               
                                            Delete
                                        </MenuItem>
                                    </Popconfirm>
                                </Menu>
                            </Overlay>
                            <ChildContent>
                                <Tooltip Title="More actions">
                                    <a href="#" @onclick:preventDefault @onmousedown:stopPropagation>
                                        <Icon Type="more" Theme="IconThemeType.Outline" />
                                    </a>
                                </Tooltip>
                            </ChildContent>
                        </Dropdown>
                    </div>
                </ActionColumn>
            </Table>
        }
    </Content>
</Layout>
```

### 2. AppDesignerPage (`Automaton.Studio/Pages/Apps/AppDesignerPage.razor`)

Following the pattern from `StepPage.razor`, with modifications for HTML/CSS/JavaScript editing:

```razor
@page "/appdesigner/{appId}"
@using AntDesign

<Layout>
    <Content>
        <Header Class="header">
            <Row>
                <Col>
                    <Space Direction="SpaceDirection.Horizontal">
                        <SpaceItem Class="header-item">
                            <Breadcrumb>
                                <BreadcrumbItem Href="/">
                                    <Icon Type="home" Width="16" Height="16" />
                                    <span>Home</span>
                                </BreadcrumbItem>
                                <BreadcrumbItem Href="/apps">
                                    <Icon Type="appstore" Width="16" Height="16" />
                                    <span>Apps</span>
                                </BreadcrumbItem>
                                <BreadcrumbItem>
                                    <Icon Type="edit" Width="16" Height="16" />
                                    <span>@AppDesignerViewModel.App.Name</span>
                                </BreadcrumbItem>
                            </Breadcrumb>
                        </SpaceItem>
                    </Space>
                </Col>
                <Col Class="right-actions">
                    <Space Class="right-header-items">
                        <SpaceItem>
                            <Tooltip Title="Save app">
                                <Button Icon="save" OnClick="Save" Type="@ButtonType.Primary">
                                    Save
                                </Button>
                            </Tooltip>
                        </SpaceItem>
                        <SpaceItem>
                            <Tooltip Title="Preview app">
                                <Button Icon="eye" OnClick="Preview" Type="@ButtonType.Default">
                                    Preview
                                </Button>
                            </Tooltip>
                        </SpaceItem>
                    </Space>
                </Col>
            </Row>
        </Header>
        
        <Form @ref="form"
              Model="@AppDesignerViewModel.App"
              Loading="loading"
              Class="app-form"
              Layout="@FormLayout.Vertical"
              LabelColSpan="24"
              WrapperColSpan="24">
            <Validator>
                <FluentValidationValidator />
                <ValidationSummary />
            </Validator>
            <ChildContent>
                <Tabs>
                    <TabPane Tab="Code" Key="1">
                        <Splitter Layout="@SplitterLayout.Horizontal" Class="main-splitter">
                            <SplitterPanel Size="70%">
                                <div class="left-panel">
                                    <div class="monaco-container">
                                        <StandaloneCodeEditor Id="app-code" 
                                                            ConstructionOptions="EditorConstructionOptions" 
                                                            @ref="codeEditor" />
                                    </div>
                                </div>
                            </SplitterPanel>

                            <SplitterPanel Size="30%">
                                <div class="right-panel-container">
                                    <Tabs Size="TabSize.Small" Class="right-panel">
                                        <TabPane Tab="AI Chat" Key="ai">
                                            <div class="tab-content-scrollable">
                                                <AppAiChatPanel OnCodeGenerated="OnCodeGenerated"
                                                               GetCurrentCode="@GetCurrentCodeForAI"
                                                               CurrentAppId="@AppId" />
                                            </div>
                                        </TabPane>
                                        <TabPane Tab="Flow Connections" Key="flows">
                                            <div class="tab-content-scrollable">
                                                <div class="flows-panel">
                                                    <PanelHeader Title="Connected Flows"
                                                                ShowClearButton="false"
                                                                ShowPinButton="true"
                                                                ShowCloseButton="true" />
                                                    
                                                    <div class="flows-list">
                                                        @foreach (var flowId in AppDesignerViewModel.App.Definition.ConnectedFlowIds)
                                                        {
                                                            <div class="flow-item">
                                                                <span>@flowId</span>
                                                                <Button Icon="delete" 
                                                                        Size="@ButtonSize.Small" 
                                                                        OnClick="() => RemoveFlowConnection(flowId)">
                                                                </Button>
                                                            </div>
                                                        }
                                                    </div>
                                                    
                                                    <Button Icon="plus" OnClick="AddFlowConnection">
                                                        Add Flow
                                                    </Button>
                                                </div>
                                            </div>
                                        </TabPane>
                                    </Tabs>
                                </div>
                            </SplitterPanel>
                        </Splitter>
                    </TabPane>
                    
                    <TabPane Tab="Settings" Key="2">
                        <div class="settings-container">
                            <Card Class="settings-card" Bordered="true">
                                <TitleTemplate>
                                    <Space>
                                        <SpaceItem>
                                            <Icon Type="setting" Style="color: #1890ff;" />
                                        </SpaceItem>
                                        <SpaceItem>
                                            <span>App Configuration</span>
                                        </SpaceItem>
                                    </Space>
                                </TitleTemplate>
                                <ChildContent>
                                    <Row Gutter="(24, 16)">
                                        <Col Xs="24" Md="12">
                                            <FormItem Label="Name" Required>
                                                <Input @bind-Value="@context.Name" 
                                                       Placeholder="Enter app name" 
                                                       Size="@InputSize.Large" />
                                            </FormItem>
                                        </Col>
                                        <Col Xs="24" Md="12">
                                            <FormItem Label="Display Name">
                                                <Input @bind-Value="@context.DisplayName" 
                                                       Placeholder="Enter display name" 
                                                       Size="@InputSize.Large" />
                                            </FormItem>
                                        </Col>
                                    </Row>

                                    <Row Gutter="(24, 16)">
                                        <Col Span="24">
                                            <FormItem Label="Description">
                                                <TextArea @bind-Value="@context.Description" 
                                                         Placeholder="Describe your app..."
                                                         Rows="4"
                                                         ShowCount
                                                         MaxLength="500"
                                                         Size="@InputSize.Large" />
                                            </FormItem>
                                        </Col>
                                    </Row>
                                </ChildContent>
                            </Card>
                            
                            <Card Class="settings-card" Bordered="true">
                                <TitleTemplate>
                                    <Space>
                                        <SpaceItem>
                                            <Icon Type="mobile" Style="color: #1890ff;" />
                                        </SpaceItem>
                                        <SpaceItem>
                                            <span>PWA Settings</span>
                                        </SpaceItem>
                                    </Space>
                                </TitleTemplate>
                                <ChildContent>
                                    <FormItem Label="Enable PWA">
                                        <Switch @bind-Value="@context.Definition.Settings.EnablePwa" />
                                    </FormItem>
                                    
                                    @if (context.Definition.Settings.EnablePwa)
                                    {
                                        <Row Gutter="(24, 16)">
                                            <Col Xs="24" Md="12">
                                                <FormItem Label="PWA Name">
                                                    <Input @bind-Value="@context.Definition.Settings.PwaName" />
                                                </FormItem>
                                            </Col>
                                            <Col Xs="24" Md="12">
                                                <FormItem Label="PWA Short Name">
                                                    <Input @bind-Value="@context.Definition.Settings.PwaShortName" />
                                                </FormItem>
                                            </Col>
                                        </Row>
                                        
                                        <!-- Additional PWA settings -->
                                    }
                                </ChildContent>
                            </Card>
                        </div>
                    </TabPane>
                </Tabs>
            </ChildContent>
        </Form>
    </Content>
</Layout>
```

### 3. AppAiChatPanel (`Automaton.Studio/Pages/Apps/Components/AppAiChatPanel.razor`)

A simplified version of `StepAiChatPanel` adapted for HTML/CSS/JavaScript generation:

```razor
@using AntDesign

<div class="ai-chat-panel">
    <div class="chat-messages">
        @foreach (var message in messages)
        {
            <div class="message @message.Role">
                <div class="message-content">
                    @message.Content
                </div>
            </div>
        }
    </div>
    
    <div class="chat-input">
        <TextArea @bind-Value="userPrompt" 
                  Placeholder="Describe the app you want to create..."
                  Rows="3" />
        <Button Type="@ButtonType.Primary" 
                OnClick="GenerateCode"
                Loading="@isGenerating">
            Generate
        </Button>
    </div>
</div>
```

### 4. Preview Component

The preview will render the app in an iframe:

```razor
@page "/apps/preview/{appId}"

<div class="app-preview">
    <iframe id="app-preview-frame" 
            srcdoc="@htmlContent"
            sandbox="allow-scripts allow-same-origin"
            class="preview-frame">
    </iframe>
</div>
```

### 5. Public App Viewer

A separate page for viewing published apps:

```razor
@page "/apps/{publicUrl}"
@attribute [AllowAnonymous]

<div class="public-app-container">
    @if (app != null)
    {
        <div id="app-content" @key="app.Id">
            @((MarkupString)app.HtmlContent)
        </div>
        
        @if (app.Settings?.EnablePwa == true)
        {
            <!-- PWA metadata injection -->
            <link rel="manifest" href="/apps/@publicUrl/manifest.json">
        }
    }
    else
    {
        <Result Status="404"
                Title="App Not Found"
                SubTitle="The app you're looking for doesn't exist or has been unpublished." />
    }
</div>

<!-- Inject Automaton SDK for Flow invocation -->
<script src="/js/automaton-sdk.js"></script>
```

### 6. Automaton SDK (`wwwroot/js/automaton-sdk.js`)

JavaScript SDK that apps can use to interact with Flows:

```javascript
window.Automaton = {
    executeFlow: async function(flowId, inputVariables) {
        const appId = window.__AUTOMATON_APP_ID__;
        
        try {
            const response = await fetch(`/api/flows/${flowId}/execute-from-app`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    appId: appId,
                    inputVariables: inputVariables
                })
            });
            
            if (!response.ok) {
                throw new Error(`Failed to execute flow: ${response.statusText}`);
            }
            
            const result = await response.json();
            return result;
        } catch (error) {
            console.error('Error executing flow:', error);
            throw error;
        }
    },
    
    // Utility function to get output variable from flow execution result
    getOutputVariable: function(result, variableName) {
        return result.outputVariables?.[variableName];
    }
};
```

### 7. Navigation Menu Update

Add Apps to the navigation menu in `NavMenu.razor`:

```razor
<MenuItem RouterLink="/apps" RouterMatch="NavLinkMatch.Prefix">
    <Icon Type="appstore" Width="16" Height="16" />
    <span>Apps</span>
</MenuItem>
```

## Implementation Phases

### Phase 1: Backend Foundation (Database & Services)
1. Create entity models (`App`, `AppUser`)
2. Create database migration
3. Implement `AppsService` with basic CRUD operations
4. Implement `AppsController`
5. Update `ApplicationDbContext`
6. Add activity logging support

### Phase 2: Frontend Foundation (List & Basic Editor)
1. Create `AppsPage.razor` for listing apps
2. Create `AppDesignerPage.razor` with Monaco editor
3. Add routing and navigation menu items
4. Implement basic save/load functionality
5. Create view models and services

### Phase 3: AI Integration (Code Generation)
1. Create `AppAiChatPanel` component
2. Implement AI service integration (reuse existing AI infrastructure)
3. Add prompt engineering for HTML/CSS/JavaScript generation
4. Integrate generated code into Monaco editor

### Phase 4: Preview & Testing
1. Create preview component
2. Implement iframe-based preview
3. Add security sandboxing
4. Test preview functionality

### Phase 5: Publishing & Public Access
1. Implement publish/unpublish functionality
2. Create `AppPublicAccessController`
3. Implement public app viewer page
4. Add URL slug validation and management

### Phase 6: Flow Integration
1. Create Automaton SDK (`automaton-sdk.js`)
2. Implement Flow invocation API endpoint
3. Add Flow connection management UI
4. Test Flow integration from apps

### Phase 7: PWA Support
1. Implement PWA manifest generation
2. Add service worker support
3. Create PWA settings UI
4. Test PWA installation on mobile devices

## Security Considerations

1. **Sandboxing**: Apps run in sandboxed iframes with restricted permissions
2. **Flow Authorization**: Apps can only invoke Flows they're explicitly connected to
3. **Input Validation**: All user inputs are validated and sanitized
4. **Rate Limiting**: Implement rate limiting on Flow invocations from apps
5. **CORS**: Configure appropriate CORS policies for public app access
6. **CSP**: Implement Content Security Policy headers for apps
7. **XSS Prevention**: Sanitize user-generated HTML before rendering

## Testing Strategy

1. **Unit Tests**: Test services, controllers, and data access
2. **Integration Tests**: Test end-to-end flows (create, edit, publish, access)
3. **UI Tests**: Test Blazor components and user interactions
4. **Security Tests**: Test sandboxing, authorization, and input validation
5. **Performance Tests**: Test public app loading times and Flow invocation performance

## Migration Path

For existing users:
1. Deploy database migration
2. Deploy backend services
3. Deploy frontend updates
4. Announce new feature via in-app notifications
5. Provide documentation and examples

## Documentation Requirements

1. User Guide: Creating and publishing apps
2. Developer Guide: Using the Automaton SDK
3. API Reference: Flow invocation API
4. Security Guide: Best practices for app security
5. PWA Guide: Deploying apps as Progressive Web Apps

## Future Enhancements (Out of Scope for Initial Release)

1. App templates and marketplace
2. Version control and rollback
3. A/B testing capabilities
4. Analytics and usage tracking
5. Custom domain support
6. Collaborative editing
7. Component library and drag-and-drop builder
8. Server-side rendering options
9. App cloning and duplication
10. Export/import functionality

## Open Questions & Decisions Needed

1. **Monaco Editor Language**: Should we use HTML language mode or a custom mode for combined HTML/CSS/JavaScript?
2. **Preview Refresh**: Should preview auto-refresh on code changes or require manual refresh?
3. **Flow Discovery**: How should users discover and select Flows to connect to their apps?
4. **Public URL Format**: Should URLs be `/apps/{slug}` or `/public/apps/{slug}`?
5. **Asset Storage**: How will users upload and manage images/assets for their apps?
6. **Database Choice**: Should we use SQLite, SQL Server, or PostgreSQL for production?

## Success Metrics

1. Number of apps created
2. Number of apps published
3. Public app page views
4. Flow invocations from apps
5. User satisfaction ratings
6. Time to create first app

## Conclusion

This technical approach provides a comprehensive foundation for implementing the Apps feature in Automaton.Studio. The design follows established patterns from Custom Steps while introducing new capabilities for web app creation, publishing, and Flow integration. The phased implementation approach ensures steady progress with testable milestones.

The architecture supports the core requirements:
- ✅ Create/edit apps using Monaco editor
- ✅ AI-assisted code generation
- ✅ Preview functionality
- ✅ Public access without authentication
- ✅ Flow integration for backend logic
- ✅ PWA support for mobile deployment

Next steps: Review this document with stakeholders, address open questions, and begin Phase 1 implementation.
