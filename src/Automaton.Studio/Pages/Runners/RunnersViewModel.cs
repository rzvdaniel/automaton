using Automaton.Studio.Models;
using Automaton.Studio.Services;
using System.Threading.Tasks;
using System.Linq;

namespace Automaton.Studio.Pages.Runners;

public class RunnersViewModel
{
    private readonly RunnerService runnerService;
    private readonly ResourceAuthorizationService authorizationService;

    public ICollection<RunnerModel> Runners { get; set; } = new List<RunnerModel>();

    /// <summary>
    /// Check if user can create runners in the current context.
    /// </summary>
    public bool CanCreateRunner => authorizationService.CanCreateRunner();

    /// <summary>
    /// Check if user can manage resources (update/delete) in the current context.
    /// </summary>
    public bool CanManageRunners => authorizationService.CanManageResources();

    public RunnersViewModel
    (
        RunnerService runnerService,
        FlowService flowService,
        ResourceAuthorizationService authorizationService
    )
    {
        this.runnerService = runnerService;
        this.authorizationService = authorizationService;
    }

    public async Task GetRunners()
    {
        var runners = await runnerService.List();
        Runners = runners
            .Select(runner => new RunnerModel
            {
                Id = runner.Id,
                Name = runner.Name,
                ConnectionId = runner.ConnectionId,
                Status = runner.Status,
                UserId = null // No UserId in RunnerDetails; set as needed
            })
            .ToList();
    }

    public async Task DeleteRunner(Guid id)
    {
        await runnerService.Delete(id);
        await GetRunners();
    }
}
