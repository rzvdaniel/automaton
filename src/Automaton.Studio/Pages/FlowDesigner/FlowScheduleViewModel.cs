using Automaton.Studio.Models;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.Runners;

public class FlowScheduleViewModel
{
    private readonly FlowScheduleService flowScheduleService;
    private readonly RunnerService runnerService;
    public ICollection<RunnerModel> Runners { get; set; } = new List<RunnerModel>();

    public Guid FlowId { get; set; }
    public IList<FlowScheduleModel> Schedules { get; set; } = new List<FlowScheduleModel>();

    public FlowScheduleViewModel
    (
        FlowScheduleService flowScheduleService,
        RunnerService runnerService
    )
    {
        this.flowScheduleService = flowScheduleService;
        this.runnerService = runnerService;
    }

    public async Task GetFlowSchedules(int startIndex, int pageSize)
    {
        Schedules = await flowScheduleService.List(FlowId);
    }

    public async Task GetRunners()
    {
        Runners = await runnerService.List();
    }

    public void NewSchedule()
    {
        var schedule = new FlowScheduleModel
        {
            Id = Guid.NewGuid(),
            Name = GetNewScheduleName(),
            FlowId = FlowId,
            IsNew = true
        };

        Schedules.Insert(0, schedule);
    }

    public async Task AddSchedule(FlowScheduleModel schedule)
    {
        await flowScheduleService.Create(schedule);
        schedule.IsNew = false;
    }

    public async Task UpdateSchedule(FlowScheduleModel schedule)
    {
        await flowScheduleService.Update(schedule);
    }

    public async Task DeleteSchedule(Guid id)
    {
        var schedule = Schedules.SingleOrDefault(x => x.Id == id);

        if (!schedule.IsNew)
        {
            await flowScheduleService.Delete(id);
        }

        Schedules.Remove(schedule);
    }

    private string GetNewScheduleName()
    {
        return $"Schedule {Schedules.Count}";
    }
}
