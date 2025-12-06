# Phase 2: Backend Actions Tab UI

## Overview

Add a "Backend Actions" tab to `AppPage.razor` that lists Custom Steps linked to the App, allowing users to view, navigate to, and manage backend steps without leaving the App designer context.

**Estimated Effort:** 2-3 days

---

## Goal

Provide visibility and management of backend steps directly within the App designer:

```
???????????????????????????????????????????????????????????????????????????
? AppPage.razor                                                           ?
???????????????????????????????????????????????????????????????????????????
?                                                                         ?
?  [ Code ] [ Preview ] [ Backend Actions ] [ Settings ]                  ?
?                                                                         ?
?  ???????????????????????????????????????????????????????????????????   ?
?  ? Backend Actions                                                  ?   ?
?  ?                                                                  ?   ?
?  ? ??????????????????????????????????????????????????????????????? ?   ?
?  ? ? ?? SendEmailStep                           [Edit] [Unlink]  ? ?   ?
?  ? ?    Language: Python | Version: 3                            ? ?   ?
?  ? ?    Sends emails using SendGrid API                          ? ?   ?
?  ? ??????????????????????????????????????????????????????????????? ?   ?
?  ?                                                                  ?   ?
?  ? ??????????????????????????????????????????????????????????????? ?   ?
?  ? ? ?? ProcessDataStep                         [Edit] [Unlink]  ? ?   ?
?  ? ?    Language: Python | Version: 2                            ? ?   ?
?  ? ?    Processes and transforms data                            ? ?   ?
?  ? ??????????????????????????????????????????????????????????????? ?   ?
?  ?                                                                  ?   ?
?  ? [+ Link Existing Step]  [+ Create New Step]                     ?   ?
?  ?                                                                  ?   ?
?  ? ?? These steps can be called using automatonApi.executeStep()   ?   ?
?  ?                                                                  ?   ?
?  ???????????????????????????????????????????????????????????????????   ?
?                                                                         ?
???????????????????????????????????????????????????????????????????????????
```

---

## Components to Create/Modify

### 1. Update `AppPage.razor`

Add new TabPane for Backend Actions:

