using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Shared.Components;

public partial class PanelHeader : ComponentBase
{
    [Parameter] public string Title { get; set; } = string.Empty;
    
    // Clear button configuration
    [Parameter] public bool ShowClearButton { get; set; } = false;
    [Parameter] public EventCallback OnClearClick { get; set; }
    [Parameter] public bool ClearDisabled { get; set; } = false;
    [Parameter] public string ClearTooltip { get; set; } = "Clear";
    
    // Pin button configuration
    [Parameter] public bool ShowPinButton { get; set; } = true;
    [Parameter] public EventCallback OnPinClick { get; set; }
    [Parameter] public string PinTooltip { get; set; } = "Pin";
    
    // Close button configuration
    [Parameter] public bool ShowCloseButton { get; set; } = true;
    [Parameter] public EventCallback OnCloseClick { get; set; }
    [Parameter] public string CloseTooltip { get; set; } = "Close";
    
    // Additional actions slot
    [Parameter] public RenderFragment? AdditionalActions { get; set; }
}