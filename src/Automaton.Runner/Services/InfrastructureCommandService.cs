using System.Diagnostics;
using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Executes commands requested by the API on an infrastructure-profile Runner.
/// The process identity and container capabilities come from installation;
/// this service does not grant privileges to an ordinary Runner.
/// </summary>
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

    private bool IsEnabled() => RunnerCapabilities.Normalize(
            Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES")?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"))
        .Contains(RunnerCapabilities.HostExecution, StringComparer.OrdinalIgnoreCase);

    public async Task<InfrastructureCommandResponse> ExecuteAsync(
        InfrastructureCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return Failure("Infrastructure commands are disabled for this Runner installation.");
        }

        if (string.IsNullOrWhiteSpace(request.Script))
        {
            return Failure("Infrastructure command script cannot be empty.");
        }

        var timeoutSeconds = Math.Clamp(request.TimeoutSeconds, 1, 900);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--noprofile");
        startInfo.ArgumentList.Add("--norc");
        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add(request.Script);

        Process? process = null;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start infrastructure command.");

            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            return new InfrastructureCommandResponse
            {
                Success = process.ExitCode == 0,
                ExitStatus = process.ExitCode,
                Output = await outputTask,
                Error = await errorTask
            };
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            TryKill(process);
            return Failure($"Infrastructure command timed out after {timeoutSeconds} seconds.", 124);
        }
        catch (Exception ex)
        {
            TryKill(process);
            return Failure(ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
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
        if (!string.Equals(purpose, RunnerPurposes.Infrastructure, StringComparison.Ordinal))
            return Task.FromResult(Failure("Typed host-container execution requires an Infrastructure Runner."));

        var capabilities = RunnerCapabilities.Normalize(
            Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES")?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            purpose);
        if (!capabilities.Contains(RunnerCapabilities.HostContainerExecution, StringComparer.OrdinalIgnoreCase))
            return Task.FromResult(Failure("Host container execution capability is not enabled."));

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
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            var output = await outputTask;
            var error = await errorTask;
            return new InfrastructureCommandResponse
            {
                Success = process.ExitCode == 0,
                ExitStatus = process.ExitCode,
                Output = output,
                Error = error
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

    private static InfrastructureCommandResponse Failure(string message, int exitStatus = -1)
        => new()
        {
            Success = false,
            ExitStatus = exitStatus,
            Error = message
        };

    private static void TryKill(Process? process)
    {
        if (process is { HasExited: false })
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The command result already reports the timeout/failure.
            }
        }
    }
}