```razor
@* Add after Preview TabPane, before Settings *@

<TabPane Tab="Backend Actions" Key="backend">
    <div class="backend-actions-container">
        <Card Class="backend-card" Bordered="true">
            <TitleTemplate>
                <Space>
                    <SpaceItem>
                        <Icon Type="code" Style="color: #1890ff;" />
                    </SpaceItem>
                    <SpaceItem>
                        <span>Linked Custom Steps</span>
                    </SpaceItem>
                    <SpaceItem>
                        <Tag Color="blue">@linkedSteps.Count steps</Tag>
                    </SpaceItem>
                </Space>
            </TitleTemplate>
            <Extra>
                <Space>
                    <SpaceItem>
                        <Button Icon="plus" 
                                Type="@ButtonType.Primary" 
                                OnClick="ShowLinkStepDialog">
                            Link Step
                        </Button>
                    </SpaceItem>
                    <SpaceItem>
                        <Button Icon="plus-circle" 
                                OnClick="CreateNewStep">
                            Create Step
                        </Button>
                    </SpaceItem>
                </Space>
            </Extra>
            <ChildContent>
                @if (loadingSteps)
                {
                    <Spin Tip="Loading steps..." />
                }
                else if (!linkedSteps.Any())
                {
                    <Empty Description="No backend steps linked to this app">
                        <ChildContent>
                            <Space Direction="DirectionVHType.Vertical" Style="width: 100%">
                                <SpaceItem>
                                    <Text Type="TextElementType.Secondary">
                                        Backend steps let you execute Python/Batch scripts when users interact with your app.
                                    </Text>
                                </SpaceItem>
                                <SpaceItem>
                                    <Space>
                                        <SpaceItem>
                                            <Button Type="@ButtonType.Primary" 
                                                    Icon="plus" 
                                                    OnClick="ShowLinkStepDialog">
                                                Link Existing Step
                                            </Button>
                                        </SpaceItem>
                                        <SpaceItem>
                                            <Button Icon="plus-circle" 
                                                    OnClick="CreateNewStep">
                                                Create New Step
                                            </Button>
                                        </SpaceItem>
                                    </Space>
                                </SpaceItem>
                            </Space>
                        </ChildContent>
                    </Empty>
                }
                else
                {
                    <div class="steps-list">
                        @foreach (var step in linkedSteps)
                        {
                            <Card Class="step-card" Size="small" Bordered="true">
                                <ChildContent>
                                    <div class="step-card-content">
                                        <div class="step-info">
                                            <Space>
                                                <SpaceItem>
                                                    <Icon Type="@GetStepIcon(step.Language)" 
                                                          Style="font-size: 24px; color: #1890ff;" />
                                                </SpaceItem>
                                                <SpaceItem>
                                                    <div class="step-details">
                                                        <div class="step-name">
                                                            <Text Strong>@step.DisplayName</Text>
                                                            @if (!string.IsNullOrEmpty(step.Name) && step.Name != step.DisplayName)
                                                            {
                                                                <Text Type="TextElementType.Secondary" 
                                                                      Style="margin-left: 8px; font-size: 12px;">
                                                                    (@step.Name)
                                                                </Text>
                                                            }
                                                        </div>
                                                        <div class="step-meta">
                                                            <Space Size="@SpaceSize.Small">
                                                                <SpaceItem>
                                                                    <Tag Color="@GetLanguageColor(step.Language)">
                                                                        @step.Language
                                                                    </Tag>
                                                                </SpaceItem>
                                                                <SpaceItem>
                                                                    <Tag>v@step.Version</Tag>
                                                                </SpaceItem>
                                                                @if (step.HasInputVariables)
                                                                {
                                                                    <SpaceItem>
                                                                        <Tooltip Title="Has input variables">
                                                                            <Tag Icon="arrow-right" Color="green">
                                                                                @step.InputCount inputs
                                                                            </Tag>
                                                                        </Tooltip>
                                                                    </SpaceItem>
                                                                }
                                                                @if (step.HasOutputVariables)
                                                                {
                                                                    <SpaceItem>
                                                                        <Tooltip Title="Has output variables">
                                                                            <Tag Icon="arrow-left" Color="orange">
                                                                                @step.OutputCount outputs
                                                                            </Tag>
                                                                        </Tooltip>
                                                                    </SpaceItem>
                                                                }
                                                            </Space>
                                                        </div>
                                                        @if (!string.IsNullOrEmpty(step.Description))
                                                        {
                                                            <div class="step-description">
                                                                <Text Type="TextElementType.Secondary">
                                                                    @step.Description
                                                                </Text>
                                                            </div>
                                                        }
                                                    </div>
                                                </SpaceItem>
                                            </Space>
                                        </div>
                                        <div class="step-actions">
                                            <Space>
                                                <SpaceItem>
                                                    <Tooltip Title="Edit step code">
                                                        <Button Icon="edit" 
                                                                Size="@ButtonSize.Small"
                                                                OnClick="() => NavigateToStep(step.Id)">
                                                            Edit
                                                        </Button>
                                                    </Tooltip>
                                                </SpaceItem>
                                                <SpaceItem>
                                                    <Tooltip Title="Copy executeStep code">
                                                        <Button Icon="copy" 
                                                                Size="@ButtonSize.Small"
                                                                OnClick="() => CopyStepCode(step)">
                                                        </Button>
                                                    </Tooltip>
                                                </SpaceItem>
                                                <SpaceItem>
                                                    <Popconfirm Title="Unlink this step from the app?"
                                                                OnConfirm="() => UnlinkStep(step.Id)"
                                                                OkText="Unlink"
                                                                CancelText="Cancel">
                                                        <Tooltip Title="Unlink step">
                                                            <Button Icon="disconnect" 
                                                                    Size="@ButtonSize.Small"
                                                                    Danger>
                                                            </Button>
                                                        </Tooltip>
                                                    </Popconfirm>
                                                </SpaceItem>
                                            </Space>
                                        </div>
                                    </div>
                                </ChildContent>
                            </Card>
                        }
                    </div>
                }
                
                <Divider />
                
                <Alert Type="@AlertType.Info" ShowIcon="true">
                    <MessageTemplate>
                        <div>
                            <Text Strong>How to use these steps in your app:</Text>
                            <pre class="code-example">
const result = await automatonApi.executeStep('StepName', {
    inputs: { key: 'value' }
});
console.log(result.outputVariables);</pre>
                        </div>
                    </MessageTemplate>
                </Alert>
            </ChildContent>
        </Card>
    </div>
</TabPane>
```

---

### 2. Update `AppPage.razor.cs`

Add code-behind logic:

