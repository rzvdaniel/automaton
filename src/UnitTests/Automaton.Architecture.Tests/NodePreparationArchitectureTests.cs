using System.Xml.Linq;
using NUnit.Framework;

namespace Automaton.Architecture.Tests;

[TestFixture]
public sealed class NodePreparationArchitectureTests
{
    [Test]
    public void NodeProjectGraphStaysInsideNarrowManagementBoundary()
    {
        var node = Path.Combine(FindRepositoryRoot(), "src", "Automaton.Node", "Automaton.Node.csproj");
        string[] allowedProjects = ["Automaton.Node", "Automaton.Core", "Automaton.Messaging", "Automaton.Execution.Management"];
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(node);
        while (pending.TryPop(out var path))
        {
            if (!visited.Add(path)) continue;
            Assert.That(allowedProjects, Does.Contain(Path.GetFileNameWithoutExtension(path)), path);
            foreach (var reference in XDocument.Load(path).Descendants("ProjectReference"))
                pending.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar))));
        }
    }

    [Test]
    public void PreparationHasOnlyGenericHostingPackageAndNoBundledRuntimePayloads()
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "Automaton.Node", "Automaton.Node.csproj");
        var project = XDocument.Load(path);
        Assert.That(project.Descendants("PackageReference").Select(p => p.Attribute("Include")!.Value),
            Is.EqualTo(new[] { "Microsoft.Extensions.Hosting" }));
        Assert.That(project.Descendants("Content"), Is.Empty);
        Assert.That(project.Descendants("Compile").Where(p => p.Attribute("Include") is not null), Is.Empty);
    }

    [Test]
    public void AllSolutionContainsNodeAndItsTests()
    {
        var solution = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Automaton.All.sln"));
        Assert.That(solution, Does.Contain("src\\Automaton.Node\\Automaton.Node.csproj"));
        Assert.That(solution, Does.Contain("src\\UnitTests\\Automaton.Node.Tests\\Automaton.Node.Tests.csproj"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
