using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class ServerUrlPersistenceTests
{
    private FileStorageService storageService;
    private ConfigurationService configService;
    private string tempConfigDir;

    [SetUp]
    public void Setup()
    {
        // Create a temporary directory for test storage
        tempConfigDir = Path.Combine(Path.GetTempPath(), "AutomatonRunnerTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempConfigDir);

        // Create test storage service that uses our temp directory
        storageService = new TestFileStorageService(tempConfigDir);

        // Create test configuration
        var appConfigOptions = Options.Create(new AppConfig());
        var configuration = new ConfigurationBuilder().Build();
        
        configService = new ConfigurationService(appConfigOptions, configuration, storageService);
    }

    [TearDown]
    public void Cleanup()
    {
        if (Directory.Exists(tempConfigDir))
        {
            Directory.Delete(tempConfigDir, true);
        }
    }
}

// Test implementation that allows us to control the storage directory
public class TestFileStorageService : FileStorageService
{
    private readonly string configDirectory;
    private readonly string serverConfigFile;

    public TestFileStorageService(string testConfigDir)
    {
        configDirectory = testConfigDir;
        serverConfigFile = Path.Combine(configDirectory, "server.json");
    }

    public new async Task SaveServerConfigAsync(string serverUrl)
    {
        var config = new
        {
            ServerUrl = serverUrl,
            SavedAt = DateTime.UtcNow
        };

        var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(serverConfigFile, json);
    }

    public new async Task<string?> LoadServerUrlAsync()
    {
        if (!File.Exists(serverConfigFile))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(serverConfigFile);
            var config = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            return config.TryGetProperty("ServerUrl", out var url) ? url.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public new Task DeleteServerConfigAsync()
    {
        if (File.Exists(serverConfigFile))
        {
            File.Delete(serverConfigFile);
        }
        return Task.CompletedTask;
    }
}