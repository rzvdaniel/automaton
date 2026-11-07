using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class HostHealthParsersTests
{
    [Test]
    public void ProcFiles_ParseToHostFigures()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HostHealthParsers.ParseUptimeSeconds("12345.67 9999.1\n"), Is.EqualTo(12345));
            Assert.That(HostHealthParsers.ParseUptimeSeconds("garbage"), Is.Null);
            Assert.That(HostHealthParsers.ParseLoad("0.52 0.40 0.30 1/200 999\n"), Is.EqualTo((0.52, 0.40, 0.30)));
            Assert.That(HostHealthParsers.ParseLoad(""), Is.Null);
            Assert.That(HostHealthParsers.ParseMemory("MemTotal:  2000 kB\nMemFree: 1 kB\nMemAvailable:  500 kB\n"),
                Is.EqualTo((2000L * 1024, 500L * 1024)));
            Assert.That(HostHealthParsers.ParseMemory("MemTotal: 10 kB\n"), Is.Null);
        });
    }

    [Test]
    public void CpuPercent_IsBusyShareBetweenTwoSamples()
    {
        var first = HostHealthParsers.ParseCpuStat("cpu  100 0 100 700 100 0 0 0 0 0\ncpu0 1 0 1 1 1 0 0 0 0 0\ncpu1 1 0 1 1 1 0 0 0 0 0\n");
        var second = HostHealthParsers.ParseCpuStat("cpu  200 0 200 800 200 0 0 0 0 0\ncpu0 1 0 1 1 1 0 0 0 0 0\ncpu1 1 0 1 1 1 0 0 0 0 0\n");

        Assert.Multiple(() =>
        {
            Assert.That(first!.Value.Cpus, Is.EqualTo(2));
            Assert.That(HostHealthParsers.CpuPercent(first.Value, second!.Value), Is.EqualTo(50.0));
            Assert.That(HostHealthParsers.CpuPercent(first.Value, first.Value), Is.Null);
            Assert.That(HostHealthParsers.ParseCpuStat("nothing"), Is.Null);
        });
    }

    [Test]
    public void DockerDiskUsage_SumsLayersContainersVolumesAndUnsharedBuildCache()
    {
        var usage = HostHealthParsers.ParseDockerDiskUsage("""
            {"LayersSize":1000,"Containers":[{"SizeRw":10},{"SizeRw":null}],
             "Volumes":[{"UsageData":{"Size":300}},{"UsageData":{"Size":-1}}],
             "BuildCache":[{"Size":50,"Shared":false},{"Size":70,"Shared":true}]}
            """);

        Assert.That(usage, Is.EqualTo((1000L, 10L, 300L, 50L)));
        Assert.That(HostHealthParsers.ParseDockerDiskUsage("not json"), Is.Null);
    }

    [Test]
    public void HelperOutput_ParsesOsUpdatesAndRebootSections()
    {
        var sections = HostHealthParsers.SplitSections("[os]\nPRETTY_NAME=\"Ubuntu 24.04.1 LTS\"\nNAME=\"Ubuntu\"\nVERSION_ID=\"24.04\"\n[updates]\n12 updates can be applied immediately.\n5 of these updates are standard security updates.\n[reboot]\n1700000000\n");

        Assert.Multiple(() =>
        {
            Assert.That(HostHealthParsers.ParseOsRelease(sections["os"]), Is.EqualTo(("Ubuntu 24.04.1 LTS", "24.04")));
            Assert.That(HostHealthParsers.ParseUpdatesAvailable(sections["updates"]), Is.EqualTo(((int?)12, (int?)5)));
            Assert.That(sections["reboot"].Trim(), Is.EqualTo("1700000000"));
        });
    }

    [TestCase("1 update can be applied immediately.\n1 of these updates is a standard security update.\n", 1, 1)]
    [TestCase("3 updates can be applied immediately.\n", 3, 0)]
    [TestCase("0 updates can be applied immediately.\n", 0, 0)]
    public void UpdateNotifierText_ReadsCounts(string text, int pending, int security)
        => Assert.That(HostHealthParsers.ParseUpdatesAvailable(text), Is.EqualTo(((int?)pending, (int?)security)));

    [Test]
    public void MissingUpdateNotifierFile_MeansUnknownNotZero()
        => Assert.That(HostHealthParsers.ParseUpdatesAvailable(""), Is.EqualTo(((int?)null, (int?)null)));
}
