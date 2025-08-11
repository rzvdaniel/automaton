using AntDesign;
using Automaton.Studio.Factories;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Automaton.Studio.Pages.FlowDesigner.Components.StepExplorer;

public class CustomStepsViewModel : INotifyPropertyChanged
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

    public CustomStepsViewModel(DesignerViewModel designerViewModel, StepFactory stepFactory)
    {
        this.designerViewModel = designerViewModel;
        this.stepFactory = stepFactory;
    }

    public void Initialize()
    {
        Steps = stepFactory.GetCustomSteps();
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