using System.Net;
using System.Net.Sockets;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using static Automaton.Runner.Console.Tests.RunnerRuntimeProcessManagerTestSupport;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerRuntimeProcessManagerPortTests : RunnerRuntimeProcessManagerTestBase
{    [Test]
    public async Task RequestedDefaultGatewayPort_IsNotAllocatedWhenOtherwiseAvailable()
    {
        Assert.That(CanBind(8080), Is.True, "This regression requires the default gateway port to be free.");
        var fallbackPort = GetFreePort();
        ConfigureRuntimePorts(null, fallbackPort, fallbackPort);
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contract = CreateContract(["--tcp"]);
        contract.Runtime.Port = 8080;

        await manager.ApplyAsync(appId, targetId, contract);

        Assert.That(registry.TryGet(appId, targetId, out var runtime), Is.True);
        Assert.That(runtime.Port, Is.EqualTo(fallbackPort));
    }

    [Test]
    public async Task Recovery_DoesNotReclaimGatewayPortBeforeGatewayStarts()
    {
        Assert.That(CanBind(8080), Is.True, "This regression requires the default gateway port to be free.");
        var bootstrapGatewayPort = GetFreePort();
        var fallbackPort = GetFreePort(excluding: bootstrapGatewayPort);
        ConfigureRuntimePorts(bootstrapGatewayPort, fallbackPort, fallbackPort);
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contract = CreateContract(["--tcp"]);
        contract.Runtime.Port = 8080;

        using (var initialManager = CreateManager(workspace, registry))
        {
            await initialManager.ApplyAsync(appId, targetId, contract);
            Assert.That(registry.TryGet(appId, targetId, out var initialRuntime), Is.True);
            Assert.That(initialRuntime.Port, Is.EqualTo(8080));
        }

        ConfigureRuntimePorts(null, fallbackPort, fallbackPort);
        using var recoveryManager = CreateManager(workspace, registry);
        await recoveryManager.StartAsync(CancellationToken.None);

        Assert.That(registry.TryGet(appId, targetId, out var recoveredRuntime), Is.True);
        Assert.That(recoveredRuntime.Port, Is.EqualTo(fallbackPort));
        Assert.That(recoveredRuntime.Port, Is.Not.EqualTo(8080));
    }

    [Test]
    public async Task RequestedNonReservedPort_IsHonoredWhenAvailable()
    {
        var requestedPort = GetFreePort();
        var fallbackPort = GetFreePort(excluding: requestedPort);
        ConfigureRuntimePorts(null, fallbackPort, fallbackPort);
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contract = CreateContract(["--tcp"]);
        contract.Runtime.Port = requestedPort;

        await manager.ApplyAsync(appId, targetId, contract);

        Assert.That(registry.TryGet(appId, targetId, out var runtime), Is.True);
        Assert.That(runtime.Port, Is.EqualTo(requestedPort));
    }

    [Test]
    public async Task ConfiguredGatewayPort_IsNotAllocatedToApplicationRuntime()
    {
        var gatewayPort = GetFreePort();
        var fallbackPort = GetFreePort(excluding: gatewayPort);
        ConfigureRuntimePorts(gatewayPort, fallbackPort, fallbackPort);
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contract = CreateContract(["--tcp"]);
        contract.Runtime.Port = gatewayPort;

        await manager.ApplyAsync(appId, targetId, contract);

        Assert.That(registry.TryGet(appId, targetId, out var runtime), Is.True);
        Assert.That(runtime.Port, Is.EqualTo(fallbackPort));
    }

    [Test]
    public async Task FallbackRange_SkipsConfiguredGatewayPort()
    {
        var (gatewayPort, fallbackPort) = GetAdjacentFreePorts();
        var requestedPort = GetFreePort(gatewayPort, fallbackPort);
        using var reservedRequestedPort = new TcpListener(IPAddress.Loopback, requestedPort);
        reservedRequestedPort.Start();
        ConfigureRuntimePorts(gatewayPort, gatewayPort, fallbackPort);
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contract = CreateContract(["--tcp"]);
        contract.Runtime.Port = requestedPort;

        await manager.ApplyAsync(appId, targetId, contract);

        Assert.That(registry.TryGet(appId, targetId, out var runtime), Is.True);
        Assert.That(runtime.Port, Is.EqualTo(fallbackPort));
    }

}
