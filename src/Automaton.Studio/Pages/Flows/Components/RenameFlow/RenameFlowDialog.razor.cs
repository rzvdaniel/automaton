using AntDesign;
using Blazored.FluentValidation;
using System.Threading.Tasks;

namespace Automaton.Studio.Pages.Flows.Components.RenameFlow;

public partial class RenameFlowDialog : FeedbackComponent<RenameFlowModel>
{
    private Form<RenameFlowModel> form;
    private RenameFlowModel flowModel;
    private FluentValidationValidator fluentValidationValidator;

    protected override void OnInitialized()
    {
        flowModel = this.Options;
        base.OnInitialized();
    }

    public override async Task OnFeedbackOkAsync(ModalClosingEventArgs args)
    {
        await InvokeAsync(StateHasChanged);
        await Task.Delay(50);

        args.Cancel = !fluentValidationValidator.Validate(options => options.IncludeAllRuleSets());
    }
}
