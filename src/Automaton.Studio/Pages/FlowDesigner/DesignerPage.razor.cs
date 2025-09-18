using AntDesign;
using Automaton.Core.Enums;
using Automaton.Studio.Domain;
using Automaton.Studio.Events;
using Automaton.Studio.Extensions;
using Automaton.Studio.Models;
using Automaton.Studio.Pages.FlowDesigner.Components.NewDefinition;
using Automaton.Studio.Resources;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.FlowDesigner;

partial class DesignerPage : ComponentBase
{
    private const string ZeroPercent = "0%";
    private const string ThirtyPercent = "30%";
    private const string SeventyPercent = "70%";
    private const string HundredPercent = "100%";

    private Designer designer;
    private string activeTabKey = "designer";
    private bool isStepCreatedHandlerAttached;
    private StudioStep selectedStep;
    private IEnumerable<RunnerModel> availableRunners = new List<RunnerModel>();
    private IEnumerable<Guid> selectedRunners = new List<Guid>();
    private bool isLoadingRunners = false;
    private string executionMode = "debug"; // "debug" or "runner"

    private string TopSplitterPercentage => selectedStep != null ? SeventyPercent : HundredPercent;
    private string BottomSplitterPercentage => selectedStep != null ? ThirtyPercent : ZeroPercent;

    [Parameter] public string FlowId { get; set; }
    [Parameter] public string FlowName { get; set; }

