using AntDesign;
using Automaton.Core.Enums;
using Blazored.FluentValidation;

namespace Automaton.Studio.Pages.Flows.Designer.Components.NewVariable;

public partial class VariableDialog : FeedbackComponent<VariableModel>
{
    private VariableModel model;
    private FluentValidationValidator validator;
    private IEnumerable<VariableType> VariableTypes { get; } = Enum.GetValues<VariableType>();

    protected override void OnInitialized()
    {
        model = this.Options;
        base.OnInitialized();
    }

    public override async Task OnFeedbackOkAsync(ModalClosingEventArgs args)
    {
        // Below workaround is required to avoid NewWorkflow dialog
        // close unexpectedly when shown for the first time.
        // The fact that validation happens synchronous (while this method is async) may be the cause of the problem.
        // Please keep both of the next two lines, or find a fix for the issue.
        // Razvan, May 2021
        await InvokeAsync(StateHasChanged);
        await Task.Delay(50);

        // Cancel closing dialog if validation fails
        args.Cancel = !validator.Validate(options => options.IncludeAllRuleSets());
    }

    // ✅ Helper methods for value input
    private string GetValuePlaceholder(VariableModel variable)
    {
        return "Enter value (API key, text, etc.)...";
    }

    private bool IsLongValue(object value)
    {
        // Consider value "long" if it's a string with more than 100 characters
        if (value is string str)
        {
            return str.Length > 100;
        }
        return false;
    }
}
