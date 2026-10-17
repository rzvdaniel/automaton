using System.Diagnostics;
using System.Text;
using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>Executes typed Docker operations on an infrastructure-profile Runner.</summary>
public sealed class InfrastructureCommandService
{
    private readonly Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<InfrastructureCommandResponse>> typedDockerExecutor;

    public InfrastructureCommandService()
        : this(RunTypedDockerProcessAsync)
    {
    }

    internal InfrastructureCommandService(
        Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<InfrastructureCommandResponse>> typedDockerExecutor)
    {
        this.typedDockerExecutor = typedDockerExecutor;
    }

    internal Task<InfrastructureCommandResponse> ExecuteTypedDockerAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
        => ExecuteTypedDockerAsync(arguments, TimeSpan.FromSeconds(900), cancellationToken);

    internal Task<InfrastructureCommandResponse> ExecuteTypedDockerAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (arguments.Count == 0 || !string.Equals(arguments[0], "docker", StringComparison.Ordinal))
            return Task.FromResult(Failure("Only typed Docker operations are supported."));

        var purpose = RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"));
        var capabilities = RunnerCapabilities.Normalize(
            Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES")?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            purpose);
        var profile = RunnerApplicationProfiles.Normalize(
            Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"));
        var authorized = purpose == RunnerPurposes.Infrastructure
            ? capabilities.Contains(RunnerCapabilities.HostContainerExecution, StringComparer.OrdinalIgnoreCase)
            : profile == RunnerApplicationProfiles.MachineManagement
                && capabilities.Contains(RunnerCapabilities.ManagedContainerExecution, StringComparer.OrdinalIgnoreCase);
        if (!authorized)
            return Task.FromResult(Failure("Typed container execution is not enabled for this Runner profile."));

        if (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromSeconds(86_400))
            return Task.FromResult(Failure("Typed Docker timeout must be between 1 and 86400 seconds."));
        return typedDockerExecutor(arguments, timeout, cancellationToken);
    }

    private static async Task<InfrastructureCommandResponse> RunTypedDockerProcessAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = arguments[0],
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments.Skip(1))
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            if (!process.Start())
                return Failure("Failed to start the typed Docker process.");
            var actionContainer = IsActionContainerRun(arguments);
            var boundedOutput = ShouldBoundDockerOutput(arguments);
            var outputTask = CaptureOutputAsync(process.StandardOutput, actionContainer, boundedOutput, keepTail: false, timeoutCts.Token);
            var errorTask = CaptureOutputAsync(process.StandardError, actionContainer, boundedOutput, keepTail: true, timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            var outputCapture = await outputTask;
            var errorCapture = await errorTask;
            return new InfrastructureCommandResponse
            {
                Success = process.ExitCode == 0,
                ExitStatus = process.ExitCode,
                Output = outputCapture.Content,
                Error = errorCapture.Content,
                OutputExceededLimit = outputCapture.ExceededLimit,
                ErrorTruncated = errorCapture.ExceededLimit
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return Failure($"Typed Docker operation timed out after {timeout.TotalSeconds:0} seconds.", 124);
        }
        catch (Exception exception)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return Failure(exception.Message);
        }
    }

    internal static async Task<string> ReadLogTailAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var tail = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            tail.Append(buffer, 0, count);
            if (tail.Length > HostContainerExecutionService.MaximumLogCharacters)
                tail.Remove(0, tail.Length - HostContainerExecutionService.MaximumLogCharacters);
        }
        return tail.ToString();
    }

    private static async Task<BoundedCapture> ReadBoundedCaptureAsync(
        TextReader reader,
        int maximumCharacters,
        bool keepTail,
        CancellationToken cancellationToken)
    {
        var content = new StringBuilder(maximumCharacters);
        var buffer = new char[4096];
        var exceededLimit = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            var available = maximumCharacters - content.Length;
            if (available >= count)
            {
                content.Append(buffer, 0, count);
                continue;
            }

            exceededLimit = true;
            if (keepTail)
            {
                content.Append(buffer, 0, count);
                content.Remove(0, content.Length - maximumCharacters);
            }
            else if (available > 0)
                content.Append(buffer, 0, available);
        }
        return new(content.ToString(), exceededLimit);
    }

    private static async Task<BoundedCapture> CaptureOutputAsync(
        TextReader reader,
        bool actionContainer,
        bool boundedOutput,
        bool keepTail,
        CancellationToken cancellationToken)
    {
        if (!actionContainer && !boundedOutput)
            return new(await reader.ReadToEndAsync(cancellationToken), false);
        var maximumCharacters = actionContainer
            ? keepTail ? AppActionProcessExecutor.MaximumLogBytes : AppActionProcessExecutor.MaximumResultBytes
            : HostContainerExecutionService.MaximumLogCharacters;
        return await ReadBoundedCaptureAsync(reader, maximumCharacters, keepTail, cancellationToken);
    }

    private static bool ShouldBoundDockerOutput(IReadOnlyList<string> arguments)
        => arguments.Count > 1 && (arguments[1] is "logs" or "run");

    private static bool IsActionContainerRun(IReadOnlyList<string> arguments)
        => arguments.Count > 1 && arguments[1] == "run"
            && arguments.Contains("automaton.resource=action-job", StringComparer.Ordinal);

    private sealed record BoundedCapture(string Content, bool ExceededLimit);

    private static InfrastructureCommandResponse Failure(string message, int exitStatus = -1)
        => new()
        {
            Success = false,
            ExitStatus = exitStatus,
            Error = message
        };

}
