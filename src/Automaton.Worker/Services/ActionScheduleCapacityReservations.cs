using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;

namespace Automaton.Worker.Services;

internal static class ActionScheduleCapacityReservations
{
    internal static IQueryable<ActionScheduleOccurrence> Active(ApplicationDbContext db)
        => db.ActionScheduleOccurrences.Where(occurrence =>
            (occurrence.ExecutionId == null
                && (occurrence.Status == ActionScheduleOccurrenceStatuses.Published
                    || occurrence.Status == ActionScheduleOccurrenceStatuses.Dispatching))
            || db.AppModuleExecutions.Any(execution => execution.Id == occurrence.ExecutionId
                && (execution.Status == AppModuleExecutionStatuses.Created
                    || execution.Status == AppModuleExecutionStatuses.Dispatching
                    || execution.Status == AppModuleExecutionStatuses.Running)));
}
