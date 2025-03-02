using AutoMapper;
using Automaton.Core.Models;
using Automaton.Core.Services;
using Automaton.Runner.Models;

namespace Automaton.Runner.Services;

public class FlowService
{
    private readonly IMapper mapper;
    private readonly HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly ConfigurationService configurationService;
    private readonly FlowConvertService flowConvertService;
    private readonly FlowExecutionsService flowExecutionsService;
    private readonly FlowExecuteService flowExecuteService;


    public FlowService
    (
        IMapper mapper,
        ConfigurationService configService, 
        HttpClient httpClient,
        FlowConvertService flowConvertService, 
        FlowExecutionsService flowExecutionsService,
        FlowExecuteService flowExecuteService,
        ConfigurationService configurationService
    )
    {
        this.configService = configService;
        this.httpClient = httpClient;
        this.mapper = mapper;
        this.httpClient = httpClient;
        this.configurationService = configurationService;
        this.flowConvertService = flowConvertService;
        this.flowExecutionsService = flowExecutionsService;
        this.flowExecuteService = flowExecuteService;
    }

    public async Task<WorkflowExecution> ExecuteFlow(Guid flowId)
    {
        var flow = await Load(flowId);

        var result = await Execute(flow);

        return result;
    }

    private async Task<WorkflowExecution> Execute(Flow flow, CancellationToken cancellationToken = default)
    {
        var workflow = flowConvertService.ConvertFlow(flow);

        var workflowExecution = await flowExecuteService.Execute(workflow, cancellationToken);

        await SaveWorkflowExecution(workflowExecution);

        return workflowExecution;
    }

    private async Task<Flow> Load(Guid id)
    {
        var response = await httpClient.GetAsync($"{configService.FlowsUrl}/{id}");
        var flow = await response.Content.ReadAsAsync<Flow>();

        return flow;
    }

    private async Task SaveWorkflowExecution(WorkflowExecution workflowExecution)
    {
        var flowExecution = mapper.Map<FlowExecution>(workflowExecution);
        await flowExecutionsService.Add(flowExecution);
    }
}
