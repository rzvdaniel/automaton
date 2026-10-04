using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerExecutionDeliveryHandler(
    RunnerExecutionProcessor processor,
    IRuntimeTunnelReconnectRequester tunnelReconnect,
    RunnerFileTransferExecutor? fileTransfers = null)
{
    internal async Task<bool> HandleAsync(ReadOnlyMemory<byte> body, string? messageId, RunnerExecutionApiClient api,
        Guid runnerId, CancellationToken token)
    {
        RunnerExecutionCommand? command;
        try
        {
            command = JsonSerializer.Deserialize<RunnerExecutionCommand>(body.Span,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (command is null || command.OperationId == Guid.Empty) throw new JsonException("Operation identity is required.");
        }
        catch (JsonException exception)
        {
            if (Guid.TryParse(messageId, out var id))
                await RejectKnownAsync(id, api, token);
            Serilog.Log.Warning(exception, "Rejected invalid Runner execution delivery {MessageId}.", messageId);
            return false;
        }
        if (command.RunnerId != runnerId)
        {
            await RejectKnownAsync(command.OperationId, api, token);
            Serilog.Log.Warning("Rejected execution {OperationId} addressed to another Runner.", command.OperationId);
            return false;
        }
        if (command.Kind == RunnerExecutionCommandKinds.ReconnectRuntimeTunnels)
        {
            if (string.IsNullOrWhiteSpace(command.TargetApiInstanceId)
                && string.IsNullOrWhiteSpace(command.TargetApiColor))
            {
                Serilog.Log.Warning("Rejected runtime tunnel reconnect command without a target API instance.");
                return false;
            }

            tunnelReconnect.RequestRuntimeTunnelReconnect(
                command.TargetApiInstanceId, command.TargetApiColor, "API color changed");
            return true;
        }
        if (command.Kind is not null)
        {
            Serilog.Log.Warning("Rejected unsupported Runner execution command kind {CommandKind}.", command.Kind);
            return false;
        }
        RunnerExecutionAssignment assignment;
        try { assignment = await api.AssignmentAsync(command.OperationId, token); }
        catch (HttpRequestException exception) when (IsUnassigned(exception))
        {
            Serilog.Log.Warning(exception, "Rejected unassigned Runner execution {OperationId}.", command.OperationId);
            return false;
        }
        if (assignment.Disposition == "already-applied")
        {
            processor.Forget(command.OperationId);
            return true;
        }
        var result = assignment.Disposition == "cancel"
            ? await processor.CancelAsync(command.OperationId, token)
            : await ExecuteAsync(command, assignment, api, token);
        await api.SubmitAsync(command.OperationId, result, token);
        processor.Forget(command.OperationId);
        return true;
    }

    private static async Task RejectKnownAsync(Guid id, RunnerExecutionApiClient api, CancellationToken token)
    {
        try
        {
            await api.SubmitAsync(id, new() { Error = "Invalid Runner execution command.",
                Started = DateTime.UtcNow, Finished = DateTime.UtcNow }, token);
        }
        catch (HttpRequestException exception) when (IsUnassigned(exception))
        {
            // A foreign or retired identity cannot terminalize another Runner's operation.
            Serilog.Log.Warning(exception, "Discarding unassigned invalid execution {OperationId}.", id);
        }
    }
    private static bool IsUnassigned(HttpRequestException exception)
        => exception.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound;

    private async Task<RunnerExecutionResult> ExecuteAsync(RunnerExecutionCommand command, RunnerExecutionAssignment assignment,
        RunnerExecutionApiClient api, CancellationToken token)
    {
        if (assignment.Disposition != "execute" || assignment.Request is null)
            throw new InvalidDataException("Unknown Runner execution assignment.");
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var monitor = CancellationTokenSource.CreateLinkedTokenSource(token);
        var watching = WatchCancellationAsync(command.OperationId, api, execution, monitor.Token);
        try
        {
            var request = assignment.Request;
            if (request.FileTransfer is null)
                return await processor.ProcessAsync(command.OperationId, request, execution.Token);
            if (fileTransfers is null)
                throw new InvalidOperationException("Runner file-transfer executor is unavailable.");
            return await processor.ProcessAsync(command.OperationId, request, execution.Token,
                (_, transferToken) => fileTransfers.ExecuteAsync(command.OperationId, request.FileTransfer, api, transferToken));
        }
        finally
        {
            await monitor.CancelAsync();
            try { await watching; }
            catch (OperationCanceledException) when (monitor.IsCancellationRequested)
            {
                // Completing execution also stops its cancellation watcher.
            }
        }
    }

    private static async Task WatchCancellationAsync(Guid id, RunnerExecutionApiClient api,
        CancellationTokenSource execution, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            var assignment = await api.StatusAsync(id, token);
            if (assignment.Disposition is "cancel" or "already-applied") { await execution.CancelAsync(); return; }
        }
    }
}
