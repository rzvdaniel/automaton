using System.Diagnostics;
using Automaton.Api.Config;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public sealed class LocalDockerHostedRunnerRemoteCommandExecutor : IHostedRunnerRemoteCommandExecutor
{
    private readonly HostedRunnerOptions options;
    private readonly ILogger<LocalDockerHostedRunnerRemoteCommandExecutor> logger;

    public LocalDockerHostedRunnerRemoteCommandExecutor(
        IOptionsSnapshot<HostedRunnerOptions> options,
        ILogger<LocalDockerHostedRunnerRemoteCommandExecutor> logger)
    {
        this.options = options.Value;
        this.logger = logger;
    }

    public bool CanHandle(RunnerHost host, HostedRunnerOptions options)
        => string.Equals(options.DeploymentMode, HostedRunnerDeploymentModes.LocalDocker, StringComparison.Ordinal)
           && string.Equals(host.Provider, RunnerHostProviders.Local, StringComparison.OrdinalIgnoreCase);

    public Task<HostedRunnerRemoteExecutionResult> ExecuteProvisionScriptAsync(
        RunnerHost host,
        string script,
        CancellationToken cancellationToken)
        => ExecuteScriptAsync(host, script, cancellationToken);

    public async Task<HostedRunnerRemoteExecutionResult> ExecuteScriptAsync(
        RunnerHost host,
        string script,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var psi = new ProcessStartInfo("/bin/sh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-lc");
            psi.ArgumentList.Add(script);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start local docker command process.");
            var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return new HostedRunnerRemoteExecutionResult(
                process.ExitCode == 0,
                process.ExitCode,
                await stdOutTask,
                await stdErrTask);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local Docker command failed for runner host {RunnerHostId}", host.Id);
            return Failure(ex.Message);
        }
    }

    private static HostedRunnerRemoteExecutionResult Failure(string message)
        => new(false, -1, string.Empty, message);
}
