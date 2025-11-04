using Automaton.Core.Enums;
using Automaton.Studio.Domain;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using System.Text;

namespace Automaton.Studio.Pages.Flows;

public class FlowsViewModel
{
    private readonly FlowsService flowsService;
    private readonly FlowService flowService;

    public ICollection<FlowModel> Flows { get; set;  } = new List<FlowModel>();

    public FlowsViewModel
    (
        FlowsService flowsService,
        RunnerService runnerService,
        FlowService flowService
    )
    {
        this.flowsService = flowsService;
        this.flowService = flowService;
    }

    public async Task GetFlows()
    {
        var flowsInfo = await flowsService.List();
        Flows = flowsInfo
            .Select(flow => new FlowModel
            {
                Id = flow.Id,
                Name = flow.Name,
                Created = flow.Created,
                Updated = flow.Updated,
                Started = flow.Started,
                Finished = flow.Finished,
                Status = Enum.TryParse<WorkflowStatus>(flow.Status, out var status) ? status : WorkflowStatus.None
            })
            .ToList();
    }

    public async Task CreateFlow(string name)
    {
        var flow = await flowService.Create(name);

        var flowModel = new FlowModel
        {
            Id = flow.Id,
            Name = flow.Name,
            Created = flow.Created,
            Updated = flow.Updated
        };

        Flows.Add(flowModel);
    }

    public async Task DeleteFlow(Guid id)
    {
        await flowService.Delete(id);

        var flow = Flows.SingleOrDefault(x => x.Id == id);

        Flows.Remove(flow);
    }

    public async Task<string> RunFlow(Guid id, IEnumerable<Guid> runnerIds)
    {
        var flowModel = GetFlowModel(id);

        try
        {
            flowModel.IsRunning();

            var result = await flowService.Run(id, runnerIds);

            var message = GetFlowExecutionResult(result);

            return message;
        }
        finally 
        {
            flowModel.IsNotRunning();
        }
    }

    private FlowModel GetFlowModel(Guid id)
    {
        return Flows.SingleOrDefault(x => x.Id == id);
    }

    private string GetFlowExecutionResult(IEnumerable<RunnerFlowResult> results)
    {
        var resultText = new StringBuilder();

        foreach (var result in results)
        {
            var runnerMessage = result.Status == WorkflowStatus.None ?
                $"Runner {result.RunnerId} did not respond" :
                $"Runner {result.RunnerId} returned {result.Status}";

            resultText.AppendLine(runnerMessage);
        }

        return resultText.ToString();
    }
}
