using System.Text;

namespace Automaton.Infrastructure.RunnerHosts;

public sealed record RunnerHostCloudInitRequest(
    Uri ApiUrl,
    Guid RunnerHostId,
    string EnrollmentToken,
    string RunnerImage,
    string DataRoot = "/opt/automaton/infrastructure-runner",
    RunnerRabbitMqConfiguration? RabbitMq = null);

public sealed record RunnerRabbitMqConfiguration(
    string Host,
    int Port,
    string Username,
    string Password,
    string VirtualHost = "/",
    string ExchangeName = "cap.default.router")
{
    public string ToEnvironmentFile() => string.Join('\n',
        $"RabbitMq__Host={Host}",
        $"RabbitMq__Port={Port}",
        $"RabbitMq__Username={Username}",
        $"RabbitMq__Password={Password}",
        $"RabbitMq__VirtualHost={VirtualHost}",
        $"RabbitMq__ExchangeName={ExchangeName}",
        string.Empty);
}

public static class RunnerHostCloudInit
{
    public static string Generate(RunnerHostCloudInitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.EnrollmentToken))
        {
            throw new ArgumentException("An enrollment token is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.RunnerImage))
        {
            throw new ArgumentException("A Runner image is required.", nameof(request));
        }

        var root = request.DataRoot.TrimEnd('/');
        var configPath = $"{root}/config";
        var workspacePath = $"{root}/workspace";
        var artifactsPath = $"{root}/artifacts";
        var logsPath = $"{root}/logs";
        var rabbitMqEnvPath = "/etc/automaton/infrastructure-runner-rabbitmq.env";
        var rabbitMqEnv = request.RabbitMq?.ToEnvironmentFile() ?? string.Empty;
        var env = string.Join('\n',
            $"AUTOMATON_SERVER_URL={request.ApiUrl.ToString().TrimEnd('/')}",
            "AUTOMATON_RUNNER_PURPOSE=Infrastructure",
            $"AUTOMATON_RUNNER_HOST_ID={request.RunnerHostId}",
            $"AUTOMATON_ENROLLMENT_TOKEN={request.EnrollmentToken}",
            request.RabbitMq is null ? string.Empty : $"RabbitMq__Host={request.RabbitMq.Host}",
            request.RabbitMq is null ? string.Empty : $"RabbitMq__Port={request.RabbitMq.Port}",
            request.RabbitMq is null ? string.Empty : $"RabbitMq__Username={request.RabbitMq.Username}",
            request.RabbitMq is null ? string.Empty : $"RabbitMq__Password={request.RabbitMq.Password}",
            request.RabbitMq is null ? string.Empty : $"RabbitMq__VirtualHost={request.RabbitMq.VirtualHost}",
            request.RabbitMq is null ? string.Empty : $"RabbitMq__ExchangeName={request.RabbitMq.ExchangeName}",
            string.Empty);

        var builder = new StringBuilder();
        builder.AppendLine("#cloud-config");
        builder.AppendLine("write_files:");
        builder.AppendLine("  - path: /etc/automaton/infrastructure-runner.env");
        builder.AppendLine("    owner: root:root");
        builder.AppendLine("    permissions: \"0600\"");
        builder.AppendLine("    content: |");
        foreach (var line in env.Split('\n'))
        {
            builder.AppendLine($"      {line}");
        }

        if (request.RabbitMq is not null)
        {
            builder.AppendLine("  - path: /etc/automaton/infrastructure-runner-rabbitmq.env");
            builder.AppendLine("    owner: root:root");
            builder.AppendLine("    permissions: \"0600\"");
            builder.AppendLine("    content: |");
            foreach (var line in rabbitMqEnv.Split('\n'))
            {
                builder.AppendLine($"      {line}");
            }
        }

        builder.AppendLine("runcmd:");
        builder.AppendLine($"  - mkdir -p {ShellQuote(configPath)} {ShellQuote(workspacePath)} {ShellQuote(artifactsPath)} {ShellQuote(logsPath)}");
        builder.AppendLine($"  - chmod 700 {ShellQuote(root)}");
        builder.AppendLine($"  - docker pull {ShellQuote(request.RunnerImage)}");
        builder.AppendLine("  - >-");
        builder.AppendLine("    docker run -d --name automaton-infrastructure-runner");
        builder.AppendLine($"    --restart unless-stopped {RunnerDockerArguments.InfrastructureHost}");
        builder.AppendLine("    --env-file /etc/automaton/infrastructure-runner.env");
        if (request.RabbitMq is not null)
            builder.AppendLine($"    --env-file {ShellQuote(rabbitMqEnvPath)}");
        builder.AppendLine($"    -v {ShellQuote(configPath)}:/app/config");
        builder.AppendLine($"    -v {ShellQuote(workspacePath)}:/automaton/workspace");
        builder.AppendLine($"    -v {ShellQuote(artifactsPath)}:/automaton/artifacts");
        builder.AppendLine($"    -v {ShellQuote(logsPath)}:/app/logs");
        builder.AppendLine($"    {ShellQuote(request.RunnerImage)}");
        builder.AppendLine($"  - shred -u /etc/automaton/infrastructure-runner.env || rm -f /etc/automaton/infrastructure-runner.env");
        return builder.ToString();
    }

    private static string ShellQuote(string value)
        => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
}
