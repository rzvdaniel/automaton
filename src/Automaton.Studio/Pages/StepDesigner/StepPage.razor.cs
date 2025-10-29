using AntDesign;
using Automaton.Core.Enums;
using Automaton.Studio.Domain;
using Automaton.Studio.Steps.Custom.Variables;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using BlazorMonaco.Editor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Automaton.Studio.Pages.StepDesigner;

partial class StepPage : ComponentBase, IDisposable
{
    private bool loading = false;
    private bool runningCode = false;
    private bool autoScroll = true;
    private IEnumerable<RunnerModel> availableRunners = new List<RunnerModel>();
    private IEnumerable<Guid> selectedRunners = new List<Guid>();
    private bool isLoadingRunners = false;

    private Form<CustomStep> form;
    private Form<CustomStep> codeForm;
    private StandaloneCodeEditor codeEditor;
    private TextArea logsTextArea;
    
    public IEnumerable<VariableType> VariableTypes { get; } = Enum.GetValues<VariableType>();

    private static readonly List<string> CustomStepLanguages =
    [
        CustomStepLanguage.Python,
        CustomStepLanguage.Batch
    ];

    // Computed properties for Razor UI binding
    private bool HasSelectedRunners => selectedRunners.Any();
    private bool HasAvailableRunners => availableRunners.Any();
    private ExecutionCapabilities ExecutionCapabilities => StepDesignerViewModel.GetExecutionCapabilities(HasSelectedRunners);
    private string RunButtonDisabledMessage => StepDesignerViewModel.GetRunButtonDisabledMessage(HasSelectedRunners, HasAvailableRunners);

    [Inject] private StepViewModel StepDesignerViewModel { get; set; }
    [Inject] private MessageService MessageService { get; set; }
    [Inject] private AiCodeCleanupService CodeCleanupService { get; set; }
    [Inject] private IJSRuntime JSRuntime { get; set; }
    [Inject] private ICurrentStepContextService CurrentStepContextService { get; set; }
    [Inject] private RunnerService RunnerService { get; set; }
    [Inject] private NavigationManager NavigationManager { get; set; }

    [Parameter] public string StepId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        StepDesignerViewModel.ScriptTextWritten += OnScriptOutputWritten;
        CurrentStepContextService.CodeChanged += OnCodeChangedByMcpTools;

        await StepDesignerViewModel.Load(Guid.Parse(StepId));
        await LoadRunners();

        // Register current step with the context service
        CurrentStepContextService.SetCurrentStep(
            StepDesignerViewModel.CustomStep,
            GetCurrentCodeForAI);

        // Clear logs when entering the step
        StepDesignerViewModel.ClearScriptOutputText();

