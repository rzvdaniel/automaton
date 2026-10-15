namespace Automaton.Api.Tests;

public sealed class RepositoryAgentGuidanceTests
{
    [Test]
    public void RepositoryGuidanceUsesOnlyTheCanonicalOwnedSkillSurface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var agentsDirectory = Path.Combine(repositoryRoot, ".agents");
        var ownedSkills = new[] { "admin-docs", "generic-runtime", "production-deployment" };

        Assert.That(Directory.Exists(Path.Combine(repositoryRoot, ".agent")), Is.False);

        foreach (var obsoleteDirectory in new[] { "agents", "harness", "maps", "instructions", "docs" })
        {
            Assert.That(Directory.Exists(Path.Combine(agentsDirectory, obsoleteDirectory)), Is.False);
        }

        var guidanceFiles = new[] { Path.Combine(repositoryRoot, "AGENTS.md") }
            .Concat(ownedSkills.Select(skill => Path.Combine(agentsDirectory, "skills", skill, "SKILL.md")))
            .ToArray();

        foreach (var path in guidanceFiles)
        {
            Assert.That(File.Exists(path), Is.True, path);
        }

        var guidance = string.Join("\n", guidanceFiles.Select(File.ReadAllText));
        foreach (var forbiddenText in new[]
        {
            "source/frontend",
            "source/backend",
            "live/frontend",
            "live/backend",
            "FastAPI",
            "Vite",
            "Keep frontend and backend surfaces separate"
        })
        {
            Assert.That(guidance, Does.Not.Contain(forbiddenText), forbiddenText);
        }

        Assert.That(guidance, Does.Contain("Symphony"));
        Assert.That(guidance, Does.Contain("Do not add manager/developer/reviewer role prompts"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
