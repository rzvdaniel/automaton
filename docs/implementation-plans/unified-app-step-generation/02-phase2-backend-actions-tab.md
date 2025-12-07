# Phase 2: Backend Actions Tab UI

## Overview

Add a "Backend Actions" tab to `AppPage.razor` that displays ALL Custom Steps the user has access to in an inline table with checkboxes, allowing users to link/unlink steps directly without dialogs.

**Estimated Effort:** 2-3 days

---

## Goal

Provide visibility and management of backend steps directly within the App designer using an inline table approach:

```
???????????????????????????????????????????????????????????????????????????
? AppPage.razor                                                           ?
???????????????????????????????????????????????????????????????????????????
?                                                                         ?
?  [ Code ] [ Preview ] [ Backend Actions ] [ Settings ]                  ?
?                                                                         ?
?  ?????????????????????????????????????????????????????????????????????  ?
?  ? Custom Steps                                        [3 linked]    ?  ?
?  ?                                                                   ?  ?
?  ? [?? Search steps...]                    5 of 8 steps              ?  ?
?  ?                                                                   ?  ?
?  ? ????????????????????????????????????????????????????????????????  ?  ?
?  ? ? ?  ? Name                    ? Language ? Variables? Actions ?  ?  ?
?  ? ????????????????????????????????????????????????????????????????  ?  ?
?  ? ? ?  ? ?? SendEmailStep        ? Python   ? 2 in 1out? ?? ??   ?  ?  ?
?  ? ?    ?    Sends emails via API ?          ?          ?         ?  ?  ?
?  ? ????????????????????????????????????????????????????????????????  ?  ?
?  ? ? ?  ? ?? ProcessDataStep      ? Python   ? 1 in 2out? ?? ??   ?  ?  ?
?  ? ?    ?    Processes data       ?          ?          ?         ?  ?  ?
?  ? ????????????????????????????????????????????????????????????????  ?  ?
?  ? ? ?  ? ?? GeneratePdfStep      ? Python   ? 3 in 1out? ??      ?  ?  ?
?  ? ?    ?    Creates PDF files    ?          ?          ?         ?  ?  ?
?  ? ????????????????????????????????????????????????????????????????  ?  ?
?  ?                                                                   ?  ?
?  ? ?? How to use linked steps in your app:                           ?  ?
?  ? const result = await automatonApi.executeStep('StepName', {...}); ?  ?
?  ?                                                                   ?  ?
?  ?????????????????????????????????????????????????????????????????????  ?
?                                                                         ?
???????????????????????????????????????????????????????????????????????????
```

### Key Design Decisions

1. **Inline Table vs Dialog** - Shows all steps directly in the tab without requiring a modal
2. **Checkbox-based Linking** - Click checkbox to instantly link/unlink a step
3. **Select All** - Header checkbox to link/unlink all filtered steps at once
4. **Search/Filter** - Filter steps by name, display name, or description
5. **Full Width** - Uses maximum page width for better visibility on large screens

---

## Components to Create/Modify

### 1. Update `AppPage.razor`

Replace the Backend Actions tab with an inline table:

