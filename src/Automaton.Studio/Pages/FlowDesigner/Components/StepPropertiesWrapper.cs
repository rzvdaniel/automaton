using Automaton.Studio.Domain;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Automaton.Studio.Pages.FlowDesigner.Components;

public class StepPropertiesWrapper : ComponentBase
{
    [Parameter] public StudioStep Step { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Step == null || !Step.HasProperties)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "no-properties");
            builder.AddContent(2, "No properties available for this step.");
            builder.CloseElement();
            return;
        }

        try
        {
            var propertiesComponentType = Step.GetPropertiesComponent();
            
            // Create a wrapper div
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "step-properties-content");
            
            // Create the properties component
            builder.OpenComponent(2, propertiesComponentType);
            builder.AddAttribute(3, "Options", Step);
            
            // Add any additional parameters that might be needed
            builder.CloseComponent();
            
            builder.CloseElement();
        }
        catch (Exception ex)
        {
            builder.OpenElement(10, "div");
            builder.AddAttribute(11, "class", "error");
            builder.AddContent(12, $"Error loading properties: {ex.Message}");
            builder.AddMarkupContent(13, "<br/><small>Note: This step's properties component may be designed only for modal dialogs.</small>");
            builder.CloseElement();
        }
    }
}