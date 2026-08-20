using System.Diagnostics;
using System.Text.Json;
using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workspaces;
using Automaton.AgentOrchestrator.Workflows;

namespace Automaton.AgentOrchestrator.Trackers.GitHub;

public sealed record GitHubCliResult(int ExitCode, string StandardOutput, string StandardError);

public interface IGitHubCliRunner
{
    Task<GitHubCliResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken);
}

public sealed class ProcessGitHubCliRunner : IGitHubCliRunner
{
    public async Task<GitHubCliResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("gh")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the GitHub CLI for human handoff.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new(process.ExitCode, await output, await error);
    }
}

public interface IHumanApprovalHandoff
{
    Task<string> CaptureReviewedHeadAsync(Workspace workspace, CancellationToken cancellationToken);
    Task<RunOutcome> CompleteAsync(Workspace workspace, string reviewedHeadSha, AgentExecutionOptions agent, CancellationToken cancellationToken);
}

public sealed class GitHubHumanApprovalHandoff(IGitCommandRunner git, IGitHubCliRunner github) : IHumanApprovalHandoff
{
    public async Task<string> CaptureReviewedHeadAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(workspace.Path, ["rev-parse", "HEAD"], cancellationToken);
        var head = result.StandardOutput.Trim();
        if (result.ExitCode != 0 || !IsSha(head)) throw new InvalidOperationException("Unable to capture the reviewed local branch head.");
        return head;
    }

    public async Task<RunOutcome> CompleteAsync(Workspace workspace, string reviewedHeadSha, AgentExecutionOptions agent, CancellationToken cancellationToken)
    {
        if (!IsSha(reviewedHeadSha)) return RunOutcome.PermanentFailure("Reviewer-approved head is invalid; human handoff was refused.");
        var capability = CodexRuntimeCapability.From(agent, agent.Developer);
        if (capability.GitHubAccess != "write") return RunOutcome.PermanentFailure("GitHub write capability is required for human handoff.");

        var localHead = await CaptureReviewedHeadAsync(workspace, cancellationToken);
        if (!ShaEqual(reviewedHeadSha, localHead))
            return RunOutcome.PermanentFailure("Local branch head changed after Reviewer approval; human handoff was refused.");

        var pullRequest = await FindPullRequestAsync(workspace, capability.ChildEnvironment, cancellationToken);
        if (pullRequest is null) return RunOutcome.PermanentFailure("No pull request exists for the assigned branch; human handoff was refused.");
        if (!ShaEqual(reviewedHeadSha, pullRequest.HeadSha))
            return RunOutcome.PermanentFailure("GitHub pull request head changed after Reviewer approval; human handoff was refused.");

        if (!pullRequest.IsDraft) return RunOutcome.Success();

        var ready = await github.RunAsync(workspace.Path, ["pr", "ready", pullRequest.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), "--repo", workspace.RepositoryIdentity], capability.ChildEnvironment, cancellationToken);
        return ready.ExitCode == 0
            ? RunOutcome.Success()
            : RunOutcome.RetryableFailure("GitHub ready-for-review mutation failed.");
    }

    private async Task<PullRequestSnapshot?> FindPullRequestAsync(Workspace workspace, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
    {
        var result = await github.RunAsync(workspace.Path, ["pr", "list", "--state", "open", "--head", workspace.Branch, "--repo", workspace.RepositoryIdentity, "--json", "number,isDraft,headRefName,headRefOid"], environment, cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException("Unable to inspect the assigned pull request for human handoff.");
        using var document = JsonDocument.Parse(result.StandardOutput);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() != 1) return null;
        var value = document.RootElement[0];
        var number = value.GetProperty("number").GetInt32();
        var isDraft = value.GetProperty("isDraft").GetBoolean();
        var branch = value.GetProperty("headRefName").GetString();
        var head = value.GetProperty("headRefOid").GetString();
        if (number <= 0 || !string.Equals(branch, workspace.Branch, StringComparison.Ordinal) || !IsSha(head)) return null;
        return new(number, isDraft, head!);
    }

    private static bool ShaEqual(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool IsSha(string? value) => value is { Length: 40 } && value.All(Uri.IsHexDigit);
    private sealed record PullRequestSnapshot(int Number, bool IsDraft, string HeadSha);
}
