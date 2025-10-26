using Automaton.Core.Models;
using Automaton.Studio.Domain;
using Automaton.Studio.Factories;
using Automaton.Studio.Mapping;
using Automaton.Studio.Models;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services;

public class FlowService
{
    private readonly HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly ILogger<FlowService> logger;
    private readonly StepFactory stepFactory;

    public FlowService
    (
        ConfigurationService configService,
        HttpClient httpClient,
        ILogger<FlowService> logger,
        StepFactory stepFactory
    )
    {
        this.logger = logger;
        this.configService = configService;
        this.httpClient = httpClient;
        this.stepFactory = stepFactory;
    }

    public async Task<StudioFlow> Load(Guid id)
    {
        var response = await httpClient.GetAsync($"{configService.FlowsUrl}/{id}");
        response.EnsureSuccessStatusCode();

        var flow = await response.Content.ReadAsAsync<Flow>();
        var studioFlow = FlowMapper.ToStudioFlow(flow, stepFactory);

        return studioFlow;
    }

    public async Task<StudioFlow> Create(string name)
    {
        var defaultDefinition = new Definition();

        var flow = new Flow 
        { 
            Name = name,
            StartupDefinitionId = defaultDefinition.Id,
            Definitions = new List<Definition> { defaultDefinition }
        };

        var response = await httpClient.PostAsJsonAsync(configService.FlowsUrl, flow);
        response.EnsureSuccessStatusCode();

        flow = await response.Content.ReadAsAsync<Flow>();

        var studioFlow = FlowMapper.ToStudioFlow(flow, stepFactory);

        return studioFlow;
    }

    public async Task Update(StudioFlow flow)
    {
        var flowDto = FlowMapper.ToFlow(flow);

        var response = await httpClient.PutAsJsonAsync($"{configService.FlowsUrl}/{flow.Id}", flowDto);
        response.EnsureSuccessStatusCode();
    }

    public async Task Delete(Guid flowId)
    {
        var response = await httpClient.DeleteAsync($"{configService.FlowsUrl}/{flowId}");

        response.EnsureSuccessStatusCode();
    }
   
    public async Task<IEnumerable<RunnerFlowResult>> Run(Guid flowId, IEnumerable<Guid> runnerIds)
    {
        var flowAndRunners = new
        {
            FlowId = flowId,
            RunnerIds = runnerIds
        };

        var response = await httpClient.PostAsJsonAsync($"{configService.FlowsUrl}/run", flowAndRunners);
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadAsAsync<IEnumerable<RunnerFlowResult>>();

        return results;
    }
}
