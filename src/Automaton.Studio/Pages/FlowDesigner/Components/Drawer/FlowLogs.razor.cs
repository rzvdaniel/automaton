using Automaton.Core.Logs;
using Automaton.Studio.Domain;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using AntDesign;

namespace Automaton.Studio.Pages.FlowDesigner.Components.Drawer;

public partial class FlowLogs : ComponentBase, IDisposable
{
    private TextArea logsTextArea;
    private bool autoScroll = true;
    private DateTime? lastClearTime;
    private Timer? scrollTimer;

    [CascadingParameter]
    private StudioFlow Flow { get; set; }

    [Inject]
    public WorkflowLogsSink WorkflowSink { get; set; }

    [Inject]
    public IJSRuntime JSRuntime { get; set; }

    [Inject]
    public IMessageService MessageService { get; set; }

    protected override async Task OnInitializedAsync()
    {
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
        WorkflowSink?.ClearLogs();
        lastClearTime = DateTime.Now;
        StateHasChanged();
    }

    private async Task CopyLogs()
    {
        try
        {
            if (!string.IsNullOrEmpty(WorkflowSink?.Logs))
            {
                await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", WorkflowSink.Logs);
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

    private int GetLogLineCount()
    {
        if (string.IsNullOrEmpty(WorkflowSink?.Logs))
            return 0;
        
        return WorkflowSink.Logs.Split('\n').Length;
    }

    private int GetLogCharCount()
    {
        return WorkflowSink?.Logs?.Length ?? 0;
    }

    public void Dispose()
    {
        scrollTimer?.Dispose();
    }
}
