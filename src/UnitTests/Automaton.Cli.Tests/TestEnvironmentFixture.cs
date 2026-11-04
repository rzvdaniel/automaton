using System.Security.Cryptography;
using NUnit.Framework;

/// <summary>
/// Keeps the whole run away from the developer's real CLI config and credentials.
/// AUTOMATON_CONFIG_DIR points the default ConfigService at a temp directory, and the run fails
/// if the real ~/.automaton/config.json changed.
/// </summary>
[SetUpFixture]
public sealed class TestEnvironmentFixture
{
    private static readonly string[] ClearedVariables =
        ["AUTOMATON_SERVER", "AUTOMATON_API_KEY", "AUTOMATON_PROFILE"];

    private string tempDirectory = null!;
    private string realConfigPath = null!;
    private string? realConfigSnapshot;

    [OneTimeSetUp]
    public void IsolateConfig()
    {
        realConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".automaton", "config.json");
        realConfigSnapshot = Snapshot(realConfigPath);
        tempDirectory = Path.Combine(Path.GetTempPath(), "automaton-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        Environment.SetEnvironmentVariable("AUTOMATON_CONFIG_DIR", tempDirectory);
        foreach (var name in ClearedVariables) Environment.SetEnvironmentVariable(name, null);
    }

    [OneTimeTearDown]
    public void VerifyRealConfigUntouched()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_CONFIG_DIR", null);
        try { Directory.Delete(tempDirectory, recursive: true); } catch (IOException) { }
        Assert.That(Snapshot(realConfigPath), Is.EqualTo(realConfigSnapshot),
            "The test run modified the real ~/.automaton/config.json; a test is not using an isolated ConfigService.");
    }

    private static string? Snapshot(string path)
        => File.Exists(path)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) + "|" + File.GetLastWriteTimeUtc(path).Ticks
            : null;
}
