using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class GenericApplicationDeploymentArchitectureTests
{
    [Test]
    public void DeploymentOperationService_UsesGenericApplicationDeploymentBoundary()
    {
        var constructor = typeof(DeploymentOperationService).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.That(parameterTypes, Does.Contain(typeof(ApplicationDeploymentService)));
    }

    [Test]
    public void GenericApplicationDeploymentService_DoesNotDependOnControllerRuntime()
    {
        var members = typeof(ApplicationDeploymentService)
            .GetMembers()
            .Select(member => member.ToString() ?? string.Empty)
            .ToArray();

        Assert.That(members, Has.None.Contains("AppsControllerExecutionService"));
        Assert.That(members, Has.None.Contains("UploadControllerScript"));
        Assert.That(members, Has.None.Contains("ExecuteAction"));
        Assert.That(members, Has.None.Contains("FastAPI"));
        Assert.That(members, Has.None.Contains("LegacyControllerRuntimeDeployed"));
    }
}
