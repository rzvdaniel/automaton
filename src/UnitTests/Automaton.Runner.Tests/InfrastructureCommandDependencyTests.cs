using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class InfrastructureCommandDependencyTests
{
    [Test]
    public void RunnerAssemblyHasNoCapOrDatabaseDependency()
    {
        var runnerAssembly = typeof(InfrastructureCommandConsumer).Assembly;
        var references = runnerAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.That(references, Does.Contain("RabbitMQ.Client"));
        Assert.That(
            references.Any(name => name!.Contains("CAP") || name.Contains("MySql") || name.Contains("EntityFramework")),
            Is.False);
        Assert.That(typeof(InfrastructureCommandConsumer).GetConstructors().Any(constructor =>
            constructor.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .SequenceEqual(new[]
                {
                    typeof(InfrastructureCommandIdentity),
                    typeof(IInfrastructureActionExecutor),
                    typeof(IInfrastructureCommandResultClient)
                })), Is.True);
    }
}
