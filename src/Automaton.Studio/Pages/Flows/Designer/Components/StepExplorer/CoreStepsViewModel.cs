using AntDesign;
using Automaton.Studio.Factories;
using Automaton.Studio.Pages.Flows.FlowDesigner;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Automaton.Studio.Pages.Flows.Designer.Components.StepExplorer;

public class CoreStepsViewModel : INotifyPropertyChanged
{
    private readonly DesignerViewModel designerViewModel;
    private readonly StepFactory stepFactory;

    private IEnumerable<StepExplorerModel> steps;
    public IEnumerable<StepExplorerModel> Steps
    {
        get => steps;

        set
        {
            steps = value;
            OnPropertyChanged();
        }
    }

    public CoreStepsViewModel(DesignerViewModel designerViewModel, StepFactory stepFactory)
    {
        this.designerViewModel = designerViewModel;
        this.stepFactory = stepFactory;
    }

    public void Initialize()
    {
        Steps = stepFactory.GetCoreSteps();
    }

    public void CreateStep(TreeEventArgs<StepExplorerModel> args)
    {
        var step = args.Node.DataItem;

        if (!step.IsCategory())
        {
            designerViewModel.CreateStep(step);
        }
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}