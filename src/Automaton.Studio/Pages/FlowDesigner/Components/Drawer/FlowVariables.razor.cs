using AntDesign;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Studio.Domain;
using Automaton.Studio.Pages.FlowDesigner.Components.NewVariable;
using Automaton.Studio.Pages.FlowDesigner.Components.ViewVariable;
using Automaton.Studio.Resources;
using Blazored.FluentValidation;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.FlowDesigner.Components.Drawer;

public partial class FlowVariables : ComponentBase
{
    private FluentValidationValidator fluentValidationValidator;
    private string selectedVariableType = "Flow";

    [CascadingParameter] StudioFlow Flow { get; set; }
    [Inject] ModalService ModalService { get; set; } = default!;

    public IEnumerable<VariableType> VariableTypes { get; } = Enum.GetValues<VariableType>();

    private IEnumerable<StepVariable> Variables
    {
        get
        {
            return Flow.Variables.Select(x => new StepVariable
            {
                Id = x.Key,
                Name = x.Value.Name,
                Value = x.Value.Value,
                Description = x.Value.Description
            }).OrderBy(x => x.Name);
        }
    }

    private IEnumerable<StepVariable> InputVariables
    {
        get
        {
            return Flow.InputVariables.Select(x => new StepVariable
            {
                Id = x.Key,
                Name = x.Value.Name,
                Value = x.Value.Value,
                Description = x.Value.Description
            }).OrderBy(x => x.Name);
        }
    }

    private IEnumerable<StepVariable> OutputVariables
    {
        get
        {
            return Flow.OutputVariables.Select(x => new StepVariable
            {
                Id = x.Key,
                Name = x.Value.Name,
                Value = x.Value.Value,
                Description = x.Value.Description
            }).OrderBy(x => x.Name);
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
    }

    private void OnVariableTypeChange(string value)
    {
        selectedVariableType = value;
        StateHasChanged();
    }

    private async Task AddVariable()
    {
        if (selectedVariableType == "Input")
        {
            await AddInputVariable();
        }
        else if (selectedVariableType == "Output")
        {
            await AddOutputVariable();
        }
    }

    public async Task ViewVariable(StepVariable variable)
    {
        var viewVariableModel = new ViewVariableModel
        {
            Variable = variable
        };

        await ModalService.CreateModalAsync<ViewVariableDialog, ViewVariableModel>
        (
            new ModalOptions { Title = Labels.Variable }, viewVariableModel
        );
    }

    public async Task AddInputVariable()
    {
        var newVariable = new VariableModel
        {
            ExistingNames = Flow.GetInputVariableNames()
        };

        var newVariableDialog = await ModalService.CreateModalAsync<VariableDialog, VariableModel>
        (
            new ModalOptions { Title = "Input Variable" }, newVariable
        );

        newVariableDialog.OnOk = () =>
        {
            Flow.SetInputVariable(new StepVariable 
            { 
                Id = Guid.NewGuid().ToString(),
                Name = newVariable.Name, 
                Value = newVariable.Value,
                Type = newVariable.Type,
                Description = newVariable.Description
            });

            StateHasChanged();
            return Task.CompletedTask;
        };
    }

    public async Task EditInputVariable(StepVariable variable)
    {
        var inputVariableNames = Flow.GetInputVariableNames();
        var existingInputVariables = inputVariableNames.Where(x => !x.Equals(variable.Name, StringComparison.OrdinalIgnoreCase));

        var updatedVariable = new VariableModel
        {
            ExistingNames = existingInputVariables,
            Name = variable.Name,
            Type = variable.Type,
            Value = variable.Value,
            Description = variable.Description
        };

        var editVariableDialog = await ModalService.CreateModalAsync<VariableDialog, VariableModel>
        (
            new ModalOptions { Title = "Edit Input Variable" }, updatedVariable
        );

        editVariableDialog.OnOk = () =>
        {
            Flow.SetInputVariable(new StepVariable 
            { 
                Id = variable.Id,
                Name = updatedVariable.Name, 
                Value = updatedVariable.Value,
                Type = updatedVariable.Type,
                Description = updatedVariable.Description
            });

            StateHasChanged();
            return Task.CompletedTask;
        };
    }

    public async Task DeleteInputVariable(StepVariable variable)
    {
        var deleteConfirm = await ModalService.ConfirmAsync(new ConfirmOptions()
        {
            Title = "Delete Input Variable",
            Content = $"Are you sure you want to delete the input variable '{variable.Name}'?",
            OkText = "Delete",
            CancelText = "Cancel",
            OkType = ButtonType.Primary
        });

        if (deleteConfirm)
        {
            Flow.DeleteInputVariable(variable.Id);
            StateHasChanged();
        }
    }

    public async Task AddOutputVariable()
    {
        var newVariable = new VariableModel
        {
            ExistingNames = Flow.GetOutputVariableNames()
        };

        var newVariableDialog = await ModalService.CreateModalAsync<VariableDialog, VariableModel>
        (
            new ModalOptions { Title = "Output Variable" }, newVariable
        );

        newVariableDialog.OnOk = () =>
        {
            Flow.SetOutputVariable(new StepVariable 
            { 
                Id = Guid.NewGuid().ToString(),
                Name = newVariable.Name, 
                Value = newVariable.Value,
                Type = newVariable.Type,
                Description = newVariable.Description
            });

            StateHasChanged();
            return Task.CompletedTask;
        };
    }

    public async Task EditOutputVariable(StepVariable variable)
    {
        var outputVariableNames = Flow.GetOutputVariableNames();
        var existingOutputVariables = outputVariableNames.Where(x => !x.Equals(variable.Name, StringComparison.OrdinalIgnoreCase));

        var updatedVariable = new VariableModel
        {
            ExistingNames = existingOutputVariables,
            Name = variable.Name,
            Type = variable.Type,
            Value = variable.Value,
            Description = variable.Description
        };

        var editVariableDialog = await ModalService.CreateModalAsync<VariableDialog, VariableModel>
        (
            new ModalOptions { Title = "Edit Output Variable" }, updatedVariable
        );

        editVariableDialog.OnOk = () =>
        {
            Flow.SetOutputVariable(new StepVariable 
            { 
                Id = variable.Id,
                Name = updatedVariable.Name, 
                Value = updatedVariable.Value,
                Type = updatedVariable.Type,
                Description = updatedVariable.Description
            });

            StateHasChanged();
            return Task.CompletedTask;
        };
    }

    public async Task DeleteOutputVariable(StepVariable variable)
    {
        var deleteConfirm = await ModalService.ConfirmAsync(new ConfirmOptions()
        {
            Title = "Delete Output Variable",
            Content = $"Are you sure you want to delete the output variable '{variable.Name}'?",
            OkText = "Delete",
            CancelText = "Cancel",
            OkType = ButtonType.Primary
        });

        if (deleteConfirm)
        {
            Flow.DeleteOutputVariable(variable.Id);
            StateHasChanged();
        }
    }
}
