using System.Text.Json;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class MachineStaticServingContainerTests
{
    private readonly Dictionary<string, string?> previousEnvironment = new(StringComparer.Ordinal);

    [SetUp]
    public void SetUp()
    {
        Set("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Managed);
        Set("AUTOMATON_RUNNER_APPLICATION_PROFILE", RunnerApplicationProfiles.MachineManagement);
        Set("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.ManagedContainerExecution);
        Set("AUTOMATON_RUNTIME_HOST_ADDRESS", "172.22.0.1");
        Set("AUTOMATON_RUNNER_ID", Guid.NewGuid().ToString("D"));
        Set("AUTOMATON_RUNNER_INSTALLATION_ID", $"sh_{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var (name, value) in previousEnvironment)
            Environment.SetEnvironmentVariable(name, value);
    }

    [Test]
    public async Task EnsureAsync_BindsToConfiguredPrivateAddressAndAllowsImageMetadataLabels()
    {
        var runnerId = Guid.Parse(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_ID")!);
        var installationId = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_INSTALLATION_ID")!;
        var calls = new List<IReadOnlyList<string>>();
        var containerInspections = 0;
        var labels = new Dictionary<string, string>
        {
            ["automaton.managed"] = "true",
            ["automaton.machine-runner-id"] = runnerId.ToString("D"),
            ["automaton.installation-id"] = installationId,
            ["automaton.resource"] = "machine-static-server",
            ["maintainer"] = "NGINX Docker Maintainers <docker-maint@nginx.com>"
        };
        var execute = (IReadOnlyList<string> args, TimeSpan _, CancellationToken _) =>
        {
            calls.Add(args.ToArray());
            if (args[1] == "volume")
                return Task.FromResult(new InfrastructureCommandResponse { Success = true, Output = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["automaton.managed"] = "true",
                    ["automaton.storage-scope"] = "workspace",
                    ["automaton.resource"] = "machine-workspace-volume",
                    ["automaton.installation-id"] = installationId
                }) });
            if (args[1] == "container")
            {
                containerInspections++;
                return containerInspections == 1
                    ? Task.FromResult(new InfrastructureCommandResponse { Error = "No such container" })
                    : Task.FromResult(new InfrastructureCommandResponse
                    {
                        Success = true,
                        Output = JsonSerializer.Serialize(new
                        {
                            Config = new { Labels = labels },
                            State = new { Running = true },
                            NetworkSettings = new
                            {
                                Ports = new Dictionary<string, object[]>
                                {
                                    ["8080/tcp"] = [new { HostIp = "172.22.0.1", HostPort = "43210" }]
                                }
                            }
                        })
                    });
            }
            return Task.FromResult(new InfrastructureCommandResponse { Success = true });
        };
        var container = new MachineStaticServingContainer(execute, new MachineStaticServingOptions());
        var scope = new HostContainerScope(Guid.Empty, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), MachineRunnerId: runnerId);

        var endpoint = await container.EnsureAsync(scope, CancellationToken.None);
        var run = calls.Single(arguments => arguments.Count > 1 && arguments[1] == "run");

        Assert.Multiple(() =>
        {
            Assert.That(endpoint?.Address, Is.EqualTo("172.22.0.1"));
            Assert.That(endpoint?.Port, Is.EqualTo(43210));
            Assert.That(run, Does.Contain("172.22.0.1::8080/tcp"));
            Assert.That(labels.ContainsKey("maintainer"), Is.True);
        });
    }

    private void Set(string name, string value)
    {
        previousEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }
}
