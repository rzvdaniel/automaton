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
