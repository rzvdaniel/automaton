using Automaton.Runner.Console.Services;
using NUnit.Framework;
using System.Text.Json;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class RunnerRegistrationTests
{
    private TestRunnerStorageService storageService;
    private string tempConfigDir;

    [SetUp]
    public void Setup()
    {
        // Create a temporary directory for test storage
        tempConfigDir = Path.Combine(Path.GetTempPath(), "AutomatonRunnerTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempConfigDir);

        // Create test storage service that uses our temp directory
        storageService = new TestRunnerStorageService(tempConfigDir);
    }

    [TearDown]
    public void Cleanup()
    {
        if (Directory.Exists(tempConfigDir))
        {
            Directory.Delete(tempConfigDir, true);
        }
    }

    [Test]
    public async Task RunnerConfig_ShouldBeSaved_WhenRegistered()
    {
        // Arrange
        var runnerId = Guid.NewGuid().ToString();
        var runnerName = "Test-Runner";

        // Act
        await storageService.SaveRunnerConfigAsync(runnerId, runnerName);

        // Assert
        var savedId = storageService.GetRunnerId();
        var savedName = storageService.GetRunnerName();
        
        Assert.That(savedId, Is.EqualTo(runnerId));
        Assert.That(savedName, Is.EqualTo(runnerName));
        Assert.That(storageService.IsRunnerRegistered(), Is.True);
    }

    [Test]
    public async Task RunnerConfig_ShouldBeUpdated_WhenSavedWithSameId()
    {
        // Arrange
        var runnerId = Guid.NewGuid().ToString();
        var originalName = "Original-Runner";
        var updatedName = "Updated-Runner";

        // Act - Save initial config
        await storageService.SaveRunnerConfigAsync(runnerId, originalName);
        
        // Assert initial state
        Assert.That(storageService.GetRunnerName(), Is.EqualTo(originalName));
        
        // Act - Update with same ID, different name
        await storageService.SaveRunnerConfigAsync(runnerId, updatedName);

        // Assert - Name should be updated, ID should remain the same
        var savedId = storageService.GetRunnerId();
        var savedName = storageService.GetRunnerName();
        
        Assert.That(savedId, Is.EqualTo(runnerId));
        Assert.That(savedName, Is.EqualTo(updatedName));
    }

    [Test]
    public async Task RunnerConfig_ShouldBeCleared_WhenDeleted()
    {
        // Arrange
        var runnerId = Guid.NewGuid().ToString();
        var runnerName = "Test-Runner";
        await storageService.SaveRunnerConfigAsync(runnerId, runnerName);
        
        // Verify it's saved
        Assert.That(storageService.IsRunnerRegistered(), Is.True);

        // Act
        await storageService.DeleteRunnerConfigAsync();

        // Assert
        Assert.That(storageService.IsRunnerRegistered(), Is.False);
        Assert.That(storageService.GetRunnerId(), Is.Null);
        Assert.That(storageService.GetRunnerName(), Is.Null);
    }

    [Test]
    public async Task RunnerConfig_ShouldPersistRunnerId_AfterCleanupAndBeforeReRegistration()
    {
        // Arrange - Initial registration
        var originalRunnerId = Guid.NewGuid().ToString();
        var originalName = "Original-Runner";
        await storageService.SaveRunnerConfigAsync(originalRunnerId, originalName);

        // Act - Simulate what happens when user clears settings
        // In the real scenario, cleanup would delete the runner config
        // But the runner ID should be preserved for potential re-use
        var savedRunnerId = storageService.GetRunnerId()
            ?? throw new InvalidOperationException("The registered Runner ID should be retained before cleanup.");
        await storageService.DeleteRunnerConfigAsync();

        // Verify runner config is cleared
        Assert.That(storageService.IsRunnerRegistered(), Is.False);

        // Simulate re-registration with the preserved runner ID
        var newName = "New-Runner-Name";
        await storageService.SaveRunnerConfigAsync(savedRunnerId, newName);

        // Assert - Same runner ID should be used with new name
        Assert.That(storageService.GetRunnerId(), Is.EqualTo(originalRunnerId));
        Assert.That(storageService.GetRunnerName(), Is.EqualTo(newName));
    }

    [Test]
    public void RunnerConfig_ShouldReturnNull_WhenNotRegistered()
    {
        // Act & Assert
        Assert.That(storageService.GetRunnerId(), Is.Null);
        Assert.That(storageService.GetRunnerName(), Is.Null);
        Assert.That(storageService.IsRunnerRegistered(), Is.False);
    }
}

// Test storage service implementation
public class TestRunnerStorageService
{
    private readonly string configDirectory;
    private readonly string runnerConfigFile;

    public TestRunnerStorageService(string testConfigDir)
    {
        configDirectory = testConfigDir;
        runnerConfigFile = Path.Combine(configDirectory, "runner.json");
    }

    public async Task SaveRunnerConfigAsync(string runnerId, string runnerName)
    {
        var config = new
        {
            RunnerId = runnerId,
            RunnerName = runnerName,
            RegisteredAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(runnerConfigFile, json);
    }

    public string? GetRunnerId()
    {
        if (!File.Exists(runnerConfigFile))
            return null;

        try
        {
            var json = File.ReadAllText(runnerConfigFile);
            var config = JsonSerializer.Deserialize<JsonElement>(json);
            return config.TryGetProperty("RunnerId", out var id) ? id.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public string? GetRunnerName()
    {
        if (!File.Exists(runnerConfigFile))
            return null;

        try
        {
            var json = File.ReadAllText(runnerConfigFile);
            var config = JsonSerializer.Deserialize<JsonElement>(json);
            return config.TryGetProperty("RunnerName", out var name) ? name.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public bool IsRunnerRegistered()
    {
        return !string.IsNullOrEmpty(GetRunnerId()) && !string.IsNullOrEmpty(GetRunnerName());
    }

    public Task DeleteRunnerConfigAsync()
    {
        if (File.Exists(runnerConfigFile))
        {
            File.Delete(runnerConfigFile);
        }
        return Task.CompletedTask;
    }
}
