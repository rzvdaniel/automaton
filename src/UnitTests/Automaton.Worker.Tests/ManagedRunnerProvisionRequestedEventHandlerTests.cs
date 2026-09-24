using NUnit.Framework;

namespace Automaton.Worker.Tests;

public sealed class ManagedRunnerProvisionRequestedEventHandlerTests
{
    [Test]
    public Task PublishesTrustedInfrastructureActionWithoutRunningDocker()
        => ManagedRunnerProvisionScenarios.VerifyTrustedProvisionLifecycleAsync();

    [Test]
    public Task InvalidImageReferenceFailsProvisioningWithoutPublishingInfrastructureAction()
        => ManagedRunnerProvisionScenarios.VerifyInvalidImageFailsClosedAsync();
}
