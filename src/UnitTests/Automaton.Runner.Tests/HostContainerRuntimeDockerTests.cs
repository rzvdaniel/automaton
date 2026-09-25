using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class HostContainerRuntimeDockerTests
{
    [Test, Explicit("Creates disposable resources on a local Docker Desktop daemon. Requires a cached debian:bookworm-slim image.")]
    public async Task LocalDockerCreateStartInspectLogsAndRemove()
    {
        await HostContainerRuntimeDockerAcceptance.RunAsync();
    }
}
