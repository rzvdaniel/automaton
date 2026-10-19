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
        using var json = JsonDocument.Parse("""{"HostConfig":{"PortBindings":null}}""");
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
    public void Build_AddsPrivateRouteAndConfiguredLimitsToEveryImageService()
    {
        var compose = new ApplicationComposeSpec
        {
            PublicService = "web",
            Services =
            [
                new ApplicationComposeService { Name = "web", Image = "nginx@sha256:abc", Port = 8080 },
                new ApplicationComposeService { Name = "worker", Image = "worker@sha256:def" }
            ]
        };
        var limits = new HostContainerResourceLimits(1.25m, 768, 96);
        var document = HostContainerComposeDocumentBuilder.Build(
            compose, new Dictionary<string, string> { ["web"] = "nginx@sha256:abc", ["worker"] = "worker@sha256:def" },
            new(Guid.Empty, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), MachineRunnerId: Guid.NewGuid()),
            Guid.NewGuid(), "10.20.30.40", limits, 64, 16, 3);

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
    }
}
