using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Automaton.Node.Preparation;
using NUnit.Framework;

namespace Automaton.Node.Tests;

[TestFixture]
public sealed class NodePreparationDependencyTests
{
    [Test]
    public void CompiledNodeHasNoOperationalOrTenantTypeDependencies()
    {
        using var stream = File.OpenRead(typeof(NodePreparationHost).Assembly.Location);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        var types = metadata.TypeReferences.Select(handle => metadata.GetTypeReference(handle))
            .Select(type => metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name)).ToArray();
        string[] forbiddenPrefixes = ["Automaton.Runner.", "Automaton.Steps.", "Automaton.Core.Scripting.",
            "Automaton.Api.", "Automaton.Infrastructure.", "Automaton.Worker.", "RabbitMQ.", "Docker.",
            "System.Net.", "System.IO.", "System.Diagnostics.Process", "System.Environment"];
        Assert.That(types, Does.Not.Contain("System.Reflection.Assembly"));
        foreach (var prefix in forbiddenPrefixes)
            Assert.That(types.Any(t => t.StartsWith(prefix, StringComparison.Ordinal)), Is.False, prefix);
    }
}
