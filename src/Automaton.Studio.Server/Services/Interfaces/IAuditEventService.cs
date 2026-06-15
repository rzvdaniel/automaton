namespace Automaton.Studio.Server.Services.Interfaces;

public interface IAuditEventService
{
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
