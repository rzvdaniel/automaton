using Automaton.Core.Models;
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
    public void DockerAppUsage_GroupsContainersAndImagesByAppAndSumsUnusedImages()
    {
        var app = Guid.NewGuid();
        var usage = HostHealthParsers.ParseDockerAppUsage($$$"""
            {"Images":[{"Id":"a","Size":500,"Containers":2},{"Id":"b","Size":70,"Containers":0,"SharedSize":20},{"Id":"c","Size":30,"Containers":0,"SharedSize":-1}],
             "Containers":[{"ImageID":"a","SizeRw":10,"Labels":{"com.automaton.app-id":"{{{app}}}"}},
                           {"ImageID":"a","SizeRw":5,"Labels":{"com.automaton.app-id":"{{{app}}}"}},
                           {"ImageID":"a","SizeRw":99,"Labels":{}}]}
            """);

        Assert.Multiple(() =>
        {
            Assert.That(usage!.Value.Apps, Is.EqualTo(new Dictionary<Guid, (long, long)> { [app] = (15, 500) }));
            Assert.That(usage.Value.UnusedImages, Is.EqualTo(80), "unique size only: (70 - 20) + 30");
            Assert.That(HostHealthParsers.ParseDockerAppUsage("nope"), Is.Null);
        });
    }

    [Test]
    public void ContainerStats_GiveCpuPercentOfOneCoreAndMemoryWithoutCache_AndRunningAppContainersAreListed()
    {
        var app = Guid.NewGuid();
        var (cpu, memory) = HostHealthParsers.ParseContainerStats("""
            {"cpu_stats":{"cpu_usage":{"total_usage":300},"system_cpu_usage":2000,"online_cpus":2},
             "precpu_stats":{"cpu_usage":{"total_usage":200},"system_cpu_usage":1000},
             "memory_stats":{"usage":1000,"stats":{"inactive_file":400}}}
            """);
        var containers = HostHealthParsers.ParseRunningAppContainers($$$"""
            {"Containers":[{"Id":"c1","State":"running","Labels":{"com.automaton.app-id":"{{{app}}}"}},
                           {"Id":"c2","State":"exited","Labels":{"com.automaton.app-id":"{{{app}}}"}},
                           {"Id":"c3","State":"running","Labels":{}}]}
            """);
        Assert.Multiple(() =>
        {
            Assert.That(cpu, Is.EqualTo(20.0), "100 / 1000 * 2 cpus * 100");
            Assert.That(memory, Is.EqualTo(600));
            Assert.That(HostHealthParsers.ParseContainerStats("nope"), Is.EqualTo(((double?)null, (long?)null)));
            Assert.That(containers, Is.EqualTo(new[] { (app, "c1") }));
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
    public void Disks_ListMainDiskAndDockerDiskOnlyWhenItIsAnotherDevice()
    {
        var split = "root 10 4096 100 40\ndocker 20 4096 200 150\n";
        var disks = HostHealthParsers.ParseDisks(split, "/mnt/docker-data");
        Assert.That(disks, Is.EqualTo(new[] { new HostDisk("/", 4096 * 100, 4096 * 40), new HostDisk("/mnt/docker-data", 4096 * 200, 4096 * 150) }));
        Assert.That(HostHealthParsers.ParseDisks("root 10 4096 100 40\ndocker 10 4096 100 40\n", "/var/lib/docker"), Has.Count.EqualTo(1));
        Assert.That(HostHealthParsers.ParseDisks("docker 20 4096 200 150\n", "/mnt/x"), Is.Null);
        Assert.That(HostHealthParsers.ParseDisks("garbage", null), Is.Null);
    }

    [Test]
    public void DockerRoot_AcceptsOnlyPlainAbsolutePaths()
    {
        Assert.That(HostHealthParsers.ParseDockerRoot("{\"DockerRootDir\":\"/mnt/docker-data\"}"), Is.EqualTo("/mnt/docker-data"));
        Assert.That(HostHealthParsers.ParseDockerRoot("{\"DockerRootDir\":\"/a,source=/etc\"}"), Is.Null);
        Assert.That(HostHealthParsers.ParseDockerRoot("nope"), Is.Null);
    }

    [Test]
    public void HelperOutput_ParsesOsAndRebootSections()
    {
        var sections = HostHealthParsers.SplitSections("[os]\nPRETTY_NAME=\"Ubuntu 24.04.1 LTS\"\nNAME=\"Ubuntu\"\nVERSION_ID=\"24.04\"\n[reboot]\n1700000000\n");

        Assert.Multiple(() =>
        {
            Assert.That(HostHealthParsers.ParseOsRelease(sections["os"]), Is.EqualTo(("Ubuntu 24.04.1 LTS", "24.04")));
            Assert.That(sections["reboot"].Trim(), Is.EqualTo("1700000000"));
        });
    }

    [TestCase("71;12\n", 71, 12)]
    [TestCase("/usr/lib/update-notifier/apt-check:387: Warning: W:Unable to read x\n  depcache = apt_pkg.DepCache(cache)\n42;0", 42, 0)]
    [TestCase("0;0", 0, 0)]
    [TestCase("Listing... Done\nopenssl/noble-security 3.0.13 amd64 [upgradable from: 3.0.12]\nvim/noble-updates 9.1 amd64 [upgradable from: 9.0]\n", 2, 1)]
    [TestCase("Listing... Done\n", 0, 0)]
    public void UpdateCounts_ReadAptCheckAndTheAptListFallback(string text, int pending, int security)
        => Assert.That(HostHealthParsers.ParseUpdateCounts(text), Is.EqualTo(((int?)pending, (int?)security)));

    [TestCase("")]
    [TestCase("E: Could not open lock file\n")]
    public void UnusableUpdateOutput_MeansUnknownNotZero(string text)
        => Assert.That(HostHealthParsers.ParseUpdateCounts(text), Is.EqualTo(((int?)null, (int?)null)));

    [Test]
    public void DirectorySize_CountsFilesBelowThePath_AndIsZeroForAMissingOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "e2e-disk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(root, "sub", "b.bin"), new byte[23]);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(AppDiskMeasurer.DirectorySize(root), Is.EqualTo(123));
                Assert.That(AppDiskMeasurer.DirectorySize(Path.Combine(root, "missing")), Is.Zero);
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
