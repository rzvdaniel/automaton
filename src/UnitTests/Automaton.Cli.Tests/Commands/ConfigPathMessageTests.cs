using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

/// <summary>Every "saved to" message names the file that was written, also when AUTOMATON_CONFIG_DIR points elsewhere (#689).</summary>
[TestFixture]
public class ConfigPathMessageTests
{
    private string configDirectory = null!;
    private string? previous;
    private ConfigService config = null!;
    private const string Project = "/work/project";

    [SetUp]
    public void PointTheCliElsewhere()
    {
        previous = Environment.GetEnvironmentVariable("AUTOMATON_CONFIG_DIR");
        configDirectory = Path.Combine(Path.GetTempPath(), "automaton-689-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_CONFIG_DIR", configDirectory);
        config = new ConfigService();
    }

    [TearDown]
    public void Restore() => Environment.SetEnvironmentVariable("AUTOMATON_CONFIG_DIR", previous);

    private string GlobalFile => Path.Combine(configDirectory, "config.json");
    private static string LocalFile => Path.Combine(Project, ".automaton", "config.json");

    [Test]
    public void TheGlobalConfigPath_FollowsAutomatonConfigDir() =>
        Assert.That(config.GlobalConfigPath, Is.EqualTo(GlobalFile));

    [Test]
    public void KeyLogin_Global_NamesTheFileInConfigDir()
    {
        var message = LoginCommand.KeySavedMessage(LoginCommand.StoragePath(config, Project, global: true));
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain(GlobalFile));
            Assert.That(message, Does.Not.Contain("~/.automaton"));
        });
    }

    [Test]
    public void KeyLogin_Local_NamesTheProjectFile()
    {
        var message = LoginCommand.KeySavedMessage(LoginCommand.StoragePath(config, Project, global: false));
        Assert.That(message, Does.Contain(LocalFile));
    }

    [Test]
    public void DeviceLogin_Global_NamesTheFileInConfigDir()
    {
        var message = LoginCommand.DeviceSavedMessage(LoginCommand.StoragePath(config, Project, global: true));
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain(GlobalFile));
            Assert.That(message, Does.Not.Contain("~/.automaton"));
        });
    }

    [Test]
    public void DeviceLogin_Local_NamesTheProjectFile()
    {
        var message = LoginCommand.DeviceSavedMessage(LoginCommand.StoragePath(config, Project, global: false));
        Assert.That(message, Does.Contain(LocalFile));
    }

    [Test]
    public void ConfigSaved_Global_NamesTheFileInConfigDir()
    {
        var message = ConfigCommand.SavedMessage(LoginCommand.StoragePath(config, Project, global: true));
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain(GlobalFile));
            Assert.That(message, Does.Not.Contain("~/.automaton"));
        });
    }

    [Test]
    public void ConfigSaved_Local_NamesTheProjectFile() =>
        Assert.That(ConfigCommand.SavedMessage(LoginCommand.StoragePath(config, Project, global: false)), Does.Contain(LocalFile));

    [Test]
    public void ProfileSaved_NamesTheFileInConfigDir()
    {
        var message = ConfigCommand.ProfileSavedMessage("local", config.GlobalConfigPath);
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("Profile local saved to"));
            Assert.That(message, Does.Contain(GlobalFile));
            Assert.That(message, Does.Not.Contain("~/.automaton"));
        });
    }

    [Test]
    public void Logout_Cleared_NamesTheFileInConfigDir()
    {
        var message = LogoutCommandHandler.CredentialsMessage(true, "global", config.GlobalConfigPath);
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain($"Cleared global credentials from {GlobalFile}"));
            Assert.That(message, Does.Not.Contain("~/.automaton"));
        });
    }

    [Test]
    public void Logout_NothingToClear_NamesTheProjectFile() =>
        Assert.That(LogoutCommandHandler.CredentialsMessage(false, "project-local", config.GetProjectLocalConfigPath(Project)),
            Does.Contain($"No project-local credentials found in {LocalFile}"));

    [Test]
    public void AnExplicitConfigFile_IsWhatTheMessagesName()
    {
        var explicitFile = Path.Combine(configDirectory, "other", "shared.json");
        var message = LoginCommand.KeySavedMessage(LoginCommand.StoragePath(new ConfigService(explicitFile), Project, global: true));
        Assert.That(message, Does.Contain(explicitFile));
    }
}
