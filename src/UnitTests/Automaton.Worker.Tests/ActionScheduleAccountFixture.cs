using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Worker.Tests;

internal static class ActionScheduleAccountFixture
{
    internal static async Task CompleteAsync(ApplicationDbContext db, ActionScheduleOccurrence pending, Guid appId)
    {
        pending.ExecutionId = Guid.NewGuid();
        pending.Status = ActionScheduleOccurrenceStatuses.Dispatched;
        db.AppModuleExecutions.Add(new AppModuleExecution
        {
            Id = pending.ExecutionId.Value, ScheduleOccurrenceId = pending.Id,
            AppId = appId, Status = AppModuleExecutionStatuses.Succeeded
        });
        await db.SaveChangesAsync();
    }

    internal static async Task SeedAsync(ApplicationDbContext db, Guid ownerId, ActionSchedule first, ActionSchedule second)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AspNetUsers (Id) VALUES ({ownerId})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO WebApps (Id) VALUES ({first.ApplicationId})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO WebApps (Id) VALUES ({second.ApplicationId})");
        db.ActionSchedules.Add(second);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
}
