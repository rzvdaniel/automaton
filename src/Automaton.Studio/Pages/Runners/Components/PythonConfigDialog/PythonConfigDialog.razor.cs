using AntDesign;
using System.Threading.Tasks;

namespace Automaton.Studio.Pages.Runners.Components.PythonConfigDialog;

public partial class PythonConfigDialog : FeedbackComponent<PythonConfigModel>
{
    private PythonConfigModel model = new();

    protected override void OnInitialized()
    {
        model = this.Options ?? new PythonConfigModel();
        base.OnInitialized();
    }

    private void AddPath()
    {
        model.PythonPaths.Add(string.Empty);
        StateHasChanged();
    }

    private void RemovePath(int index)
    {
        if (index >= 0 && index < model.PythonPaths.Count)
        {
            model.PythonPaths.RemoveAt(index);
            StateHasChanged();
        }
    }

    private void UpdatePath(int index, string newValue)
    {
        if (index >= 0 && index < model.PythonPaths.Count)
        {
            model.PythonPaths[index] = newValue;
        }
    }

    public override async Task OnFeedbackOkAsync(ModalClosingEventArgs args)
    {
        // Allow a brief moment for the UI to update
        await InvokeAsync(StateHasChanged);
        await Task.Delay(50);

        // Remove empty paths before saving
        model.PythonPaths = model.PythonPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }
}
