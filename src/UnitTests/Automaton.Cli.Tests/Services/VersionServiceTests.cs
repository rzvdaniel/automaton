using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class VersionServiceTests
{
    [Test]
    public void WriteVersion_WhenInstalledPackageVersionExists_PrintsPackageVersionAsPrimaryVersion()
    {
        var writer = new StringWriter();
        var service = new StubVersionService(new CliVersionInfo("1.0.20260507.3", "1.0.0+abcdef", "1.0.20260507.3"));

        service.WriteVersion(writer);

        Assert.That(writer.ToString(), Is.EqualTo(
            "Version: 1.0.20260507.3" + Environment.NewLine +
            "Build version: 1.0.0+abcdef" + Environment.NewLine));
    }

    [Test]
    public void WriteVersion_WhenInstalledPackageVersionDoesNotExist_PrintsBuildVersionOnly()
    {
        var writer = new StringWriter();
        var service = new StubVersionService(new CliVersionInfo("1.0.0+abcdef", "1.0.0+abcdef", null));

        service.WriteVersion(writer);

        Assert.That(writer.ToString(), Is.EqualTo("Version: 1.0.0+abcdef" + Environment.NewLine));
    }

    [Test]
    public void TryGetInstalledPackageVersion_WhenPathIsWindowsToolStorePath_ReturnsVersion()
    {
        var path = @"C:\Users\Razvan\.dotnet\tools\.store\automaton.cli\1.0.20260507.3\automaton.cli\1.0.20260507.3\tools\net9.0\any\automaton.dll";

        var version = VersionService.TryGetInstalledPackageVersion(path);

        Assert.That(version, Is.EqualTo("1.0.20260507.3"));
    }

    [Test]
    public void TryGetInstalledPackageVersion_WhenPathIsLinuxToolStorePath_ReturnsVersion()
    {
        var path = "/home/runner/.dotnet/tools/.store/automaton.cli/1.0.20260507.3/automaton.cli/1.0.20260507.3/tools/net9.0/any/automaton.dll";

        var version = VersionService.TryGetInstalledPackageVersion(path);

        Assert.That(version, Is.EqualTo("1.0.20260507.3"));
    }

    [Test]
    public void TryGetInstalledPackageVersion_WhenPathIsLocalBuildOutput_ReturnsNull()
    {
        var path = @"C:\Work\automaton\src\Automaton.Cli\bin\Debug\net9.0\automaton.dll";

        var version = VersionService.TryGetInstalledPackageVersion(path);

        Assert.That(version, Is.Null);
    }

    private sealed class StubVersionService(CliVersionInfo versionInfo) : VersionService
    {
        public override CliVersionInfo GetVersionInfo() => versionInfo;
    }
}