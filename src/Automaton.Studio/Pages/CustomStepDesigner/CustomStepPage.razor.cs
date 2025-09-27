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
            }
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync(Resources.Errors.CustomStepUpdateFailed);
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

            await MessageService.SuccessAsync(Resources.Information.CustomStepExecutionSuccessful);
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync($"{Resources.Errors.CustomStepExecutionFailed}. Error: {ex.Message}");
        }
        finally
        {
            runningCode = false;
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
