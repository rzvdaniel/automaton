using Automaton.Core.Models;

namespace Automaton.Core.Tests;

public class ApplicationContractDescriptorTests
{
    private static IReadOnlyList<string> Check(Action<ApplicationContract> change)
    {
        var contract = ApplicationContractDefaults.Create();
        change(contract);
        return ApplicationContractValidator.Validate(contract);
    }

    [Test]
    public void AllFieldsAreOptional() => Assert.That(Check(_ => { }), Is.Empty);

    [Test]
    public void AcceptsValidFields() => Assert.That(Check(c =>
    {
        c.Kind = "api"; c.Description = "A one line summary"; c.OpenApi = "/openapi.json";
        c.Pages = [new() { Path = "/", Label = "Home" }, new() { Path = "/admin", Label = "Admin" }];
    }), Is.Empty);

    [TestCase("mobile")]
    [TestCase("Web")]
    public void RejectsUnknownKind(string kind) => Assert.That(Check(c => c.Kind = kind), Has.Some.Contain("kind"));

    [Test]
    public void RejectsLongOrMultilineDescription()
    {
        Assert.That(Check(c => c.Description = new string('a', 201)), Is.Not.Empty);
        Assert.That(Check(c => c.Description = "a\nb"), Is.Not.Empty);
    }

    [Test]
    public void RejectsBadPagesAndOpenApi()
    {
        Assert.That(Check(c => c.Pages = [new() { Path = "home", Label = "Home" }]), Is.Not.Empty);
        Assert.That(Check(c => c.Pages = [new() { Path = "/", Label = "" }]), Is.Not.Empty);
        Assert.That(Check(c => c.Pages = Enumerable.Range(0, 11).Select(i => new ApplicationPageDeclaration { Path = "/" + i, Label = "x" }).ToList()), Is.Not.Empty);
        Assert.That(Check(c => c.OpenApi = "openapi.json"), Is.Not.Empty);
        Assert.That(Check(c => c.OpenApi = "/../x"), Is.Not.Empty);
        Assert.That(Check(c => c.OpenApi = "//evil.example/o.json"), Is.Not.Empty);
        Assert.That(Check(c => c.Pages = [new() { Path = "//evil.example", Label = "x" }]), Is.Not.Empty);
        Assert.That(Check(c => c.Pages = [new() { Path = "/a?b#c", Label = "x" }]), Is.Not.Empty);
        Assert.That(Check(c => c.Description = "a\tb"), Is.Not.Empty);
    }

    [Test]
    public void ActionDescriptionIsLimited()
    {
        var yaml = "apiVersion: automaton.dev/v1\nkind: Action\nmetadata:\n  description: " + new string('a', 201) + "\nspec:\n  runtime: bash\n  entrypoint: run.sh\n";
        Assert.Throws<InvalidOperationException>(() => AppActionManifestParser.Parse(yaml, "x"));
        Assert.That(AppActionManifestParser.Parse(yaml.Replace(new string('a', 201), "ok"), "x"), Is.Not.Null);
    }

    [Test]
    public void RoundTripsThroughJson()
    {
        var c = ApplicationContractDefaults.Create();
        c.Kind = "api"; c.OpenApi = "/openapi.json"; c.Pages = [new() { Path = "/", Label = "Home" }];
        var back = ApplicationContractSerialization.Deserialize(ApplicationContractSerialization.Serialize(c));
        Assert.That(back.Kind, Is.EqualTo("api"));
        Assert.That(back.OpenApi, Is.EqualTo("/openapi.json"));
        Assert.That(back.Pages, Has.Count.EqualTo(1));
    }
}
