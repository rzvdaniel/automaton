using System.Text.Json;

namespace Automaton.Worker.Services;

public interface IWorkerScheduledActionHandler
{
    string ActionKey { get; }
    Task ExecuteAsync(WorkerScheduledActionContext context, CancellationToken cancellationToken);
}

public sealed record WorkerScheduledActionContext(
    Guid ExecutionId,
    Guid OccurrenceId,
    string IdempotencyKey,
    JsonElement Input);
