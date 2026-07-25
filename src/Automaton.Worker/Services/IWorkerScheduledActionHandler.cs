using System.Text.Json;

namespace Automaton.Worker.Services;

public interface IWorkerScheduledActionHandler
{
    string ActionKey { get; }
    Task ExecuteAsync(JsonElement input, CancellationToken cancellationToken);
}