```razor
<TabPane Tab="Backend Actions" Key="backend">
    <div class="backend-actions-container">
        <Card Class="backend-card" Bordered="true">
            <TitleTemplate>
                <Space>
                    <SpaceItem><Icon Type="code" Style="color: #1890ff;" /></SpaceItem>
                    <SpaceItem><span>Custom Steps</span></SpaceItem>
                    <SpaceItem><Tag Color="@("blue")">@LinkedStepsCount linked</Tag></SpaceItem>
                </Space>
            </TitleTemplate>
            <ChildContent>
                @if (loadingSteps)
                {
                    <Spin Tip="Loading steps..." />
                }
                else if (!allSteps.Any())
                {
                    <Empty Description="@("No custom steps available")">
                        <ChildContent>
                            <div>
                                <p>Create custom steps to add backend functionality to your app.</p>
                                <Button Type="@ButtonType.Primary" Icon="plus" 
                                        OnClick="@(() => NavigationManager.NavigateTo("/customsteps"))">
                                    Create Step
                                </Button>
                            </div>
                        </ChildContent>
                    </Empty>
                }
                else
                {
                    <div class="steps-toolbar">
                        <Space>
                            <SpaceItem>
                                <Input Placeholder="Search steps..."
                                       @bind-Value="stepSearchText"
                                       @oninput="OnStepSearchInput"
                                       AllowClear="true"
                                       Style="width: 300px;" />
                            </SpaceItem>
                            <SpaceItem>
                                <Text Type="TextElementType.Secondary">
                                    @FilteredSteps.Count of @allSteps.Count steps
                                </Text>
                            </SpaceItem>
                        </Space>
                    </div>

                    <Table TItem="StepWithLinkStatus" 
                           DataSource="@FilteredSteps" 
                           Size="@TableSize.Small"
                           Context="step">
                        <Column TData="bool" Title="" Width="60">
                            <TitleTemplate>
                                <Checkbox Checked="@AllFilteredStepsLinked" 
                                          Indeterminate="@(FilteredSteps.Any(s => s.IsLinked) && !AllFilteredStepsLinked)"
                                          OnChange="@(e => ToggleAllFilteredSteps())" />
                            </TitleTemplate>
                            <ChildContent>
                                <Checkbox Checked="@step.IsLinked" 
                                          OnChange="@(e => ToggleStepLink(step))" />
                            </ChildContent>
                        </Column>
                        <Column TData="string" Title="Name">
                            <!-- Step name with icon and description -->
                        </Column>
                        <Column TData="string" Title="Language" Width="100">
                            <Tag Color="@GetLanguageColor(step.Step.Language)">@step.Step.Language</Tag>
                        </Column>
                        <Column TData="string" Title="Variables" Width="150">
                            <!-- Input/Output variable counts -->
                        </Column>
                        <ActionColumn Title="Actions" Width="150">
                            <!-- Edit and Copy buttons -->
                        </ActionColumn>
                    </Table>
                }

                <Divider />
                <Alert Type="@AlertType.Info" ShowIcon="true">
                    <MessageTemplate>
                        <div>
                            <strong>How to use linked steps in your app:</strong>
                            <pre class="code-example">const result = await automatonApi.executeStep('StepName', { inputs: { key: 'value' } });
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

Add code-behind logic for the inline table approach:

```csharp
// Fields
private List<StepWithLinkStatus> allSteps = new();
private bool loadingSteps = false;
private string stepSearchText = "";

