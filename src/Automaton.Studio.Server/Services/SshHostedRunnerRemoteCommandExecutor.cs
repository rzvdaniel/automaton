using Automaton.Api.Config;
using Automaton.Api.Entities;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Automaton.Api.Services;

public sealed class SshHostedRunnerRemoteCommandExecutor : IHostedRunnerRemoteCommandExecutor
{
    private readonly HostedRunnerOptions options;
    private readonly ILogger<SshHostedRunnerRemoteCommandExecutor> logger;

    public SshHostedRunnerRemoteCommandExecutor(
        IOptionsSnapshot<HostedRunnerOptions> options,
        ILogger<SshHostedRunnerRemoteCommandExecutor> logger)
    {
        this.options = options.Value;
        this.logger = logger;
    }

    public Task<HostedRunnerRemoteExecutionResult> ExecuteProvisionScriptAsync(
        RunnerHost host,
        string script,
        CancellationToken cancellationToken)
        => ExecuteScriptAsync(host, script, cancellationToken);

    public Task<HostedRunnerRemoteExecutionResult> ExecuteScriptAsync(
        RunnerHost host,
        string script,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(host.PublicIp))
        {
            return Task.FromResult(Failure("Runner host has no public IP configured."));
        }

        if (string.IsNullOrWhiteSpace(options.SshPrivateKeyPath) || !File.Exists(options.SshPrivateKeyPath))
        {
            return Task.FromResult(Failure($"SSH private key file was not found at '{options.SshPrivateKeyPath}'."));
        }

        try
        {
            using var privateKey = string.IsNullOrWhiteSpace(options.SshPrivateKeyPassphrase)
                ? new PrivateKeyFile(options.SshPrivateKeyPath)
                : new PrivateKeyFile(options.SshPrivateKeyPath, options.SshPrivateKeyPassphrase);

            var connectionInfo = new Renci.SshNet.ConnectionInfo(
                host.PublicIp,
                options.SshPort,
                options.SshUsername,
                new PrivateKeyAuthenticationMethod(options.SshUsername, privateKey))
            {
                Timeout = TimeSpan.FromSeconds(options.SshConnectTimeoutSeconds)
            };

            using var client = new SshClient(connectionInfo);
            client.Connect();

            using var command = client.CreateCommand($"sh -lc {HostedRunnerDockerCommandBuilder.ShellQuote(script)}");
            command.CommandTimeout = TimeSpan.FromSeconds(options.SshCommandTimeoutSeconds);
            var output = command.Execute() ?? string.Empty;
            var error = command.Error ?? string.Empty;
            var exitStatus = command.ExitStatus ?? -1;
            var result = new HostedRunnerRemoteExecutionResult(
                exitStatus == 0,
                exitStatus,
                output,
                error);

            client.Disconnect();
            return Task.FromResult(result);
        }
        catch (SshException ex)
        {
            logger.LogError(ex, "SSH provisioning failed for runner host {RunnerHostId}", host.Id);
            return Task.FromResult(Failure(ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected remote provisioning failure for runner host {RunnerHostId}", host.Id);
            return Task.FromResult(Failure(ex.Message));
        }
    }

    private static HostedRunnerRemoteExecutionResult Failure(string message)
        => new(false, -1, string.Empty, message);
}
