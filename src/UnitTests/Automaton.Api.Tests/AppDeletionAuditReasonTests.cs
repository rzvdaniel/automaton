using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Tests;

public sealed class AppDeletionAuditReasonTests
{
    [TestCase(true, "Runner offline")]
    [TestCase(false, null)]
    public async Task RecordDeleted_NamesTheReasonOnlyWhenRuntimeCleanupWasAbandoned(bool abandoned, string? reason)
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var user = Guid.NewGuid();
        var context = AppAuthorizationTestData.CreateUserContextService(user);
        var audit = new Capture();
        var service = new AppMutationAuditService(context, new RecentActivityService(db, context), audit);

        await service.RecordDeletedAsync(new WebApp { Id = Guid.NewGuid(), Name = "tasks" }, user, abandoned, []);

        var metadata = audit.Request!.Metadata!;
        Assert.That(metadata.GetType().GetProperty("Reason")!.GetValue(metadata), Is.EqualTo(reason));
        Assert.That(audit.Request.Message, abandoned ? Does.Contain("Runner offline") : Does.Not.Contain("Runner offline"));
    }

    private sealed class Capture : IAuditEventService
    {
        public AuditEventWriteRequest? Request;
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
        { Request = request; return Task.CompletedTask; }
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message,
            object? metadata = null, string outcome = "Success", bool failOnError = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
