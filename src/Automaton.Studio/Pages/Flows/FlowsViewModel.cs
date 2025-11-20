using Automaton.Core.Enums;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using System.Text;

namespace Automaton.Studio.Pages.Flows;

public class FlowsViewModel
{
    private readonly FlowsService flowsService;
    private readonly FlowService flowService;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly ProjectContextService projectContextService;

    public ICollection<FlowModel> Flows { get; set;  } = new List<FlowModel>();

    /// <summary>
    /// Check if user can manage resources (update/delete) in the current context.
    /// </summary>
    public bool CanManageFlows => authorizationService.CanManageResources();

    /// <summary>
    /// Check if user can execute flows in the current context.
    /// </summary>
    public bool CanExecuteFlows => authorizationService.CanExecuteResources();

    public FlowsViewModel
    (
        FlowsService flowsService,
        RunnerService runnerService,
        FlowService flowService,
        ResourceAuthorizationService authorizationService,
        ProjectContextService projectContextService
    )
    {
        this.flowsService = flowsService;
        this.flowService = flowService;
        this.authorizationService = authorizationService;
        this.projectContextService = projectContextService;
    }

    public async Task GetFlows()
    {
        var projectId = projectContextService.GetCurrentProjectId();
        var flowsInfo = await flowsService.List(projectId);
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

    public async Task RenameFlow(Guid id, string newName)
    {
        await flowService.Rename(id, newName);

        var flow = Flows.SingleOrDefault(x => x.Id == id);
        if (flow != null)
        {
            flow.Name = newName;
        }
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
