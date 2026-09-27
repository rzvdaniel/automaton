using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Tests;

public sealed class RunnerHostImageStoreTests
{
    [Test]
    public void Parse_RecognizesClassicDockerStorage()
    {
        var store = RunnerHostImageStore.Parse("overlay2", "[]");

        Assert.That(store.UsesContainerd, Is.False);
    }

    [Test]
    public void Parse_RecognizesContainerdImageStore()
    {
        var store = RunnerHostImageStore.Parse("overlayfs", "[[\"driver-type\",\"io.containerd.snapshotter.v1\"]]");

        Assert.That(store.UsesContainerd, Is.True);
    }

    [Test]
    public async Task VerifyBoundedLayout_ChecksDockerRootForClassicImageStore()
    {
        var commands = new List<string>();
        await RunnerHostImageStore.VerifyBoundedLayoutAsync("/mnt/automaton-docker", (arguments, _) =>
        {
            var command = string.Join(' ', arguments);
            commands.Add(command);
            return Task.FromResult(command switch
            {
                "docker info --format {{.Driver}}" => "overlay2",
                "docker info --format {{json .DriverStatus}}" => "[]",
                _ => MountJson("/mnt/automaton-docker")
            });
        }, CancellationToken.None);

        Assert.That(commands.Count(command => command.StartsWith("findmnt", StringComparison.Ordinal)), Is.EqualTo(1));
    }

    [Test]
    public async Task VerifyBoundedLayout_ChecksSeparateContainerdImageStoreRoot()
    {
        var commands = new List<string>();
        await RunnerHostImageStore.VerifyBoundedLayoutAsync("/mnt/automaton-docker", (arguments, _) =>
        {
            var command = string.Join(' ', arguments);
            commands.Add(command);
            return Task.FromResult(command switch
            {
                "docker info --format {{.Driver}}" => "overlayfs",
                "docker info --format {{json .DriverStatus}}" => "[[\"driver-type\",\"io.containerd.snapshotter.v1\"]]",
                "sh -c ps -C dockerd -o args= 2>/dev/null || true" => "dockerd --containerd=/run/docker/containerd/containerd.sock",
                "sh -c ps -C containerd -o args= 2>/dev/null || true" => "containerd --address /run/docker/containerd/containerd.sock --root /mnt/automaton-docker/containerd",
                _ => MountJson("/mnt/automaton-docker")
            });
        }, CancellationToken.None);

        Assert.That(commands.Count(command => command.StartsWith("findmnt", StringComparison.Ordinal)), Is.EqualTo(2));
    }

    [Test]
    public async Task ResolveContainerdRoot_UsesRootFromContainerdConnectedToDocker()
    {
        IReadOnlyList<string>? capturedArguments = null;
        var root = await RunnerHostImageStore.ResolveContainerdRootAsync(
            "/mnt/automaton-docker",
            "dockerd --containerd=/run/containerd/containerd.sock",
            "/usr/bin/containerd --address /run/containerd/containerd.sock --config /etc/containerd/automaton.toml",
            (arguments, _) =>
            {
                capturedArguments = arguments;
                return Task.FromResult("version = 2\nroot = \"/mnt/automaton-docker/containerd\"\n[grpc]\naddress = \"/run/containerd/containerd.sock\"");
            }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(root, Is.EqualTo("/mnt/automaton-docker/containerd"));
            Assert.That(capturedArguments, Is.EqualTo(new[] { "containerd", "--config", "/etc/containerd/automaton.toml", "config", "dump" }));
        });
    }

    [Test]
    public async Task ResolveContainerdRoot_RejectsUnrelatedContainerdProcess()
    {
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => RunnerHostImageStore.ResolveContainerdRootAsync(
            "/mnt/automaton-docker",
            "dockerd --containerd=/run/docker/containerd/containerd.sock",
            "containerd --address /run/containerd/containerd.sock --root /var/lib/containerd",
            (_, _) => throw new AssertionException("an unrelated containerd process must not be queried"),
            CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("could not be identified unambiguously"));
    }

    [Test]
    public void ValidateBoundedLayout_RequiresOneNonRootXfsProjectFilesystem()
    {
        var mount = new RunnerHostImageStore.FilesystemMount("/mnt/automaton-docker", "/dev/vdb1", "xfs", "uuid-1", "rw,relatime,prjquota");

        Assert.DoesNotThrow(() => RunnerHostImageStore.ValidateBoundedLayout(mount, mount));
        Assert.Throws<InvalidOperationException>(() => RunnerHostImageStore.ValidateBoundedLayout(
            mount, mount with { Target = "/mnt/containerd", Uuid = "uuid-2" }));
        Assert.Throws<InvalidOperationException>(() => RunnerHostImageStore.ValidateBoundedLayout(
            mount with { Target = "/" }, mount with { Target = "/" }));
    }

    [Test]
    public void ParseMount_ReadsFilesystemIdentityAndQuotaOptions()
    {
        var output = "{\"filesystems\":[{\"target\":\"/mnt/automaton-docker\",\"source\":\"/dev/vdb1\",\"fstype\":\"xfs\",\"uuid\":\"uuid-1\",\"options\":\"rw,relatime,prjquota\"}]}";

        var mount = RunnerHostImageStore.ParseMount(output);

        Assert.Multiple(() =>
        {
            Assert.That(mount.Target, Is.EqualTo("/mnt/automaton-docker"));
            Assert.That(mount.FilesystemType, Is.EqualTo("xfs"));
            Assert.That(mount.Uuid, Is.EqualTo("uuid-1"));
        });
    }

    private static string MountJson(string target)
        => $"{{\"filesystems\":[{{\"target\":\"{target}\",\"source\":\"/dev/vdb1\",\"fstype\":\"xfs\",\"uuid\":\"uuid-1\",\"options\":\"rw,relatime,prjquota\"}}]}}";
}
