using Automaton.Runner.Console.Services;
using Automaton.Core.Models;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class DockerContainerRuntimeBoundaryTests
{
    [Test]
    public void HostPathTranslationUsesTheActualInfrastructureRunnerMountShape()
    {
        var translator = new RunnerManagedHostPathTranslator(
            "/automaton/workspace",
            "/var/lib/automaton/infrastructure-runner/workflows");

        Assert.That(
            translator.Translate("/automaton/workspace/apps/x/runtime"),
            Is.EqualTo("/var/lib/automaton/infrastructure-runner/workflows/apps/x/runtime"));
    }

    [Test]
    public void HostPathTranslationUsesTheGeneratedPrivilegedHostWorkspaceMountShape()
    {
        var translator = new RunnerManagedHostPathTranslator(
            "/automaton/workspace",
            "/opt/automaton/self-hosted-runners/sh_acceptance/workspace");

        Assert.That(
            translator.Translate("/automaton/workspace/apps/x/runtime"),
            Is.EqualTo("/opt/automaton/self-hosted-runners/sh_acceptance/workspace/apps/x/runtime"));
    }

    [Test]
    public void HostPathTranslationRejectsPathsOutsideTheManagedWorkspace()
    {
        var translator = new RunnerManagedHostPathTranslator(
            "/automaton/workspace",
            "/var/lib/automaton/infrastructure-runner/workflows");

        Assert.Throws<UnauthorizedAccessException>(() => translator.Translate("/etc/passwd"));
    }

    [Test]
    public void StructuredCommandUsesExplicitExecutableEntrypoint()
    {
        var args = new List<string>();
        var command = new ApplicationCommandSpec { Executable = "bin/server", Arguments = ["--port", "8080"] };

        DockerContainerRuntime.AddEntrypoint(args, command, "/automaton/app");
        DockerContainerRuntime.AddCommandArguments(args, command);

        Assert.That(args, Is.EqualTo(new[] { "--entrypoint", "/automaton/app/bin/server", "--port", "8080" }));
    }

    [Test]
    public void ShellCommandUsesShEntrypointAndSingleQuotedCommand()
    {
        var args = new List<string>();
        var command = new ApplicationCommandSpec
        {
            Executable = "server --mode 'safe'",
            Arguments = ["--flag"],
            UseShell = true
        };

        DockerContainerRuntime.AddEntrypoint(args, command, "/automaton/app");
        DockerContainerRuntime.AddCommandArguments(args, command);

        Assert.That(args, Is.EqualTo(new[] { "--entrypoint", "/bin/sh", "-c", @"'server --mode '\''safe'\''' '--flag'" }));
    }

    [Test]
    public async Task ApplicationPublishDefaultsToLoopback()
    {
        var previous = Environment.GetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable);
        Environment.SetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable, null);
        var arguments = new List<string>();
        var runtime = new DockerContainerRuntime((args, _, _, _) =>
        {
            arguments.AddRange(args);
            if (args.Contains("pull"))
                return Task.FromResult(new DockerContainerRuntime.CommandResult(0, "", ""));
            if (args.Contains("image"))
                return Task.FromResult(new DockerContainerRuntime.CommandResult(0, "runtime:1@sha256:" + new string('a', 64), ""));
            return Task.FromResult(new DockerContainerRuntime.CommandResult(0, "container-1", ""));
        });

        try
        {
            await runtime.CreateApplicationAsync(new DockerApplicationContainerRequest(
                "app", "runtime:1", Path.GetTempPath(), ".", new ApplicationCommandSpec { Executable = "server" },
                8080, 18080, Path.GetTempPath(), new Dictionary<string, string>(), Guid.NewGuid(), Guid.NewGuid()));
            Assert.That(arguments, Does.Contain("127.0.0.1:18080:8080"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable, previous);
        }
    }

    [Test]
    public async Task ApplicationPublishUsesConfiguredAddress()
    {
        var previous = Environment.GetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable);
        Environment.SetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable, "172.30.0.1");
        var arguments = new List<string>();
        var runtime = new DockerContainerRuntime((args, _, _, _) =>
        {
            arguments.AddRange(args);
            if (args.Contains("pull"))
                return Task.FromResult(new DockerContainerRuntime.CommandResult(0, "", ""));
            if (args.Contains("image"))
                return Task.FromResult(new DockerContainerRuntime.CommandResult(0, "runtime:1@sha256:" + new string('a', 64), ""));
            return Task.FromResult(new DockerContainerRuntime.CommandResult(0, "container-1", ""));
        });

        try
        {
            await runtime.CreateApplicationAsync(new DockerApplicationContainerRequest(
                "app", "runtime:1", Path.GetTempPath(), ".", new ApplicationCommandSpec { Executable = "server" },
                8080, 18080, Path.GetTempPath(), new Dictionary<string, string>(), Guid.NewGuid(), Guid.NewGuid()));
            Assert.That(arguments, Does.Contain("172.30.0.1:18080:8080"));
            Assert.That(arguments, Does.Not.Contain("0.0.0.0:18080:8080"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable, previous);
        }
    }

    [Test]
    public void RuntimeHostAddressRejectsMalformedExplicitValue()
    {
        var previous = Environment.GetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable);
        Environment.SetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable, "not-an-ip");
        try
        {
            Assert.Throws<InvalidOperationException>(() => _ = RunnerRuntimeEndpointConfiguration.HostAddress);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable, previous);
        }
    }

    [Test]
    public void RuntimeHostAddressRejectsWildcardValue()
    {
        var previous = Environment.GetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable);
        Environment.SetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable, "0.0.0.0");
        try
        {
            Assert.Throws<InvalidOperationException>(() => _ = RunnerRuntimeEndpointConfiguration.HostAddress);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable, previous);
        }
    }

    [Test]
    public void RuntimeHostAddressDefaultsToLoopback()
    {
        var previous = Environment.GetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable);
        Environment.SetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable, null);
        try
        {
            Assert.That(RunnerRuntimeEndpointConfiguration.HostAddress, Is.EqualTo("127.0.0.1"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable, previous);
        }
    }

    [Test]
    public async Task InspectTreatsOnlyRecognizedMissingContainerErrorsAsAbsent()
    {
        var runtime = new DockerContainerRuntime((_, _, _, _) => Task.FromResult(
            new DockerContainerRuntime.CommandResult(1, string.Empty, "Error: No such object: container-1")));

        var identity = await runtime.InspectAsync("container-1");

        Assert.That(identity.Exists, Is.False);
    }

    [Test]
    public void InspectSurfacesDockerDaemonFailuresInsteadOfReportingAbsence()
    {
        var runtime = new DockerContainerRuntime((_, _, _, _) => Task.FromResult(
            new DockerContainerRuntime.CommandResult(1, string.Empty, "permission denied connecting to Docker daemon")));

        Assert.ThrowsAsync<InvalidOperationException>(() => runtime.InspectAsync("container-1"));
    }

    [Test]
    public void InspectRejectsMalformedSuccessfulOutput()
    {
        var runtime = new DockerContainerRuntime((_, _, _, _) => Task.FromResult(
            new DockerContainerRuntime.CommandResult(0, "container-1|maybe|label=value", string.Empty)));

        Assert.ThrowsAsync<InvalidOperationException>(() => runtime.InspectAsync("container-1"));
    }

    [Test]
    public void IsRunningRejectsMalformedSuccessfulOutput()
    {
        var runtime = new DockerContainerRuntime((_, _, _, _) => Task.FromResult(
            new DockerContainerRuntime.CommandResult(0, "", string.Empty)));

        Assert.ThrowsAsync<InvalidOperationException>(() => runtime.IsRunningAsync("container-1"));
    }

    [Test]
    public async Task InspectParsesOwnershipLabelsWhenUnrelatedLabelsContainDelimiters()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var runtime = new DockerContainerRuntime((_, _, _, _) => Task.FromResult(
            new DockerContainerRuntime.CommandResult(
                0,
                $"container-1|true|{{\"com.automaton.managed\":\"true\",\"com.automaton.app-id\":\"{appId}\",\"com.automaton.deployment-target-id\":\"{targetId}\",\"com.automaton.runtime-kind\":\"generic\",\"image.label\":\"a;b|c=d\"}}",
                string.Empty)));

        var identity = await runtime.InspectAsync("container-1");

        Assert.Multiple(() =>
        {
            Assert.That(identity.Running, Is.True);
            Assert.That(identity.Labels["com.automaton.app-id"], Is.EqualTo(appId.ToString()));
            Assert.That(identity.Labels["image.label"], Is.EqualTo("a;b|c=d"));
        });
    }

    [Test]
    public void ApplicationStartCleanupFailurePreservesStartFailure()
    {
        var runtime = new DockerContainerRuntime((arguments, _, _, _) =>
        {
            if (arguments.Contains("pull"))
                return Task.FromResult(new DockerContainerRuntime.CommandResult(0, string.Empty, string.Empty));
            if (arguments.Contains("image"))
                return Task.FromResult(new DockerContainerRuntime.CommandResult(0, "runtime:1@sha256:" + new string('a', 64), string.Empty));
            if (arguments.Contains("create"))
                return Task.FromResult(new DockerContainerRuntime.CommandResult(0, "container-1", string.Empty));
            if (arguments.Contains("start"))
                return Task.FromResult(new DockerContainerRuntime.CommandResult(1, string.Empty, "start failed"));
            return Task.FromResult(new DockerContainerRuntime.CommandResult(1, string.Empty, "cleanup failed"));
        });

        var error = Assert.ThrowsAsync<AggregateException>(() => runtime.StartApplicationAsync(
            new DockerApplicationContainerRequest(
                "app",
                "runtime:1",
                Path.GetTempPath(),
                ".",
                new ApplicationCommandSpec { Executable = "server" },
                8080,
                18080,
                Path.GetTempPath(),
                new Dictionary<string, string>(),
                Guid.NewGuid(),
                 Guid.NewGuid())))!;

        Assert.That(error.InnerExceptions[0].Message, Does.Contain("start failed"));
    }

    [Test]
    public void BuildCleanupFailurePreservesBuildFailure()
    {
        var runtime = new DockerContainerRuntime((arguments, _, _, _) =>
        {
            if (arguments.Contains("pull"))
                return Task.FromResult(new DockerContainerRuntime.CommandResult(0, string.Empty, string.Empty));
            if (arguments.Contains("image"))
                return Task.FromResult(new DockerContainerRuntime.CommandResult(0, "runtime:1@sha256:" + new string('a', 64), string.Empty));
            if (arguments.Contains("run"))
                return Task.FromResult(new DockerContainerRuntime.CommandResult(1, string.Empty, "build failed"));
            return Task.FromResult(new DockerContainerRuntime.CommandResult(1, string.Empty, "cleanup failed"));
        });

        var error = Assert.ThrowsAsync<AggregateException>(() => runtime.RunBuildAsync(
            new DockerBuildRequest("runtime:1", new ApplicationCommandSpec { Executable = "build" }, Path.GetTempPath(), 1),
            new Dictionary<string, string>()))!;

        Assert.That(error.InnerExceptions[0].Message, Does.Contain("build failed"));
    }
}
