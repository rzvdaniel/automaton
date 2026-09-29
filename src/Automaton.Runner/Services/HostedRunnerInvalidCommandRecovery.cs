using Automaton.Core.Models;
using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Services;

internal sealed class HostedRunnerInvalidCommandRecovery(ILogger<HostedRunnerInvalidCommandRecovery> logger)
{
    public async Task RejectAsync(
        Guid? operationId,
        Guid? attemptId,
        ulong deliveryTag,
        Exception exception,
        Func<Guid, Guid, string, CancellationToken, Task<string>> reportFailure,
        Func<bool, CancellationToken, Task> rejectDelivery,
        CancellationToken cancellationToken)
    {
        var requeue = false;
        if (operationId is { } operation && attemptId is { } attempt)
            requeue = !await TryReportFailureAsync(
                operation, attempt, deliveryTag, exception, reportFailure, cancellationToken);
        else
            logger.LogError(exception,
                "Discarding invalid Hosted Runner command with unreadable operation metadata; delivery tag {DeliveryTag}.",
                deliveryTag);

        await rejectDelivery(requeue, cancellationToken);
        if (requeue)
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }

    private async Task<bool> TryReportFailureAsync(
        Guid operationId,
        Guid attemptId,
        ulong deliveryTag,
        Exception exception,
        Func<Guid, Guid, string, CancellationToken, Task<string>> reportFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await reportFailure(operationId, attemptId, exception.Message, cancellationToken);
            logger.LogError(exception,
                "Rejected invalid Hosted Runner deployment command for operation {OperationId}, attempt {AttemptId}; API outcome {Outcome}.",
                operationId, attemptId, outcome);
            return true;
        }
        catch (Exception reportException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(reportException,
                "Could not persist rejection for invalid Hosted Runner command operation {OperationId}, attempt {AttemptId}; retrying delivery {DeliveryTag}.",
                operationId, attemptId, deliveryTag);
            return false;
        }
    }

    public static (Guid? OperationId, Guid? AttemptId) GetCommandIdentity(
        string? messageId,
        string? correlationId,
        HostedRunnerDeploymentCommand? command)
    {
        var operationId = ParseId(messageId) ?? NonEmpty(command?.OperationId);
        var attemptId = ParseId(correlationId) ?? NonEmpty(command?.ExecutionAttemptId);
        return (operationId, attemptId);
    }

    private static Guid? ParseId(string? value)
        => Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;

    private static Guid? NonEmpty(Guid? value)
        => value is { } id && id != Guid.Empty ? id : null;
}
