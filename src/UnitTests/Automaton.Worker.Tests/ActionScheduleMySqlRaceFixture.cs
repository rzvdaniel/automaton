using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Messaging.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Worker.Tests;

internal static class ActionScheduleMySqlRaceFixture
{
    internal static ApplicationDbContext Open(string connectionString) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36))).Options);

    internal static async Task<Guid> SeedAsync(ApplicationDbContext db, int maximum, bool queued)
    {
        var ownerId = Guid.NewGuid();
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(), Name = $"schedule-race-{ownerId:N}",
            CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities
            {
                ScheduledActions = new ScheduledActionsCapability { MaximumConcurrentExecutions = maximum }
            })
        };
        db.SubscriptionPlans.Add(plan);
        db.Users.Add(new ApplicationUser
        {
            Id = ownerId, PublicUsername = $"race-{ownerId:N}"[..32],
            UserName = $"race-{ownerId:N}", SubscriptionPlanId = plan.Id
        });
        for (var i = 0; i < 3; i++)
        {
            var app = new WebApp
            {
                Id = Guid.NewGuid(), Name = "Schedule race", Summary = "Test",
                Description = "Test", Definition = "{}"
            };
            db.WebApps.Add(app);
            var schedule = ActionScheduleProcessorTests.Schedule(ActionScheduleTypes.OneTime);
            schedule.OwnerType = ActionScheduleOwnerTypes.User;
            schedule.OwnerUserId = ownerId;
            schedule.ApplicationId = app.Id;
            schedule.ExecutionScope = ActionExecutionScopes.AppRunner;
            schedule.Enabled = !queued;
            db.ActionSchedules.Add(schedule);
            if (queued)
                db.ActionScheduleOccurrences.Add(new ActionScheduleOccurrence
                {
                    Id = Guid.NewGuid(), ScheduleId = schedule.Id,
                    Status = ActionScheduleOccurrenceStatuses.Queued,
                    IdempotencyKey = Guid.NewGuid().ToString("N"),
                    ScheduledForUtc = DateTime.UtcNow, TriggeredAtUtc = DateTime.UtcNow,
                    CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
                });
        }
        await db.SaveChangesAsync();
        return ownerId;
    }

    internal sealed class Transactions(ApplicationDbContext db, AdmissionBarrier barrier) : ITransactionalEventExecutor
    {
        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            await barrier.ArriveAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    internal sealed class AdmissionBarrier
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        internal async Task ArriveAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref arrivals) >= 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
        }
    }
}
