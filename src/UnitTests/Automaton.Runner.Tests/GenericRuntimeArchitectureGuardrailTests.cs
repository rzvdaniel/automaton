using System.Reflection;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class GenericRuntimeArchitectureGuardrailTests
{
    private static readonly Type[] GenericContractTypes =
    [
        typeof(ApplicationContract),
        typeof(ApplicationBuildSpec),
        typeof(ApplicationRuntimeSpec),
        typeof(ApplicationHealthSpec),
        typeof(ApplicationStorageSpec),
        typeof(ApplicationStorageMount),
        typeof(ApplicationCommandSpec)
    ];

    private static readonly string[] LegacyTerms =
    [
        "Frontend",
        "Backend",
        "Python",
        "Vue",
        "FastApi",
        "Language",
        "Framework",
        "RuntimeType"
    ];

    [Test]
    public void GenericContractAssembly_DoesNotReferenceControlPlaneOrRunnerAssemblies()
    {
        var references = typeof(ApplicationContract).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.That(references, Does.Not.Contain("Automaton.Api"));
        Assert.That(references, Does.Not.Contain("Automaton.Infrastructure"));
        Assert.That(references, Does.Not.Contain("Automaton.Runner"));
    }

    [Test]
    public void GenericContractTypes_DoNotExposeTechnologySpecificFields()
    {
        var members = GenericContractTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToArray();

        foreach (var term in LegacyTerms)
            Assert.That(members, Has.None.Contains(term), $"Found legacy term '{term}'.");
    }

    [TestCase(typeof(RunnerDeploymentService))]
    [TestCase(typeof(RunnerRuntimeProcessManager))]
    [TestCase(typeof(RunnerRuntimeContainerManager))]
    public void GenericRunnerServices_UseTheWorkspaceBoundaryAndAvoidLegacyDependencies(Type serviceType)
    {
        var constructorTypes = serviceType
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.That(constructorTypes, Does.Contain(typeof(RunnerWorkspaceService)));

        var dependencyNames = constructorTypes
            .Select(type => type.FullName ?? type.Name)
            .Concat(serviceType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(member => member.Name))
            .ToArray();

        foreach (var term in LegacyTerms)
            Assert.That(dependencyNames, Has.None.Contains(term), $"Found legacy term '{term}'.");
    }
}