// Computed properties
private List<StepWithLinkStatus> FilteredSteps => string.IsNullOrWhiteSpace(stepSearchText)
    ? allSteps
    : allSteps.Where(s =>
        (s.Step.Name?.Contains(stepSearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (s.Step.DisplayName?.Contains(stepSearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (s.Step.Description?.Contains(stepSearchText, StringComparison.OrdinalIgnoreCase) ?? false))
        .ToList();

private int LinkedStepsCount => allSteps.Count(s => s.IsLinked);

private bool AllFilteredStepsLinked => FilteredSteps.Any() && FilteredSteps.All(s => s.IsLinked);

// Load all steps with their link status
private async Task LoadAllSteps()
{
    loadingSteps = true;
    try
    {
        var linkedSteps = await AppStepsService.GetLinkedStepsAsync(currentApp.Id);
        var availableSteps = await AppStepsService.GetAvailableStepsAsync(currentApp.Id);
        
        allSteps = linkedSteps
            .Select(s => new StepWithLinkStatus { Step = s, IsLinked = true })
            .Concat(availableSteps.Select(s => new StepWithLinkStatus { Step = s, IsLinked = false }))
            .OrderBy(s => s.Step.DisplayName ?? s.Step.Name)
            .ToList();
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Failed to load steps");
        _ = MessageService.ErrorAsync("Failed to load steps");
    }
    finally
    {
        loadingSteps = false;
    }
}

// Toggle individual step link
private async Task ToggleStepLink(StepWithLinkStatus stepWithStatus)
{
    try
    {
        if (stepWithStatus.IsLinked)
        {
            await AppStepsService.UnlinkStepAsync(currentApp.Id, stepWithStatus.Step.Id);
            stepWithStatus.IsLinked = false;
        }
        else
        {
            await AppStepsService.LinkStepAsync(currentApp.Id, stepWithStatus.Step.Id);
            stepWithStatus.IsLinked = true;
        }
        await InvokeAsync(StateHasChanged);
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Failed to toggle step link");
        _ = MessageService.ErrorAsync($"Failed to update step link: {ex.Message}");
    }
}

// Toggle all filtered steps
private async Task ToggleAllFilteredSteps()
{
    try
    {
        var filtered = FilteredSteps;
        if (!filtered.Any()) return;

        var shouldLink = !AllFilteredStepsLinked;

        foreach (var stepWithStatus in filtered)
        {
            if (stepWithStatus.IsLinked != shouldLink)
            {
                if (shouldLink)
                    await AppStepsService.LinkStepAsync(currentApp.Id, stepWithStatus.Step.Id);
                else
                    await AppStepsService.UnlinkStepAsync(currentApp.Id, stepWithStatus.Step.Id);
                stepWithStatus.IsLinked = shouldLink;
            }
        }

        var action = shouldLink ? "linked" : "unlinked";
        _ = MessageService.SuccessAsync($"{filtered.Count} step(s) {action}");
        await InvokeAsync(StateHasChanged);
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Failed to toggle all steps");
        _ = MessageService.ErrorAsync($"Failed to update steps: {ex.Message}");
    }
}

// Search handler
private void OnStepSearchInput(ChangeEventArgs e)
{
    stepSearchText = e.Value?.ToString() ?? "";
    StateHasChanged();
}

// Helper class
public class StepWithLinkStatus
{
    public LinkedStepInfo Step { get; set; } = new();
    public bool IsLinked { get; set; }
}
```

---

### 3. Add Styles to `AppPage.razor`

```css
<style>
    /* Backend Actions Tab Styles */
    .backend-actions-container {
        padding: 24px;
        max-width: 100%;
        margin: 0;
    }

    .backend-card {
        box-shadow: 0 2px 8px rgba(0, 0, 0, 0.09);
        border-radius: 8px;
    }

    .steps-toolbar {
        margin-bottom: 16px;
        display: flex;
        justify-content: space-between;
        align-items: center;
    }

    .steps-table {
        margin-top: 16px;
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
</style>
```

---

### 4. ~~Create `LinkStepDialog.razor`~~ (REMOVED)

**Note:** The dialog-based approach has been replaced with an inline table. No separate dialog component is needed.

---

### 5. Update `AppStepsService.cs` (Frontend)

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
    /// Get available steps that can be linked (not yet linked)
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
    /// Link multiple steps to an app
    /// </summary>
    public async Task LinkStepsAsync(Guid appId, List<Guid> stepIds)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"api/apps/{appId}/steps/link", 
            stepIds);
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
/// Information about a Custom Step (linked or available to link)
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
/// Link multiple steps to an app
/// </summary>
[HttpPost("{appId}/steps/link")]
public async Task<IActionResult> LinkSteps(Guid appId, [FromBody] List<Guid> stepIds)
{
    await appStepsService.LinkStepsAsync(appId, stepIds);
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
- [ ] Table shows ALL steps user has access to (linked + available)
- [ ] Linked steps have checkbox checked
- [ ] Available steps have checkbox unchecked
- [ ] Clicking checkbox links/unlinks the step immediately
- [ ] "Select all" checkbox in header works correctly
- [ ] Indeterminate state shows when some (not all) steps are linked
- [ ] Search filters steps by name, display name, and description
- [ ] Step count updates correctly ("X of Y steps")
- [ ] Linked count in header updates when linking/unlinking
- [ ] Edit button navigates to StepPage.razor
- [ ] Copy button appears only for linked steps
- [ ] Copy button copies correct `executeStep` code
- [ ] Table uses full page width on large screens
- [ ] Mobile responsive layout works

---

## Files Changed Summary

### Created
- `src/Automaton.Studio/Models/LinkedStepInfo.cs`
- `src/Automaton.Studio/Services/AppStepsService.cs`

### Modified
- `src/Automaton.Studio/Pages/Apps/AppPage.razor` - Added Backend Actions tab with inline table
- `src/Automaton.Studio/Pages/Apps/AppPage.razor.cs` - Added step loading, filtering, and toggle logic
- `src/Automaton.Studio/Program.cs` - Register AppStepsService
- `src/Automaton.Studio.Server/Controllers/AppsController.cs` - Added step endpoints

### Removed
- ~~`src/Automaton.Studio/Pages/Apps/Components/LinkStepDialog/LinkStepDialog.razor`~~ - Replaced by inline table
- ~~`src/Automaton.Studio/Pages/Apps/Components/LinkStepDialog/LinkStepDialog.razor.cs`~~
- ~~`src/Automaton.Studio/Pages/Apps/Components/LinkStepDialog/LinkStepDialogModel.cs`~~

---

## Next Steps

After Phase 2 is complete:
1. Proceed to [Phase 3: Unified AI System Prompt](./03-phase3-unified-ai-prompt.md)
2. This enables the "one command" generation experience
