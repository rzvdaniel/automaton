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
            "System.Net.Sockets.", "System.Net.NetworkInformation.", "System.Diagnostics.Process"];
        Assert.That(types, Does.Not.Contain("System.Reflection.Assembly"));
        foreach (var prefix in forbiddenPrefixes)
            Assert.That(types.Any(t => t.StartsWith(prefix, StringComparison.Ordinal)), Is.False, prefix);

        string[] allowedHttpReferences =
        [
            "System.Net.Http.ByteArrayContent", "System.Net.Http.HttpClient", "System.Net.Http.HttpCompletionOption",
            "System.Net.Http.HttpContent", "System.Net.Http.HttpMessageHandler", "System.Net.Http.HttpRequestException", "System.Net.Http.HttpMethod",
            "System.Net.Http.HttpRequestMessage", "System.Net.Http.HttpResponseMessage",
            "System.Net.HttpStatusCode", "System.Net.Http.Headers.MediaTypeHeaderValue",
            "System.Net.Http.Headers.HttpContentHeaders", "System.Net.Http.Headers.HttpHeaders",
            "System.Net.Http.Headers.HttpRequestHeaders"
        ];
        foreach (var type in types.Where(t => t.StartsWith("System.Net.", StringComparison.Ordinal)))
            Assert.That(allowedHttpReferences, Does.Contain(type), type);

        string[] allowedLocalStartupReferences =
        [
            "System.Environment", "System.IO.FileAccess", "System.IO.FileStream", "System.IO.InvalidDataException", "System.IO.Path",
            "System.IO.PathTooLongException", "System.IO.IOException", "System.IO.Stream",
            "System.IO.TextWriter", "System.Console",
            "System.Runtime.InteropServices.Architecture", "System.Runtime.InteropServices.Marshal",
            "System.Runtime.InteropServices.RuntimeInformation", "System.Runtime.InteropServices.SafeHandle",
            "System.Runtime.InteropServices.UnmanagedType",
            "System.Buffers.Binary.BinaryPrimitives", "Microsoft.Win32.SafeHandles.SafeFileHandle"
        ];
        foreach (var type in types.Where(t => t.StartsWith("System.IO.", StringComparison.Ordinal)
            || t.StartsWith("System.Runtime.InteropServices.", StringComparison.Ordinal)
            || t.StartsWith("System.Buffers.Binary.", StringComparison.Ordinal)
            || t.StartsWith("Microsoft.Win32.SafeHandles.", StringComparison.Ordinal)
            || t == "System.Environment"))
            Assert.That(allowedLocalStartupReferences, Does.Contain(type), type);
    }
}
