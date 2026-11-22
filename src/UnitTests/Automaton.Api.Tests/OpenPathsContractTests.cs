using Automaton.Core.Models;

namespace Automaton.Api.Tests;

[TestFixture]
public class OpenPathsContractTests
{
    [TestCase("/hooks/*", true)]
    [TestCase("/callback", true)]
    [TestCase("/a/b-c_d.e", true)]
    [TestCase("/", false)]
    [TestCase("/*", false)]
    [TestCase("//*", false)]
    [TestCase("/a//b", false)]
    [TestCase("/a%2e", false)]
    [TestCase("hooks", false)]
    [TestCase("/a/../b", false)]
    [TestCase("/a b", false)]
    [TestCase("/a`b", false)]
    [TestCase("/a*b", false)]
    [TestCase("", false)]
    public void IsValidOpenPath_AcceptsOnlyPlainPathsWithOptionalTrailingStar(string path, bool expected)
        => Assert.That(ApplicationContractValidator.IsValidOpenPath(path), Is.EqualTo(expected));

    [TestCase("required", "Jobs check in from outside.", 0)]
    [TestCase("recommended", "A tracking script is loaded from other sites.", 0)]
    [TestCase("always", "Reason.", 1)]
    [TestCase("required", " ", 1)]
    public void Validate_ChecksThePublicAccessHint(string level, string reason, int errorCount)
    {
        var contract = ApplicationContractDefaults.Create();
        contract.PublicAccess = new ApplicationPublicAccessDeclaration { Level = level, Reason = reason };

        Assert.That(ApplicationContractValidator.Validate(contract).Count(error => error.StartsWith("publicAccess")), Is.EqualTo(errorCount));
    }

    [Test]
    public void PublicAccess_IsOptionalAndSurvivesASerializationRoundTrip()
    {
        var plain = ApplicationContractDefaults.Create();
        Assert.That(ApplicationContractSerialization.Deserialize(ApplicationContractSerialization.Serialize(plain)).PublicAccess, Is.Null);

        plain.PublicAccess = new ApplicationPublicAccessDeclaration { Level = "recommended", Reason = "Sites send page views here." };
        var again = ApplicationContractSerialization.Deserialize(ApplicationContractSerialization.Serialize(plain));

        Assert.That(again.PublicAccess!.Level, Is.EqualTo("recommended"));
        Assert.That(again.PublicAccess.Reason, Is.EqualTo("Sites send page views here."));
    }

    [Test]
    public void Validate_ReportsBadAndDuplicateOpenPaths()
    {
        var contract = ApplicationContractDefaults.Create();
        contract.OpenPaths = ["/hooks/*", "/hooks/*", "nope"];

        var errors = ApplicationContractValidator.Validate(contract);

        Assert.That(errors, Has.Some.Contain("more than once"));
        Assert.That(errors, Has.Some.Contain("'nope'"));
    }
}
