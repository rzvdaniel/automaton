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
    {
        if (arguments.Count == 0 || !string.Equals(arguments[0], "docker", StringComparison.Ordinal))
            return Task.FromResult(Failure("Only typed Docker operations are supported."));

        return ExecuteAsync(new InfrastructureCommandRequest
        {
            Script = string.Join(' ', arguments.Select(QuoteShellArgument)),
            TimeoutSeconds = 900
        }, cancellationToken);
    }

    private static string QuoteShellArgument(string value)
        => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

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
