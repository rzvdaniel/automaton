using AntDesign;
using Automaton.Core.Enums;
using Automaton.Core.Models;
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

    // Computed properties for Razor UI binding
    private bool HasSelectedRunners => selectedRunners.Any();
    private bool HasAvailableRunners => availableRunners.Any();
    private ExecutionCapabilities ExecutionCapabilities => DesignerViewModel.GetExecutionCapabilities(HasSelectedRunners);
    private string RunButtonDisabledMessage => DesignerViewModel.GetRunButtonDisabledMessage(HasSelectedRunners, HasAvailableRunners);

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
    [Inject] NavigationManager NavigationManager { get; set; } = default!;

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
        var capabilities = ExecutionCapabilities;

        if (!capabilities.CanExecute)
        {
            MessageService.Warning("Please select at least one runner to execute the flow.");
            return;
        }

        // Clear logs for new execution
        WorkflowLogsManager.ClearLogs();

        try
        {
            // Execute mutually exclusively - either locally OR on runners, not both
            if (capabilities.ShouldRunLocally)
            {
                var localExecution = await ExecuteLocally();
                await ShowLocalExecutionResult(localExecution);
            }
            else if (capabilities.ShouldRunOnRunners)
            {
                var runnerResults = await ExecuteOnRunners();
                await ShowRunnerExecutionResults(runnerResults);
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

    private async Task<WorkflowExecution> ExecuteLocally()
    {
        var execution = await DesignerViewModel.RunFlow();
        if (execution != null)
        {
            WorkflowLogsManager.SetCurrentExecution(execution.Id);
        }
        return execution;
    }

    private async Task<IEnumerable<RunnerFlowResult>> ExecuteOnRunners()
    {
        Guid.TryParse(FlowId, out var flowId);
        var results = await FlowService.Run(flowId, selectedRunners);
        return results;
    }

    private async Task ShowLocalExecutionResult(WorkflowExecution execution)
    {
        if (execution != null)
        {
            MessageService.Success($"Local execution completed with status: {execution.Status}", 5);
        }
        await Task.CompletedTask;
    }

    private async Task ShowRunnerExecutionResults(IEnumerable<RunnerFlowResult> results)
    {
        if (results != null)
        {
            var message = GetExecutionResultMessage(results);
            MessageService.Success(message, 5);
        }
        await Task.CompletedTask;
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

    private void OnRunnersSelectionChanged(IEnumerable<RunnerModel> runners)
    {
        selectedRunners = runners?.Select(x => x.Id) ?? [];
        StateHasChanged();
    }

    private string GetExecutionResultMessage(IEnumerable<RunnerFlowResult> results)
    {
        var successCount = results.Count(r => r.Status == WorkflowStatus.Success);
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

    private void OnRunButtonClickWhenDisabled()
    {
        var action = DesignerViewModel.GetRunButtonAction(HasSelectedRunners, HasAvailableRunners);
        
        switch (action)
        {
            case "download-runner":
                NavigateToDownloadRunner();
                break;
            case "select-runners":
                MessageService.Info("Please select one or more runners from the dropdown above to execute this workflow.", 3);
                break;
            default:
                MessageService.Warning(RunButtonDisabledMessage, 3);
                break;
        }
    }

    private void NavigateToDownloadRunner()
    {
        // Show informative modal with option to navigate to runner download
        var modal = ModalService.Info(new ConfirmOptions()
        {
            Title = "Install a Runner",
            Content = @"
                <div style='margin: 16px 0;'>
                    <p>To execute workflows from the web interface, you need to install and run an Automaton Runner on your local machine or server.</p>
                    <p><strong>The runner will:</strong></p>
                    <ul style='margin: 12px 0 12px 24px;'>
                        <li>Connect securely to your Automaton Studio</li>
                        <li>Execute workflows on your behalf</li>
                        <li>Provide access to local files and applications</li>
                    </ul>
                </div>",
            OkText = "Download Runner",
            CancelText = "Later",
            OnOk = (args) => 
            {
                NavigationManager.NavigateTo("/downloads-runner", true);
                return Task.CompletedTask;
            }
        });
    }
}
