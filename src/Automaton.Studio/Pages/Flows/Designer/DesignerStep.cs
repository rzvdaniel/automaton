using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Automaton.Studio.Domain;
using CustomStep = Automaton.Studio.Steps.Custom.CustomStep;

namespace Automaton.Studio.Pages.Flows.Designer;

public class DesignerStep : ComponentBase
{
    [Parameter]
    public StudioStep Step { get; set; }

    [Parameter]
    public EventCallback<bool> OnStepUpdated { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Step == null)
        {
            throw new ArgumentException("Invalid step parameter");
        }

        base.BuildRenderTree(builder);

        // get the component to view the product with
        Type componentType = Step.GetDesignerComponent();
        // create an instance of this component
        builder.OpenComponent(0, componentType);
        // set the `Step` attribute of the component
        builder.AddAttribute(1, "Step", Step);
        
        // Only add OnStepUpdated callback for CustomStep designers
        if (Step is CustomStep)
        {
            builder.AddAttribute(2, "OnStepUpdated", OnStepUpdated);
        }
        
        // close
        builder.CloseComponent();
    }
}