        await base.OnInitializedAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && autoScroll && !string.IsNullOrEmpty(StepDesignerViewModel.ScriptOutput?.ToString()))
        {
            await ScrollToBottom();
        }
        await base.OnAfterRenderAsync(firstRender);
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
            await MessageService.ErrorAsync($"Failed to load runners: {ex.Message}");
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

    public void AddInputVariable()
    {
        StepDesignerViewModel.AddInputVariable();
    }

    public void DeleteInputVariable(string name)
    {
        StepDesignerViewModel.DeleteInputVariable(name);
    }

    public void AddOutputVariable()
    {
        StepDesignerViewModel.AddOutputVariable();
    }

    public void DeleteOutputVariable(string name)
    {
        StepDesignerViewModel.DeleteOutputVariable(name);
    }

    private void ClearLogs()
    {
        StepDesignerViewModel.ClearScriptOutputText();
        _ = MessageService.InfoAsync("Logs cleared", 2);
    }

    private async Task CopyLogs()
    {
        try
        {
            var logs = StepDesignerViewModel.ScriptOutput?.ToString();
            if (!string.IsNullOrEmpty(logs))
            {
                await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", logs);
                await MessageService.SuccessAsync("Logs copied to clipboard", 2);
            }
            else
            {
                await MessageService.InfoAsync("No logs to copy", 2);
            }
        }
        catch
        {
            await MessageService.ErrorAsync("Failed to copy logs to clipboard", 3);
        }
    }

    private async Task ToggleAutoScroll()
    {
        autoScroll = !autoScroll;
        
        if (autoScroll)
        {
            await ScrollToBottom();
            await MessageService.InfoAsync("Auto-scroll enabled", 2);
        }
        else
        {
            await MessageService.InfoAsync("Auto-scroll disabled", 2);
        }
        
        StateHasChanged();
    }

    private async Task ScrollToBottom()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("eval", "var element = document.getElementById('step-logs-textarea'); if(element) { element.scrollTop = element.scrollHeight; }");
        }
        catch
        {
            // Ignore JS interop errors
        }
    }

    private async Task Save()
    {
        try
        {
            loading = true;

            if (form.Validate())
            {
                await RefreshCode();

                // Increment version when saving changes to the custom step template
                StepDesignerViewModel.CustomStep.Definition.Version++;

                await StepDesignerViewModel.Save();
                
                // Show success notification without awaiting (fire and forget)
                _ = MessageService.SuccessAsync($"Step saved successfully (v{StepDesignerViewModel.CustomStep.Definition.Version})");
            }
        }
        catch (Exception ex)
        {
            // Show error notification without awaiting (fire and forget)
            _ = MessageService.ErrorAsync(Resources.Errors.CustomStepUpdateFailed);
        }
        finally
        {
            loading = false;
        }
    }

    private async Task Run()
    {
        var capabilities = ExecutionCapabilities;

        if (!capabilities.CanExecute)
        {
            await MessageService.WarningAsync("Please select at least one runner to execute the step.");
            return;
        }

        try
        {
            runningCode = true;
            await Task.Delay(100);
            
            // Refresh code from Monaco editor to view model
            await RefreshCode();

            // Auto-save before execution to ensure runners get the latest code
            // This prevents executing outdated code from the database
            if (form.Validate())
            {
                // Increment version when auto-saving before execution
                StepDesignerViewModel.CustomStep.Definition.Version++;
                
                await StepDesignerViewModel.Save();
                
                // Show subtle notification that code was auto-saved
                _ = MessageService.InfoAsync($"Step auto-saved before execution (v{StepDesignerViewModel.CustomStep.Definition.Version})", 2);
            }
            else
            {
                // If validation fails, still continue with execution using current code
                // This allows users to test invalid configurations if needed
                _ = MessageService.WarningAsync("Validation warnings detected, but continuing with execution...", 2);
            }

            // Execute mutually exclusively - either locally OR on runners, not both
            if (capabilities.ShouldRunLocally)
            {
                await ExecuteLocally();
            }
            else if (capabilities.ShouldRunOnRunners)
            {
                await ExecuteOnRunners();
            }

            runningCode = false;
            _ = MessageService.SuccessAsync(Resources.Information.CustomStepExecutionSuccessful);
        }
        catch (Exception ex)
        {
            runningCode = false;
            _ = MessageService.ErrorAsync($"{Resources.Errors.CustomStepExecutionFailed}. Error: {ex.Message}");
        }
    }

    private async Task ExecuteLocally()
    {
        StepDesignerViewModel.Execute();
        await Task.CompletedTask;
    }

    private async Task ExecuteOnRunners()
    {
        try
        {
            var results = await StepDesignerViewModel.ExecuteOnRunners(selectedRunners);
            var resultsList = results.ToList();
            
            // Small delay to ensure DOM updates before auto-scrolling
            await Task.Delay(100);
            if (autoScroll)
            {
                await ScrollToBottom();
            }

            var successCount = resultsList.Count(r => r.Success);
            var failureCount = resultsList.Count(r => !r.Success);
            var totalCount = resultsList.Count;

            if (successCount == totalCount)
            {
                // All runners succeeded
                await MessageService.SuccessAsync(
                    $"Custom step executed successfully on all {totalCount} runner(s)!", 
                    3);
            }
            else if (successCount > 0)
            {
                // Some runners succeeded, some failed
                await MessageService.WarningAsync(
                    $"Custom step completed on {successCount} of {totalCount} runner(s). {failureCount} runner(s) failed. Check logs for details.", 
                    5);
            }
            else
            {
                // All runners failed
                var errorMessages = resultsList
                    .Where(r => !string.IsNullOrEmpty(r.Error))
                    .Select(r => r.Error)
                    .Take(3)
                    .ToList();

                var errorSummary = errorMessages.Any() 
                    ? $"Errors: {string.Join("; ", errorMessages)}" 
                    : "Check logs for error details.";

                await MessageService.ErrorAsync(
                    $"Custom step execution failed on all {totalCount} runner(s). {errorSummary}", 
                    5);
            }
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync(
                $"Failed to execute custom step on runners: {ex.Message}. Please check runner connectivity and try again.", 
                5);
        }
    }

    private async Task RefreshCode()
    {
        StepDesignerViewModel.CustomStep.Definition.Code = await codeEditor.GetValue();
    }

    private async Task OnCodeGenerated(string generatedCode)
    {
        try
        {
            // Clean up the generated code by removing markdown code block syntax
            var cleanedCode = CodeCleanupService.CleanupGeneratedCode(generatedCode);
            
            // Set the cleaned code in the Monaco editor
            await codeEditor.SetValue(cleanedCode);
            
            // Update the view model
            StepDesignerViewModel.CustomStep.Definition.Code = cleanedCode;
            
            // Show success message
            await MessageService.SuccessAsync("Code generated successfully! You can now review and modify it.");
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync($"Failed to update code editor: {ex.Message}");
        }
    }

    // Convert StepVariable to VariableInfo for AI chat panel
    private List<VariableInfo> GetInputVariablesForAI()
    {
        return StepDesignerViewModel.CustomStep.Definition.CodeInputVariables?
            .Select(v => new VariableInfo
            {
                Name = v.Name ?? string.Empty,
                Type = v.Type.ToString(),
                Description = v.Description ?? string.Empty
            }).ToList() ?? new List<VariableInfo>();
    }

    private List<VariableInfo> GetOutputVariablesForAI()
    {
        return StepDesignerViewModel.CustomStep.Definition.CodeOutputVariables?
            .Select(v => new VariableInfo
            {
                Name = v.Name ?? string.Empty,
                Type = v.Type.ToString(),
                Description = v.Description ?? string.Empty
            }).ToList() ?? new List<VariableInfo>();
    }

    private async Task<string> GetCurrentCodeForAI()
    {
        try
        {
            // Get current code from Monaco editor
            if (codeEditor != null)
            {
                return await codeEditor.GetValue() ?? string.Empty;
            }
            // Fallback to view model if editor not available
            return StepDesignerViewModel.CustomStep.Definition.Code ?? string.Empty;
        }
        catch
        {
            // Fallback to view model code if Monaco editor fails
            return StepDesignerViewModel.CustomStep.Definition.Code ?? string.Empty;
        }
    }

    private Type GetVariableComponent(VariableType type)
    {
        return type switch
        {
            _ => typeof(StringVariable)
        };
    }

    private Dictionary<string, object> GetVariableComponentParameters(Core.Models.StepVariable customStepVariable)
    {
        return new Dictionary<string, object>() { { "Variable", customStepVariable } };
    }

    private async void OnCodeChangedByMcpTools(object? sender, CodeChangedEventArgs e)
    {
        try
        {
            // Update Monaco editor with the new code saved by MCP tools
            if (codeEditor != null && !string.IsNullOrEmpty(e.NewCode))
            {
                await InvokeAsync(async () =>
                {
                    await codeEditor.SetValue(e.NewCode);
                    StateHasChanged();
                });
            }
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync($"Error updating editor: {ex.Message}");
        }
    }

    private async void OnScriptOutputWritten(object sender, EventArgs e)
    {
        await InvokeAsync(StateHasChanged);
        
        // Auto-scroll to bottom if enabled
        if (autoScroll)
        {
            await InvokeAsync(async () => await ScrollToBottom());
        }
    }

    private void OnRunButtonClickWhenDisabled()
    {
        var action = StepDesignerViewModel.GetRunButtonAction(HasSelectedRunners, HasAvailableRunners);
        
        switch (action)
        {
            case "download-runner":
                NavigateToDownloadRunner();
                break;
            case "select-runners":
                _ = MessageService.InfoAsync("Please select one or more runners from the dropdown to execute this step.", 3);
                break;
            default:
                _ = MessageService.WarningAsync(RunButtonDisabledMessage, 3);
                break;
        }
    }

    private void NavigateToDownloadRunner()
    {
        NavigationManager.NavigateTo("/downloads-runner", true);
    }

    private StandaloneEditorConstructionOptions EditorConstructionOptions(StandaloneCodeEditor editor)
    {
        return new StandaloneEditorConstructionOptions
        {
            AutomaticLayout = true,
            Language = StepDesignerViewModel.CustomStep.Definition.Language.ToLower(),
            Value = StepDesignerViewModel.CustomStep.Definition.Code ?? string.Empty
        };
    }

    public void Dispose()
    {
        StepDesignerViewModel.ScriptTextWritten -= OnScriptOutputWritten;
        CurrentStepContextService.CodeChanged -= OnCodeChangedByMcpTools;
    }
}