    [Inject] IMediator mediator { get; set; }
    [Inject] ICourier Courier { get; set; }
    [Inject] ModalService ModalService { get; set; } = default!;
    [Inject] DesignerViewModel DesignerViewModel { get; set; } = default!;
    [Inject] WorkflowLogsManager WorkflowLogsManager { get; set; } = default!;
    [Inject] RunnerService RunnerService { get; set; } = default!;
    [Inject] FlowService FlowService { get; set; } = default!;
    [Inject] IMessageService MessageService { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Courier.Subscribe<ExecuteStepNotification>(HandleExecuteStepNotification);
        Courier.Subscribe<FlowUpdateNotification>(HandleFlowUpdateNotification);
        Courier.Subscribe<VariableUpdateNotification>(HandleVariableUpdateNotification);
        Courier.Subscribe<LogUpdateNotification>(HandleLogUpdateNotification);

        await LoadFlow();
        await LoadRunners();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!isStepCreatedHandlerAttached && designer != null)
        {
            DesignerViewModel.StepCreated += OnStepCreated;
            isStepCreatedHandlerAttached = true;
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    public async Task RunFlow()
    {
        // Clear logs for new execution
        WorkflowLogsManager.ClearLogs();

        try
        {
            if (executionMode == "debug")
            {
                // Debug mode - execute locally in Studio
                var execution = await DesignerViewModel.RunFlow();
                if (execution != null)
                {
                    WorkflowLogsManager.SetCurrentExecution(execution.Id);
                }
            }
            else
            {
                // Runner mode - execute on selected runners
                if (!selectedRunners.Any())
                {
                    MessageService.Warning("Please select at least one runner to execute the flow.");
                    return;
                }

                Guid.TryParse(FlowId, out var flowId);
                var results = await FlowService.Run(flowId, selectedRunners);

                // Show execution results
                var message = GetExecutionResultMessage(results);
                MessageService.Success(message, 5);
            }
        }
        catch (Exception ex)
        {
            MessageService.Error($"Failed to execute flow: {ex.Message}", 5);
        }
    }

    public void StopFlow()
    {
        DesignerViewModel.StopFlow();
    }

    private async Task LoadRunners()
    {
        try
        {
            isLoadingRunners = true;
            StateHasChanged();
            
            availableRunners = await RunnerService.List();
        }
        catch (Exception ex)
        {
            MessageService.Error($"Failed to load runners: {ex.Message}");
            availableRunners = new List<RunnerModel>();
        }
        finally
        {
            isLoadingRunners = false;
            StateHasChanged();
        }
    }

    private void OnExecutionModeChange(string mode)
    {
        executionMode = mode;
        StateHasChanged();
    }

    private void OnRunnersSelectionChanged(IEnumerable<Guid> runners)
    {
        selectedRunners = runners ?? new List<Guid>();
        StateHasChanged();
    }

    private string GetExecutionResultMessage(IEnumerable<RunnerFlowResult> results)
    {
        var successCount = results.Count(r => r.Status == Core.Enums.WorkflowStatus.Success);
        var totalCount = results.Count();
        
        if (successCount == totalCount)
        {
            return $"Flow executed successfully on all {totalCount} runner(s)";
        }
        else if (successCount > 0)
        {
            return $"Flow executed successfully on {successCount} of {totalCount} runner(s)";
        }
        else
        {
            return $"Flow execution failed on all {totalCount} runner(s)";
        }
    }

    private void OnStepCreated(object sender, StepEventArgs e)
    {
        designer.SetActiveStep(e.Step);
    }

    private async Task OnItemDrop(StudioStep step)
    {
        if (step.IsNew)
        {
            await AddStepDialog(step);
        }
        else
        {
            DesignerViewModel.UpdateStepConnections();
        }
    }

    private async Task OnItemDoubleClick(StudioStep step)
    {
        if (!step.HasProperties)
        {
            return;
        }

        selectedStep = step;
        StateHasChanged();
    }

    private void ClosePropertiesPanel()
    {
        selectedStep = null;
        StateHasChanged();
    }

    private void OnStepDeleted(object sender, StepEventArgs e)
    {
        if (selectedStep?.Id == e.Step.Id)
        {
            selectedStep = null;
        }
        StateHasChanged();
    }

    private async Task LoadFlow()
    {
        Guid.TryParse(FlowId, out var flowId);

        await DesignerViewModel.LoadFlow(flowId);
    }

    private async Task SaveFlow()
    {
        await DesignerViewModel.SaveFlow();
    }

    private async Task AddStepDialog(StudioStep step)
    {
        if (!step.HasProperties)
        {
            designer.CompleteStep(step);

            await InvokeAsync(StateHasChanged);

            return;
        }

        // For new steps, still use modal dialog during creation
        var result = await step.DisplayPropertiesDialog(ModalService);

        result.OnOk = async () =>
        {
            designer.CompleteStep(step);

            await InvokeAsync(StateHasChanged);
        };

        result.OnCancel = async () =>
        {
            DesignerViewModel.DeleteStep(step);

            await InvokeAsync(StateHasChanged);
        };
    }

    private void OnDefinitionAddClick()
    {
        var newDefinitionModel = new NewDefinitionModel
        {
            ExistingNames = DesignerViewModel.GetDefinitionNames()
        };

        var newDefinitionDialog = ModalService.CreateModal<NewDefinitionDialog, NewDefinitionModel>
        (
            new ModalOptions { Title = Labels.DefinitionName }, newDefinitionModel
        );

        newDefinitionDialog.OnOk = () =>
        {
            var definition = DesignerViewModel.CreateDefinition(newDefinitionModel.Name);
            DesignerViewModel.SetActiveDefinition(definition);

            StateHasChanged();

            return Task.CompletedTask;
        };
    }

    private void OnTabClick(string key)
    {
        DesignerViewModel.SetActiveDefinition(key);
    }

    private void OnTabClose(string key)
    {
    }

    private void HandleExecuteStepNotification(ExecuteStepNotification notification, CancellationToken cancellationToken)
    {
        if(cancellationToken.IsCancellationRequested) 
            return;

        DesignerViewModel.SetExecutingStep(notification.StepId);

        InvokeAsync(StateHasChanged);
    }

    private void HandleFlowUpdateNotification(FlowUpdateNotification notification, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        InvokeAsync(StateHasChanged);
    }

    private void HandleVariableUpdateNotification(VariableUpdateNotification notification, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        DesignerViewModel.Flow.Variables[notification.Variable.Id] = notification.Variable;

        InvokeAsync(StateHasChanged);
    }

    private void HandleLogUpdateNotification(LogUpdateNotification notification, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        // The WorkflowLogsManager will be updated via the LogUpdateNotificationHandler
        // We just need to trigger a UI refresh
        InvokeAsync(StateHasChanged);
    }

    private void OnTabChange(string key)
    {
        activeTabKey = key;
    }

    private string GetRunnerStatusIcon(RunnerStatus status)
    {
        return status switch
        {
            RunnerStatus.Online => "check-circle",
            RunnerStatus.Offline => "close-circle",
            _ => "question-circle"
        };
    }

    private string GetRunnerStatusClass(RunnerStatus status)
    {
        return $"runner-status-icon runner-status-{status.ToString().ToLower()}";
    }

    public void Dispose()
    {
        Courier.UnSubscribe<ExecuteStepNotification>(HandleExecuteStepNotification);
        Courier.UnSubscribe<FlowUpdateNotification>(HandleFlowUpdateNotification);
        Courier.UnSubscribe<VariableUpdateNotification>(HandleVariableUpdateNotification);
        Courier.UnSubscribe<LogUpdateNotification>(HandleLogUpdateNotification);
    }
}
