using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Tests;

public sealed class RunnerHostImageStoreTests
{
    [Test]
    public void ValidateSupportedDockerDriver_RequiresOverlay2()
    {
        Assert.DoesNotThrow(() => RunnerHostImageStore.ValidateSupportedDockerDriver("overlay2"));
        Assert.Throws<InvalidOperationException>(() => RunnerHostImageStore.ValidateSupportedDockerDriver("overlayfs"));
        Assert.Throws<InvalidOperationException>(() => RunnerHostImageStore.ValidateSupportedDockerDriver("btrfs"));
    }

    [Test]
    public async Task VerifyBoundedLayout_ChecksDockerDataRootAndOperatingSystemFilesystem()
    {
        var commands = new List<string>();
        await RunnerHostImageStore.VerifyBoundedLayoutAsync("/mnt/automaton-docker", (arguments, _) =>
        {
            var command = string.Join(' ', arguments);
            commands.Add(command);
            return Task.FromResult(command switch
            {
                "docker info --format {{.Driver}}" => "overlay2",
                "findmnt --json --target / --output TARGET,SOURCE,FSTYPE,UUID,OPTIONS" => MountJson("/", "root-uuid"),
                _ => MountJson("/mnt/automaton-docker", "docker-uuid")
            });
        }, CancellationToken.None);

        Assert.That(commands.Count(command => command.StartsWith("findmnt", StringComparison.Ordinal)), Is.EqualTo(2));
    }

    [Test]
    public void ValidateBoundedLayout_RejectsBindMountBackedByOperatingSystemFilesystem()
    {
        var root = new RunnerHostImageStore.FilesystemMount("/", "/dev/root", "xfs", "same-uuid", "rw,prjquota");
        var dockerBindMount = new RunnerHostImageStore.FilesystemMount("/mnt/docker", "/dev/root", "xfs", "same-uuid", "rw,prjquota");

        Assert.Throws<InvalidOperationException>(() => RunnerHostImageStore.ValidateBoundedLayout(root, dockerBindMount));
    }

    [Test]
    public void ValidateBoundedLayout_RequiresSeparateXfsProjectFilesystem()
    {
        var root = new RunnerHostImageStore.FilesystemMount("/", "/dev/root", "ext4", "root-uuid", "rw");
        var docker = new RunnerHostImageStore.FilesystemMount("/mnt/automaton-docker", "/dev/vdb1", "xfs", "docker-uuid", "rw,relatime,prjquota");

        Assert.DoesNotThrow(() => RunnerHostImageStore.ValidateBoundedLayout(root, docker));
        Assert.Throws<InvalidOperationException>(() => RunnerHostImageStore.ValidateBoundedLayout(
            root, docker with { FilesystemType = "ext4" }));
        Assert.Throws<InvalidOperationException>(() => RunnerHostImageStore.ValidateBoundedLayout(
            root, docker with { Options = "rw,relatime" }));
    }

    [Test]
    public void ParseMount_ReadsFilesystemIdentityAndQuotaOptions()
    {
        var output = MountJson("/mnt/automaton-docker", "uuid-1");

        var mount = RunnerHostImageStore.ParseMount(output);

        Assert.Multiple(() =>
        {
            Assert.That(mount.Target, Is.EqualTo("/mnt/automaton-docker"));
            Assert.That(mount.FilesystemType, Is.EqualTo("xfs"));
            Assert.That(mount.Uuid, Is.EqualTo("uuid-1"));
        });
    }

    private static string MountJson(string target, string uuid)
        => $"{{\"filesystems\":[{{\"target\":\"{target}\",\"source\":\"/dev/vdb1\",\"fstype\":\"xfs\",\"uuid\":\"{uuid}\",\"options\":\"rw,relatime,prjquota\"}}]}}";
}
