using Automaton.AgentOrchestrator.Hosting;
using Automaton.AgentOrchestrator.Operator;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class Stage13HostingTests
{
    [Test]
    public void BuildIdentitySeparatesVersionAndRevision()
    {
        Assert.That(AssemblyBuildIdentity.Parse("2.3.4+abcdef"), Is.EqualTo(new BuildIdentity("2.3.4", "abcdef")));
        Assert.That(AssemblyBuildIdentity.Parse("2.3.4").Revision, Is.EqualTo("unknown"));
    }

    [TestCase("Authorization: Bearer raw-secret")]
    [TestCase("https://user:raw-secret@example.test/path")]
    [TestCase("request failed?token=raw-secret")]
    public void OperatorDiagnosticsRedactCredentialShapedText(string value)
    {
        var result = FileOperatorTelemetry.SanitizeDiagnostic(value);
        Assert.That(result, Does.Not.Contain("raw-secret"));
    }
}
