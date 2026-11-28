using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

public sealed class HostContainerComposeDocumentBuilderTests
{
    [Test]
    public void ReadComposeEndpoint_AcceptsInternalServiceWithoutPublishedPorts()
    {
        using var json = JsonDocument.Parse("""{"HostConfig":{"PortBindings":{"8080/tcp":[{"HostIp":"10.20.30.40","HostPort":"0"}]}},"NetworkSettings":{"Ports":null}}""");
        var result = HostContainerComposeProjectExecutor.ReadComposeEndpoint(
            json.RootElement, new string('a', 64), true, new Dictionary<string, string>(), "10.20.30.40");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Running, Is.True);
            Assert.That(result.HostPort, Is.Null);
        });
    }

    [Test]
    public void ReadComposeEndpoint_IgnoresUnpublishedImagePortsForInternalService()
    {
        using var json = JsonDocument.Parse("""{"NetworkSettings":{"Ports":{"6379/tcp":null}}}""");

        var result = HostContainerComposeProjectExecutor.ReadComposeEndpoint(
            json.RootElement, new string('a', 64), true, new Dictionary<string, string>(), "10.20.30.40");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Running, Is.True);
            Assert.That(result.HostPort, Is.Null);
        });
    }

    [Test]
    public void ReadComposeEndpoint_UsesAllocatedRuntimeBindingAndValidatesPrivateAddress()
    {
        using var json = JsonDocument.Parse("""
            {
              "HostConfig":{"PortBindings":{"8080/tcp":[{"HostIp":"10.20.30.40","HostPort":"0"}]}},
              "NetworkSettings":{"Ports":{
                "8080/tcp":[{"HostIp":"10.20.30.40","HostPort":"32768"}],
                "6379/tcp":null
              }}
            }
            """);

        var result = HostContainerComposeProjectExecutor.ReadComposeEndpoint(
            json.RootElement, new string('a', 64), true, new Dictionary<string, string>(), "10.20.30.40");
        var publicBinding = HostContainerComposeProjectExecutor.ReadComposeEndpoint(
            json.RootElement, new string('a', 64), true, new Dictionary<string, string>(), "10.20.30.41");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.HostPort, Is.EqualTo(32768));
            Assert.That(result.ContainerPort, Is.EqualTo(8080));
            Assert.That(publicBinding.Success, Is.False);
        });
    }

    [Test]
    public void Build_AddsPrivateRouteAndConfiguredLimitsToEveryImageService()
    {
        var compose = new ApplicationComposeSpec
        {
            PublicService = "web",
            Services =
            [
                new ApplicationComposeService
                {
                    Name = "web", Image = "nginx@sha256:abc", Port = 8080,
                    Mounts = [new ApplicationComposeVolumeMount { Name = "uploads", Target = "/srv/uploads" }]
                },
                new ApplicationComposeService { Name = "worker", Image = "worker@sha256:def" }
            ],
            Volumes = ["uploads"]
        };
        var scope = new HostContainerScope(Guid.Empty, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), MachineRunnerId: Guid.NewGuid());
        var candidateId = Guid.NewGuid();
        var limits = new HostContainerResourceLimits(1.25m, 768, 96);
        var document = HostContainerComposeDocumentBuilder.Build(
            compose, new Dictionary<string, string> { ["web"] = "nginx@sha256:abc", ["worker"] = "worker@sha256:def" },
            scope, candidateId, "10.20.30.40", limits, 64, 16, 3);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(document));
        var services = json.RootElement.GetProperty("services");
        foreach (var serviceName in new[] { "web", "worker" })
        {
            var service = services.GetProperty(serviceName);
            Assert.That(service.GetProperty("pids_limit").GetInt32(), Is.EqualTo(96));
            Assert.That(service.GetProperty("cpus").GetDecimal(), Is.EqualTo(1.25m));
            Assert.That(service.GetProperty("mem_limit").GetString(), Is.EqualTo("768M"));
        }

        var ports = services.GetProperty("web").GetProperty("ports");
        Assert.That(ports[0].GetProperty("host_ip").GetString(), Is.EqualTo("10.20.30.40"));
        Assert.That(services.GetProperty("worker").TryGetProperty("ports", out _), Is.False);
        var volumes = json.RootElement.GetProperty("volumes");
        Assert.That(volumes.GetProperty("uploads").GetProperty("name").GetString(),
            Is.EqualTo(HostContainerComposeDocumentBuilder.VolumeName(scope, "uploads")));
        Assert.That(services.GetProperty("web").GetProperty("volumes")[0].GetString(), Is.EqualTo("uploads:/srv/uploads"));
        foreach (var serviceName in new[] { "web", "worker" })
        {
            var labels = services.GetProperty(serviceName).GetProperty("labels");
            Assert.That(labels.TryGetProperty("com.docker.compose.service", out _), Is.False);
            Assert.That(labels.TryGetProperty("com.automaton.managed", out _), Is.True);
        }
    }

    [Test]
    public void Build_MountsGrantedVolumesOnlyIntoThePublicService_ReadOnlyByDefault()
    {
        var compose = new ApplicationComposeSpec
        {
            PublicService = "web",
            Services = [new() { Name = "web", Image = "x/y:1", Port = 8080 }, new() { Name = "db", Image = "x/db:1" }],
            Volumes = []
        };
        var scope = new HostContainerScope(Guid.Empty, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), MachineRunnerId: Guid.NewGuid());
        var grant = new HostContainerGrantedMount(Guid.NewGuid(), Guid.NewGuid(), "content", "/granted/ghost-abc123/content", true);
        var images = new Dictionary<string, string> { ["web"] = "x/y@sha256:1", ["db"] = "x/db@sha256:2" };

        var document = HostContainerComposeDocumentBuilder.Build(compose, images, scope, Guid.NewGuid(), "10.0.0.1",
            new HostContainerResourceLimits(1, 256, 128), 64, 10, 3, [grant]);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(document));
        var services = json.RootElement.GetProperty("services");

        Assert.Multiple(() =>
        {
            var web = services.GetProperty("web").GetProperty("volumes")[0].GetString()!;
            Assert.That(web, Does.EndWith(":/granted/ghost-abc123/content:ro"));
            Assert.That(services.GetProperty("db").TryGetProperty("volumes", out _), Is.False);
            var key = web[..web.IndexOf(':')];
            Assert.That(json.RootElement.GetProperty("volumes").GetProperty(key).GetProperty("name").GetString(),
                Is.EqualTo(HostContainerComposeDocumentBuilder.GrantedVolumeName(grant)));
        });
    }

    [Test]
    public void Build_RefusesAnOwnVolumeNamedLikeAGrantedOne()
    {
        var grant = new HostContainerGrantedMount(Guid.NewGuid(), Guid.NewGuid(), "content", "/granted/ghost-abc123/content", true);
        var compose = new ApplicationComposeSpec
        {
            PublicService = "web",
            Services = [new() { Name = "web", Image = "x/y:1", Port = 8080 }],
            Volumes = ["granted_" + grant.SourceAppId.ToString("N")[..12] + "_content"]
        };
        var scope = new HostContainerScope(Guid.Empty, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), MachineRunnerId: Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => HostContainerComposeDocumentBuilder.Build(compose,
            new Dictionary<string, string> { ["web"] = "x/y@sha256:1" }, scope, Guid.NewGuid(), "10.0.0.1",
            new HostContainerResourceLimits(1, 256, 128), 64, 10, 3, [grant]));
    }
}