```csharp
// Add to class fields
private List<LinkedStepInfo> linkedSteps = new();
private bool loadingSteps = false;

// Add service injection
[Inject] private AppStepsService AppStepsService { get; set; }

// Add to OnInitializedAsync
protected override async Task OnInitializedAsync()
{
    // ... existing code ...
    
    // Load linked steps
    await LoadLinkedSteps();
}

private async Task LoadLinkedSteps()
{
    loadingSteps = true;
    
    try
    {
        linkedSteps = await AppStepsService.GetLinkedStepsAsync(currentApp.Id);
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Failed to load linked steps");
        _ = MessageService.ErrorAsync("Failed to load linked steps");
    }
    finally
    {
        loadingSteps = false;
    }
}

private async Task ShowLinkStepDialog()
{
    var availableSteps = await AppStepsService.GetAvailableStepsAsync(currentApp.Id);
    
    var modalRef = await ModalService.CreateModalAsync<LinkStepDialog, List<LinkedStepInfo>>(
        new ModalOptions
        {
            Title = "Link Custom Step",
            Width = 600,
            OkText = "Link",
            CancelText = "Cancel"
        },
        availableSteps
    );

    modalRef.OnOk = async () =>
    {
        await LoadLinkedSteps();
        await InvokeAsync(StateHasChanged);
    };
}

private void CreateNewStep()
{
    // Navigate to create new step with app context
    NavigationManager.NavigateTo($"/customsteps/new?appId={currentApp.Id}");
}

private void NavigateToStep(Guid stepId)
{
    NavigationManager.NavigateTo($"/step/{stepId}");
}

private async Task CopyStepCode(LinkedStepInfo step)
{
    var code = GenerateStepCode(step);
    await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", code);
    _ = MessageService.SuccessAsync("Code copied to clipboard!");
}

private string GenerateStepCode(LinkedStepInfo step)
{
    var inputsExample = step.HasInputVariables
        ? string.Join(", ", step.InputVariables.Take(3).Select(v => $"'{v.Name}': 'value'"))
        : "";

    return $@"// Execute {step.DisplayName}
const result = await automatonApi.executeStep('{step.Name}', {{
    inputs: {{ {inputsExample} }}
}});

// Access results
console.log('Success:', result.success);
console.log('Outputs:', result.outputVariables);";
}

private async Task UnlinkStep(Guid stepId)
{
    try
    {
        await AppStepsService.UnlinkStepAsync(currentApp.Id, stepId);
        linkedSteps.RemoveAll(s => s.Id == stepId);
        _ = MessageService.SuccessAsync("Step unlinked successfully");
        await InvokeAsync(StateHasChanged);
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Failed to unlink step");
        _ = MessageService.ErrorAsync("Failed to unlink step");
    }
}

private string GetStepIcon(string language)
{
    return language?.ToLower() switch
    {
        "python" => "code",
        "batch" => "code",
        "powershell" => "code",
        _ => "api"
    };
}

private string GetLanguageColor(string language)
{
    return language?.ToLower() switch
    {
        "python" => "blue",
        "batch" => "cyan",
        "powershell" => "purple",
        _ => "default"
    };
}
```

---

### 3. Add Styles to `AppPage.razor`

```css
<style>
    /* Backend Actions Tab Styles */
    .backend-actions-container {
        padding: 24px;
        max-width: 1000px;
        margin: 0 auto;
    }

    .backend-card {
        box-shadow: 0 2px 8px rgba(0, 0, 0, 0.09);
        border-radius: 8px;
    }

    .steps-list {
        display: flex;
        flex-direction: column;
        gap: 16px;
    }

    .step-card {
        transition: box-shadow 0.3s ease;
    }

    .step-card:hover {
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
    }

    .step-card-content {
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
    }

    .step-info {
        flex: 1;
    }

    .step-details {
        display: flex;
        flex-direction: column;
        gap: 4px;
    }

    .step-name {
        font-size: 16px;
    }

    .step-meta {
        margin-top: 4px;
    }

    .step-description {
        margin-top: 8px;
        font-size: 13px;
    }

    .step-actions {
        flex-shrink: 0;
        margin-left: 16px;
    }

    .code-example {
        background: #f6f8fa;
        border-radius: 6px;
        padding: 12px;
        font-family: 'Consolas', 'Monaco', monospace;
        font-size: 13px;
        overflow-x: auto;
        margin-top: 8px;
        white-space: pre-wrap;
    }

    @@media (max-width: 768px) {
        .step-card-content {
            flex-direction: column;
        }

        .step-actions {
            margin-left: 0;
            margin-top: 12px;
        }
    }
</style>
```

