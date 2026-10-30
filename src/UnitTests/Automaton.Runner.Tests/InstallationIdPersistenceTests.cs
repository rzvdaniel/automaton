using Automaton.Runner.Console.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class InstallationIdPersistenceTests
{
    private string tempConfigDirectory = null!;
    private FileStorageService storage = null!;

    [SetUp]
    public void SetUp()
    {
        tempConfigDirectory = Path.Combine(Path.GetTempPath(), "AutomatonRunnerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempConfigDirectory);
        storage = new FileStorageService(tempConfigDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempConfigDirectory))
        {
            Directory.Delete(tempConfigDirectory, recursive: true);
        }
    }

    [Test]
    public async Task GetOrCreateInstallationIdAsync_PrefersEnrolledProfileAndRepairsStaleFile()
    {
        const string enrolledId = "sh_enrollment-issued";
        await File.WriteAllTextAsync(Path.Combine(tempConfigDirectory, "installation-id"), "ri_stale-local-id");
        await storage.SaveInstallationProfileAsync(new RunnerInstallationProfile { InstallationId = enrolledId });

        var actualId = await storage.GetOrCreateInstallationIdAsync();

        Assert.That(actualId, Is.EqualTo(enrolledId));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(tempConfigDirectory, "installation-id")), Is.EqualTo(enrolledId));
    }

    [Test]
    public async Task GetOrCreateInstallationIdAsync_PreservesLegacyFileWhenNotEnrolled()
    {
        const string legacyId = "ri_existing-legacy-id";
        await File.WriteAllTextAsync(Path.Combine(tempConfigDirectory, "installation-id"), legacyId);

        var actualId = await storage.GetOrCreateInstallationIdAsync();

        Assert.That(actualId, Is.EqualTo(legacyId));
    }
}
