using NUnit.Framework;
using Automaton.Runner.Console.Services;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class HostContainerBuildFailureOutputTests
{
    [Test]
    public void LongOutput_KeepsOnlyTheLastLines()
    {
        var output = string.Join('\n', Enumerable.Range(1, 200).Select(i => $"line {i}"));
        var message = HostContainerBuildExecutor.BuildFailureOutput(output, "tar: No space left on device", 2);
        Assert.That(message, Does.Contain("exited with code 2"));
        Assert.That(message, Does.Contain("line 200"));
        Assert.That(message, Does.Contain("No space left on device"));
        Assert.That(message, Does.Not.Contain("line 170"));
        Assert.That(message.Length, Is.LessThan(2700));
    }

    [Test]
    public void NoOutput_SaysSo()
        => Assert.That(HostContainerBuildExecutor.BuildFailureOutput("", "", 1), Does.Contain("printed nothing"));
}
