using System.Text;

namespace Automaton.Infrastructure.RunnerHosts;

public sealed record RunnerHostCloudInitRequest(
    Uri ApiUrl,
    Guid RunnerHostId,
    string EnrollmentToken,
    string RunnerImage,
    string DataRoot = "/opt/automaton/infrastructure-runner");

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
        var env = string.Join('\n',
            $"AUTOMATON_SERVER_URL={request.ApiUrl.ToString().TrimEnd('/')}",
            "AUTOMATON_RUNNER_PURPOSE=Infrastructure",
            $"AUTOMATON_RUNNER_HOST_ID={request.RunnerHostId}",
            $"AUTOMATON_ENROLLMENT_TOKEN={request.EnrollmentToken}",
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

        builder.AppendLine("runcmd:");
        builder.AppendLine($"  - mkdir -p {ShellQuote(configPath)} {ShellQuote(workspacePath)} {ShellQuote(artifactsPath)} {ShellQuote(logsPath)}");
        builder.AppendLine($"  - chmod 700 {ShellQuote(root)}");
        builder.AppendLine($"  - docker pull {ShellQuote(request.RunnerImage)}");
        builder.AppendLine("  - >-");
        builder.AppendLine("    docker run -d --name automaton-infrastructure-runner");
        builder.AppendLine($"    --restart unless-stopped {RunnerDockerArguments.PrivilegedHost}");
        builder.AppendLine("    --env-file /etc/automaton/infrastructure-runner.env");
        builder.AppendLine("    -v /var/run/docker.sock:/var/run/docker.sock -v /:/host");
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
