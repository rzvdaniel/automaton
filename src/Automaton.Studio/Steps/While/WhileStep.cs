using Automaton.Studio.Attributes;
using Automaton.Studio.Factories;
using Automaton.Studio.Steps.Sequence;

namespace Automaton.Studio.Steps.While;

[StepDescription(
    Name = "While",
    Type = "While",
    DisplayName = "While",
    Category = "Flow control",
    Description = "While loop statement",
    Icon = "fork"
)]
public class WhileStep : SequenceStep
{
    public string Condition
    {
        get => GetInputValue<string>(nameof(Condition));
        set => SetInputValue(nameof(Condition), value);
    }

    public WhileStep(StepFactory stepFactory) : base(stepFactory)
    {
        SetInputValue(nameof(Condition), string.Empty);
        HasProperties = true;
    }

    public override Type GetDesignerComponent()
    {
        return typeof(WhileDesigner);
    }

    public override Type GetPropertiesComponent()
    {
        return typeof(WhileProperties);
    }
}
