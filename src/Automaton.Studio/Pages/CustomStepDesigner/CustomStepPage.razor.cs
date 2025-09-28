using AntDesign;
using Automaton.Core.Enums;
using Automaton.Studio.Domain;
using Automaton.Studio.Steps.Custom.Variables;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using BlazorMonaco.Editor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Linq;

namespace Automaton.Studio.Pages.CustomStepDesigner;

partial class CustomStepPage : ComponentBase, IDisposable
{
    private bool loading = false;
    private bool runningCode = false;
    private bool autoScroll = true;

    private Form<CustomStep> form;
    private Form<CustomStep> codeForm;
    private StandaloneCodeEditor codeEditor;
    private TextArea logsTextArea;
    
    public IEnumerable<VariableType> VariableTypes { get; } = Enum.GetValues<VariableType>();

    private static readonly List<string> CustomStepLanguages =
    [
        Automaton.Steps.CustomStepLanguage.Python,
        Automaton.Steps.CustomStepLanguage.Batch
    ];

    [Inject] private CustomStepViewModel StepDesignerViewModel { get; set; }
    [Inject] private MessageService MessageService { get; set; }
    [Inject] private AiCodeCleanupService CodeCleanupService { get; set; }
    [Inject] private IJSRuntime JSRuntime { get; set; }

    [Parameter] public string StepId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        StepDesignerViewModel.ScriptTextWritten += OnScriptOutputWritten;

        await StepDesignerViewModel.Load(Guid.Parse(StepId));

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
        MessageService.Info("Logs cleared", 2);
    }

    private async Task CopyLogs()
    {
        try
        {
            var logs = StepDesignerViewModel.ScriptOutput?.ToString();
            if (!string.IsNullOrEmpty(logs))
            {
                await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", logs);
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

                await StepDesignerViewModel.Save();
                
                // Show success notification without awaiting (fire and forget)
                _ = MessageService.SuccessAsync("Step saved successfully");
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
        try
        {
            runningCode = true;

            await Task.Delay(100);

            await RefreshCode();

            // Don't clear logs here - let them accumulate
            StepDesignerViewModel.Execute();

            // Enable the button immediately after execution completes
            runningCode = false;

            // Show notification without awaiting it (fire and forget)
            _ = MessageService.SuccessAsync(Resources.Information.CustomStepExecutionSuccessful);
        }
        catch (Exception ex)
        {
            runningCode = false; // Also enable button immediately on error
            
            // Show error notification without awaiting it
            _ = MessageService.ErrorAsync($"{Resources.Errors.CustomStepExecutionFailed}. Error: {ex.Message}");
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
            VariableType.String => typeof(StringVariable),
            VariableType.Text => typeof(TextVariable),
            VariableType.Boolean => typeof(BooleanVariable),
            VariableType.Number => typeof(NumericVariable),
            VariableType.Date => typeof(DateVariable),
            _ => throw new NotImplementedException()
        };
    }

    private Dictionary<string, object> GetVariableComponentParameters(Core.Models.StepVariable customStepVariable)
    {
        return new Dictionary<string, object>() { { "Variable", customStepVariable } };
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

    private StandaloneEditorConstructionOptions EditorConstructionOptions(StandaloneCodeEditor editor)
    {
        return new StandaloneEditorConstructionOptions
        {
            AutomaticLayout = true,
            Language = StepDesignerViewModel.CustomStep.Definition.Language.ToLower(),
            Value = StepDesignerViewModel.CustomStep.Definition.Code
        };
    }

    public void Dispose()
    {
        StepDesignerViewModel.ScriptTextWritten -= OnScriptOutputWritten;
    }
}
