using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerApplicationBuildExecutorTests
{
    [Test]
    public async Task ExecuteAsync_UsesDeclaredBuildImageAndOpaqueCommand()
    {
        var runtime = new RecordingContainerRuntime();
        var executor = new RunnerApplicationBuildExecutor(runtime);
        var build = new ApplicationBuildSpec
        {
            Image = "node:22-bookworm-slim",
            Command = new ApplicationCommandSpec
            {
                Executable = "npm",
                Arguments = ["ci"],
                WorkingDirectory = "client",
                Environment = new Dictionary<string, string> { ["BUILD_MODE"] = "test" }
            }
        };

        var result = await executor.ExecuteAsync(
            build,
            new ApplicationRuntimeSpec { Image = "runtime:1" },
            Path.GetTempPath());

        Assert.That(result.Succeeded, Is.True);
        Assert.That(runtime.Build, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.Build!.Image, Is.EqualTo("node:22-bookworm-slim"));
            Assert.That(runtime.Build.Command.Executable, Is.EqualTo("npm"));
            Assert.That(runtime.Build.Command.Arguments, Is.EqualTo(new[] { "ci" }));
            Assert.That(runtime.Environment["BUILD_MODE"], Is.EqualTo("test"));
        });
    }

    [Test]
    public async Task ExecuteAsync_UsesRuntimeImageWhenBuildImageIsOmitted()
    {
        var runtime = new RecordingContainerRuntime();
        var executor = new RunnerApplicationBuildExecutor(runtime);

        var result = await executor.ExecuteAsync(
            new ApplicationBuildSpec { Command = new ApplicationCommandSpec { Executable = "build-tool" } },
            new ApplicationRuntimeSpec { Image = "runtime:1" },
            Path.GetTempPath());

        Assert.That(result.Succeeded, Is.True);
        Assert.That(runtime.Build!.Image, Is.EqualTo("runtime:1"));
    }

    private sealed class RecordingContainerRuntime : IApplicationContainerRuntime
    {
        public DockerBuildRequest? Build { get; private set; }
        public IReadOnlyDictionary<string, string> Environment { get; private set; } = new Dictionary<string, string>();

        public Task<DockerImageIdentity> ResolveImageAsync(string image, CancellationToken cancellationToken = default)
            => Task.FromResult(new DockerImageIdentity(image, image + "@sha256:" + new string('a', 64)));

        public Task RunBuildAsync(DockerBuildRequest request, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken = default)
        {
            Build = request;
            Environment = environment;
            return Task.CompletedTask;
        }

        public Task<DockerApplicationContainer> StartApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new DockerApplicationContainer("container", request.Name, new DockerImageIdentity(request.Image, request.Image + "@sha256:" + new string('a', 64))));

        public Task StopAndRemoveAsync(string containerIdOrName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> IsRunningAsync(string containerIdOrName, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<DockerContainerIdentity> InspectAsync(string containerIdOrName, CancellationToken cancellationToken = default)
            => Task.FromResult(new DockerContainerIdentity(containerIdOrName, false, false, new Dictionary<string, string>()));
        public Task<string> GetLogsAsync(string containerIdOrName, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
    }
}
