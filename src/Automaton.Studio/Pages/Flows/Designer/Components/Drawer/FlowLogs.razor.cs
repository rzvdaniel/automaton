using Automaton.Studio.Domain;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using AntDesign;

namespace Automaton.Studio.Pages.Flows.Designer.Components.Drawer;

public partial class FlowLogs : ComponentBase, IDisposable
{
    private TextArea logsTextArea;
    private bool autoScroll = true;
    private Timer? scrollTimer;

    [CascadingParameter]
    private StudioFlow Flow { get; set; }

    [Inject]
    public WorkflowLogsManager WorkflowLogsManager { get; set; }

    [Inject]
    public IJSRuntime JSRuntime { get; set; }

    [Inject]
    public IMessageService MessageService { get; set; }

    protected override async Task OnInitializedAsync()
    {
        // Subscribe to logs updated events
        WorkflowLogsManager.LogsUpdated += OnLogsUpdated;
        
        await base.OnInitializedAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && autoScroll)
        {
            await ScrollToBottom();
        }
        await base.OnAfterRenderAsync(firstRender);
    }

    private async void OnLogsUpdated(object sender, LogsUpdatedEventArgs e)
    {
        // Update UI when new logs arrive
        await InvokeAsync(StateHasChanged);
        
        // Auto-scroll to bottom if enabled
        if (autoScroll)
        {
            await InvokeAsync(async () => await ScrollToBottom());
        }
    }

    private async Task ScrollToBottom()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("eval", "var element = document.getElementById('flow-logs-textarea'); if(element) { element.scrollTop = element.scrollHeight; }");
        }
        catch
        {
            // Ignore JS interop errors
        }
    }

    private void ClearLogs()
    {
        WorkflowLogsManager?.ClearLogs();
        StateHasChanged();
    }

    private async Task CopyLogs()
    {
        try
        {
            if (!string.IsNullOrEmpty(WorkflowLogsManager?.Logs))
            {
                await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", WorkflowLogsManager.Logs);
                MessageService.Success("Logs copied to clipboard", 2);
            }
            else
            {
                MessageService.Info("No logs to copy", 2);
            }
        }
        catch
        {
            MessageService.Error("Failed to copy logs to clipboard", 3);
        }
    }

    private async Task ToggleAutoScroll()
    {
        autoScroll = !autoScroll;
        
        if (autoScroll)
        {
            await ScrollToBottom();
            MessageService.Info("Auto-scroll enabled", 2);
        }
        else
        {
            MessageService.Info("Auto-scroll disabled", 2);
        }
        
        StateHasChanged();
    }

    public void Dispose()
    {
        scrollTimer?.Dispose();
        
        // Unsubscribe from events
        if (WorkflowLogsManager != null)
        {
            WorkflowLogsManager.LogsUpdated -= OnLogsUpdated;
        }
    }
}
