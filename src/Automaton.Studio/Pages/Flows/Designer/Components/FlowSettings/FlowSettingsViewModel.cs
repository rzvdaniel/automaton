using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.Flows.Designer.Components.FlowSettings;

public class FlowSettingsViewModel
{
    private readonly FlowSettingsService flowDashboardService;

    public Guid Id { get; set; }
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    public FlowSettingsViewModel
    (
        FlowSettingsService flowDashboardService
    )
    {
        this.flowDashboardService = flowDashboardService;
    }

    public async Task GetFlowDashboard(string flowIdString)
    {
        Guid.TryParse(flowIdString, out var flowId);
        var result = await flowDashboardService.GetDashboardDetails(flowId);

        Id = result.Id;
        Name = result.Name;
        Created = result.Created;
        Updated = result.Updated;
    }
}
