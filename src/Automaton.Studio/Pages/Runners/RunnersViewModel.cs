using Automaton.Studio.Models;
using Automaton.Studio.Services;
using System.Threading.Tasks;
using System.Linq;

namespace Automaton.Studio.Pages.Runners;

public class RunnersViewModel
{
    private readonly RunnerService runnerService;

    public ICollection<RunnerModel> Runners { get; set; } = new List<RunnerModel>();

    public RunnersViewModel
    (
        RunnerService runnerService,
        FlowService flowService
    )
    {
        this.runnerService = runnerService;
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
