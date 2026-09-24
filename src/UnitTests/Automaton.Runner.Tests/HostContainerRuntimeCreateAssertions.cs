using Automaton.Core.Models;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

internal static class HostContainerRuntimeCreateAssertions
{
    public static void Verify(
        HostContainerScope scope,
        HostContainerExecutionRequest request,
        HostContainerExecutionResponse result,
        IReadOnlyList<IReadOnlyList<string>> calls,
        string digest)
    {
        Assert.That(result.Success, Is.True, result.Error);
        var args = calls.Single(x => x[1] == "create").ToList();
        Assert.That(result.HostPort, Is.Null);
        Assert.That(calls.Any(x => x[1] == "start"), Is.False);
        Assert.That(result.RequestedImage, Is.EqualTo(digest));
        Assert.That(result.ResolvedImage, Is.EqualTo(digest));
        VerifyCreateArguments(scope, request, args, digest);
        VerifyMounts(scope, request, args);
    }

    private static void VerifyCreateArguments(
        HostContainerScope scope,
        HostContainerExecutionRequest request,
        IReadOnlyList<string> args,
        string digest)
    {
        Assert.That(args[args.ToList().IndexOf("--publish") + 1], Is.EqualTo("172.17.0.1::8080"));
        Assert.That(args, Does.Contain("no-new-privileges"));
        Assert.That(args[args.ToList().IndexOf("--cap-drop") + 1], Is.EqualTo("ALL"));
        Assert.That(args[args.ToList().IndexOf("--network") + 1], Is.EqualTo("bridge"));
        Assert.That(args[args.ToList().IndexOf("--cgroup-parent") + 1], Is.EqualTo($"automaton-hosted-runner-{scope.HostedRunnerId:N}.slice"));
        Assert.That(args[args.ToList().IndexOf("--workdir") + 1], Is.EqualTo("/automaton/app/output"));
        Assert.That(args[args.ToList().IndexOf("--entrypoint") + 1], Is.EqualTo("/automaton/app/scripts/serve"));
        Assert.That(args.TakeLast(3), Is.EqualTo(new[] { digest, "space separated", "--flag" }));
        Assert.That(args, Does.Contain("MODE=request"));
        Assert.That(args, Does.Contain("AUTOMATON_PORT=8080"));
        Assert.That(args, Does.Contain("AUTOMATON_APP_DATA_DIR=/automaton/data"));
        Assert.That(args, Does.Contain($"AUTOMATON_APP_ID={scope.AppId:D}"));
        Assert.That(args, Does.Contain($"AUTOMATON_DEPLOYMENT_TARGET_ID={scope.DeploymentTargetId:D}"));
        Assert.That(args, Does.Contain("AUTOMATON_STORAGE_UPLOADS=/automaton/data/uploads"));
        var commandLine = string.Join(' ', args);
        Assert.That(commandLine, Does.Not.Contain("docker.sock").And.Not.Contain("/host"));
        Assert.That(args, Does.Not.Contain("--privileged").And.Not.Contain("--pid"));
    }

    private static void VerifyMounts(
        HostContainerScope scope,
        HostContainerExecutionRequest request,
        IReadOnlyList<string> args)
    {
        var mounts = args.Where((_, index) => index > 0 && args[index - 1] == "--mount").ToArray();
        Assert.That(mounts, Has.Length.EqualTo(3));
        Assert.That(mounts[0], Does.Contain(
            $"volume-subpath=workspace/apps/{scope.AppId:D}/targets/{scope.DeploymentTargetId:D}/artifacts/candidates/{request.CandidateId:D}/application"));
        Assert.That(mounts[0], Does.Contain("\"target=/automaton/app\"").And.Contain("\"readonly\""));
        Assert.That(mounts[1], Does.Contain($"volume-subpath=workspace/data/{scope.AppId:D}/database").And.Not.Contain("readonly"));
        Assert.That(mounts[2], Does.Contain($"volume-subpath=workspace/data/{scope.AppId:D}/media/files").And.Not.Contain("readonly"));
        Assert.That(mounts.All(x => x.Contains($"source=automaton-hosted-runner-{scope.HostedRunnerId:N}-user")), Is.True);
    }
}
