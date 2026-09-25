using System.Data.Common;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Automaton.Api.Tests;

internal sealed class HostedRunnerAuditNoOpAuditEventService : IAuditEventService
{
    public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task WriteAsync(
        string action,
        string entityType,
        Guid? entityId,
        string message,
        object? metadata = null,
        string outcome = "Success",
        bool failOnError = false,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class HostedRunnerFailExecutionInsertOnceInterceptor : SaveChangesInterceptor
{
    private bool failNext = true;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (failNext && eventData.Context!.ChangeTracker.Entries<AppModuleExecution>()
                .Any(x => x.State == EntityState.Added))
        {
            failNext = false;
            throw new InvalidOperationException("Simulated execution insert failure.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

internal sealed class HostedRunnerFailUpdateOnceInterceptor : DbCommandInterceptor
{
    private bool failNext = true;

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (failNext && command.CommandText.Contains("UPDATE \"HostedRunners\"", StringComparison.Ordinal))
        {
            failNext = false;
            throw new InvalidOperationException("Simulated hosted Runner state update failure.");
        }

        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}
