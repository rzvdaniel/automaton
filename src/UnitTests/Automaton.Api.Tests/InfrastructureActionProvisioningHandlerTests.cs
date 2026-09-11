using Automaton.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class InfrastructureActionProvisioningHandlerTests
{
    [Test]
    public void MissingConnectionFailureIsClassifiedAsRetryablePreDispatchLoss()
    {
        var exception = new HubException("Connection 'stale-connection' does not exist.");

        Assert.That(InfrastructureActionProvisioningHandler.IsMissingSignalRConnection(exception), Is.True);
        Assert.That(
            new InfrastructureActionConnectionUnavailableException(Guid.NewGuid(), "stale-connection", exception),
            Has.Message.Contains("retry after the Runner reconnects"));
    }

    [TestCase("The Infrastructure Runner is not online.")]
    [TestCase("Connection failed while invoking the Action.")]
    public void UnrelatedFailuresAreNotClassifiedAsStaleConnection(string message)
    {
        Assert.That(
            InfrastructureActionProvisioningHandler.IsMissingSignalRConnection(new InvalidOperationException(message)),
            Is.False);
    }
}
