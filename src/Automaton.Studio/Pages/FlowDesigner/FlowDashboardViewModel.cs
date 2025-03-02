using AutoMapper;
using Automaton.Studio.Models;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.Runners;

public class FlowDashboardViewModel
{
    private readonly FlowDashboardService flowDashboardService;
    private readonly IMapper mapper;

    public Guid Id { get; set; }
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    public FlowDashboardViewModel
    (
        FlowDashboardService flowDashboardService,
        IMapper mapper
    )
    {
        this.flowDashboardService = flowDashboardService;
        this.mapper = mapper;
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