---

### 4. Create `LinkStepDialog.razor`

**Location:** `src/Automaton.Studio/Pages/Apps/Components/LinkStepDialog/LinkStepDialog.razor`

```razor
@using AntDesign
@using Automaton.Studio.Models

<div class="link-step-dialog">
    <div class="search-box">
        <Search Placeholder="Search steps..."
                @bind-Value="searchText"
                OnSearch="OnSearch"
                Style="margin-bottom: 16px;" />
    </div>

    @if (loading)
    {
        <div style="text-align: center; padding: 40px;">
            <Spin Tip="Loading available steps..." />
        </div>
    }
    else if (!filteredSteps.Any())
    {
        <Empty Description="No available steps to link">
            <ChildContent>
                <Button Type="@ButtonType.Primary" 
                        OnClick="CreateNewStep">
                    Create New Step
                </Button>
            </ChildContent>
        </Empty>
    }
    else
    {
        <div class="steps-grid">
            @foreach (var step in filteredSteps)
            {
                <Card Class="step-option @(selectedStepId == step.Id ? "selected" : "")"
                      Size="small"
                      Hoverable="true"
                      OnClick="() => SelectStep(step.Id)">
                    <ChildContent>
                        <div class="step-option-content">
                            <Checkbox Checked="@(selectedStepId == step.Id)" 
                                      OnChange="e => SelectStep(step.Id)" />
                            <div class="step-option-info">
                                <div class="step-option-name">
                                    <Text Strong>@step.DisplayName</Text>
                                </div>
                                <div class="step-option-meta">
                                    <Tag Color="@GetLanguageColor(step.Language)" Size="small">
                                        @step.Language
                                    </Tag>
                                </div>
                                @if (!string.IsNullOrEmpty(step.Description))
                                {
                                    <div class="step-option-description">
                                        <Text Type="TextElementType.Secondary" 
                                              Style="font-size: 12px;">
                                            @step.Description
                                        </Text>
                                    </div>
                                }
                            </div>
                        </div>
                    </ChildContent>
                </Card>
            }
        </div>
    }
</div>

<style>
    .link-step-dialog {
        max-height: 400px;
        overflow-y: auto;
    }

    .steps-grid {
        display: flex;
        flex-direction: column;
        gap: 8px;
    }

    .step-option {
        cursor: pointer;
        transition: all 0.2s;
    }

    .step-option.selected {
        border-color: #1890ff;
        background: #e6f7ff;
    }

    .step-option-content {
        display: flex;
        align-items: flex-start;
        gap: 12px;
    }

    .step-option-info {
        flex: 1;
    }

    .step-option-name {
        margin-bottom: 4px;
    }

    .step-option-description {
        margin-top: 4px;
    }
</style>

@code {
    [CascadingParameter] public ModalRef ModalRef { get; set; }
    [Parameter] public List<LinkedStepInfo> AvailableSteps { get; set; } = new();
    
    [Inject] private AppStepsService AppStepsService { get; set; }
    [Inject] private NavigationManager NavigationManager { get; set; }

    private string searchText = "";
    private bool loading = false;
    private Guid? selectedStepId;
    private List<LinkedStepInfo> filteredSteps => string.IsNullOrWhiteSpace(searchText)
        ? AvailableSteps
        : AvailableSteps.Where(s => 
            s.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            s.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            (s.Description?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

    private void OnSearch(string value)
    {
        searchText = value;
        StateHasChanged();
    }

    private void SelectStep(Guid stepId)
    {
        selectedStepId = selectedStepId == stepId ? null : stepId;
        StateHasChanged();
    }

    private void CreateNewStep()
    {
        ModalRef.Close();
        NavigationManager.NavigateTo("/customsteps/new");
    }

    private string GetLanguageColor(string language)
    {
        return language?.ToLower() switch
        {
            "python" => "blue",
            "batch" => "cyan",
            "powershell" => "purple",
            _ => "default"
        };
    }
}
```

---

### 5. Create `AppStepsService.cs` (Frontend)

**Location:** `src/Automaton.Studio/Services/AppStepsService.cs`

