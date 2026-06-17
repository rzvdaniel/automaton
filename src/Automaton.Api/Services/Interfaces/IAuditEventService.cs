using Automaton.Api.Models;

namespace Automaton.Api.Services.Interfaces;

public interface IAuditEventService
{
    Task WriteAsync(
        AuditEventWriteRequest request,
        CancellationToken cancellationToken = default);

    Task WriteAsync(
        string action,
        string entityType,
        Guid? entityId,
        string message,
        object? metadata = null,
        string outcome = "Success",
        bool failOnError = false,
        CancellationToken cancellationToken = default);
}
