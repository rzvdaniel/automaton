using AntDesign;
using Automaton.Core.Enums;
using Automaton.Studio.Domain;
using Automaton.Studio.Steps.Custom.Variables;
using BlazorMonaco.Editor;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.CustomStepDesigner;

partial class CustomStepPage : ComponentBase
{
    private bool loading = false;
    private bool runningCode = false;

    private Form<CustomStep> form;
    private Form<CustomStep> codeForm;
    private StandaloneCodeEditor codeEditor;

    public IEnumerable<VariableType> VariableTypes { get; } = Enum.GetValues<VariableType>();

    private static readonly List<string> CustomStepLanguages =
    [
        Automaton.Steps.CustomStepLanguage.Python,
        Automaton.Steps.CustomStepLanguage.Batch
    ];

    [Inject] private CustomStepViewModel StepDesignerViewModel { get; set; }
    [Inject] private MessageService MessageService { get; set; }

    [Parameter] public string StepId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        StepDesignerViewModel.ScriptTextWritten += OnScriptOutputWritten;

        await StepDesignerViewModel.Load(Guid.Parse(StepId));

        await base.OnInitializedAsync();
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

    private void OnScriptOutputWritten(object sender, EventArgs e)
    {
        InvokeAsync(StateHasChanged);
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
