using AntDesign;
using Automaton.Core.Enums;
using Blazored.FluentValidation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Rendering;

namespace Automaton.Studio.Pages.Flows.Designer.Components.NewVariable;

public partial class VariableDialog : FeedbackComponent<VariableModel>
{
    private VariableModel model;
    private FluentValidationValidator validator;
    private IEnumerable<VariableType> VariableTypes { get; } = Enum.GetValues<VariableType>();
    private InputPassword _inputPassword;
    private bool passwordVisible = false;

    // Attributes for password visibility toggle icon
    private Dictionary<string, object> passwordIconAttributes = new()
    {
        ["Class"] = "custom-password-icon",
        ["Type"] = IconType.Outline.EyeInvisible,
        ["Theme"] = IconThemeType.Outline
    };

    private Dictionary<string, object> hideAttributes = new()
    {
        ["Type"] = IconType.Outline.EyeInvisible,
        ["Style"] = ""
    };

    private Dictionary<string, object> showAttributes = new()
    {
        ["Type"] = IconType.Outline.Eye,
        ["Style"] = "color: #1890ff"
    };

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
        if (variable.IsSensitive)
        {
            return "Enter sensitive value (password, API key, token, etc.)...";
        }
        return "Enter value (text, number, JSON, etc.)...";
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

    // ✅ Simple password icon render - adapted for code-behind
    private RenderFragment PasswordIconRender(Func<Task> clickActionAsync, Dictionary<string, object> iconAttributes)
    {
        return builder =>
        {
            builder.OpenComponent<Icon>(0);
            builder.AddMultipleAttributes(1, iconAttributes);
            builder.AddAttribute(2, "OnClick", EventCallback.Factory.Create<MouseEventArgs>(this, clickActionAsync));
            builder.CloseComponent();
        };
    }

    private async Task TogglePasswordVisibility()
    {
        passwordVisible = !passwordVisible;
        passwordIconAttributes = passwordVisible ? showAttributes : hideAttributes;
        
        if (_inputPassword != null)
        {
            await _inputPassword.Focus();
        }
        
        StateHasChanged();
    }
}