```csharp
using Automaton.Studio.Models;
using System.Net.Http.Json;

namespace Automaton.Studio.Services;

/// <summary>
/// Service for managing App-Step relationships in the frontend
/// </summary>
public class AppStepsService
{
    private readonly HttpClient httpClient;

    public AppStepsService(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    /// <summary>
    /// Get steps linked to an app
    /// </summary>
    public async Task<List<LinkedStepInfo>> GetLinkedStepsAsync(Guid appId)
    {
        var response = await httpClient.GetFromJsonAsync<List<LinkedStepInfo>>(
            $"api/apps/{appId}/steps");
        return response ?? new List<LinkedStepInfo>();
    }

    /// <summary>
    /// Get available steps that can be linked
    /// </summary>
    public async Task<List<LinkedStepInfo>> GetAvailableStepsAsync(Guid appId)
    {
        var response = await httpClient.GetFromJsonAsync<List<LinkedStepInfo>>(
            $"api/apps/{appId}/steps/available");
        return response ?? new List<LinkedStepInfo>();
    }

    /// <summary>
    /// Link a step to an app
    /// </summary>
    public async Task LinkStepAsync(Guid appId, Guid stepId)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"api/apps/{appId}/steps/{stepId}/link", 
            new { });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Unlink a step from an app
    /// </summary>
    public async Task UnlinkStepAsync(Guid appId, Guid stepId)
    {
        var response = await httpClient.DeleteAsync(
            $"api/apps/{appId}/steps/{stepId}");
        response.EnsureSuccessStatusCode();
    }
}
```

---

### 6. Create `LinkedStepInfo.cs` Model

**Location:** `src/Automaton.Studio/Models/LinkedStepInfo.cs`

```csharp
using Automaton.Core.Models;

namespace Automaton.Studio.Models;

/// <summary>
/// Information about a Custom Step linked to an App
/// </summary>
public class LinkedStepInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Language { get; set; } = "Python";
    public int Version { get; set; } = 1;
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    
    // Variable info
    public bool HasInputVariables => InputCount > 0;
    public bool HasOutputVariables => OutputCount > 0;
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
    public List<StepVariableInfo> InputVariables { get; set; } = new();
    public List<StepVariableInfo> OutputVariables { get; set; } = new();
}

public class StepVariableInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = "String";
}
```

---

### 7. Register Service in DI

**Location:** `src/Automaton.Studio/Program.cs`

```csharp
builder.Services.AddScoped<AppStepsService>();
```

---

## Backend API Endpoints (Server Side)

Add these endpoints to `AppsController.cs`:

```csharp
/// <summary>
/// Get steps linked to an app
/// </summary>
[HttpGet("{id}/steps")]
public async Task<ActionResult<IEnumerable<LinkedStepInfo>>> GetLinkedSteps(Guid id)
{
    var steps = await appStepsService.GetLinkedStepsAsync(id);
    return Ok(steps);
}

/// <summary>
/// Get available steps that can be linked
/// </summary>
[HttpGet("{id}/steps/available")]
public async Task<ActionResult<IEnumerable<LinkedStepInfo>>> GetAvailableSteps(Guid id)
{
    var steps = await appStepsService.GetAvailableStepsAsync(id);
    return Ok(steps);
}

/// <summary>
/// Link a step to an app
/// </summary>
[HttpPost("{appId}/steps/{stepId}/link")]
public async Task<IActionResult> LinkStep(Guid appId, Guid stepId)
{
    await appStepsService.LinkStepAsync(appId, stepId);
    return NoContent();
}

/// <summary>
/// Unlink a step from an app
/// </summary>
[HttpDelete("{appId}/steps/{stepId}")]
public async Task<IActionResult> UnlinkStep(Guid appId, Guid stepId)
{
    await appStepsService.UnlinkStepAsync(appId, stepId);
    return NoContent();
}
```

---

## Testing Checklist

- [ ] Backend Actions tab displays correctly
- [ ] Empty state shows when no steps are linked
- [ ] Link Step dialog shows available steps
- [ ] Search filtering works in Link Step dialog
- [ ] Step cards display correct information (name, language, version, variables)
- [ ] Edit button navigates to StepPage.razor
- [ ] Copy button copies correct `executeStep` code
- [ ] Unlink confirmation works
- [ ] Step list refreshes after link/unlink operations
- [ ] Mobile responsive layout works

---

## Next Steps

After Phase 2 is complete:
1. Proceed to [Phase 3: Unified AI System Prompt](./03-phase3-unified-ai-prompt.md)
2. This enables the "one command" generation experience
