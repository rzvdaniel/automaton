using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class GenericRuntimeContractTests
{
    [Test]
    public void GenericDeploymentBoundary_HasNoTechnologySelectorInTheCanonicalContract()
    {
        var canonicalContractTypes = new[]
        {
            typeof(ApplicationContract),
            typeof(ApplicationBuildSpec),
            typeof(ApplicationRuntimeSpec),
            typeof(ApplicationHealthSpec),
            typeof(ApplicationStorageSpec),
            typeof(ApplicationStorageMount),
            typeof(ApplicationCommandSpec)
        };
        var deploymentRequestProperties = typeof(DeployAppPackageRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        var technologySelectorNames = new[] { "Language", "Framework", "RuntimeType" };

        Assert.Multiple(() =>
        {
            foreach (var contractType in canonicalContractTypes)
            {
                var properties = contractType.GetProperties().Select(property => property.Name).ToArray();
                foreach (var selectorName in technologySelectorNames)
                    Assert.That(properties, Does.Not.Contain(selectorName), $"{contractType.Name} must stay stack-neutral.");
            }

            foreach (var selectorName in technologySelectorNames)
                Assert.That(deploymentRequestProperties, Does.Not.Contain(selectorName));
            Assert.That(typeof(RunnerDeploymentService).GetMethod(nameof(RunnerDeploymentService.DeployPackage)), Is.Not.Null);
        });
    }
}
