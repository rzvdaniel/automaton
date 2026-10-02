using Automaton.Api.Entities;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Api.Tests;

internal static class DeploymentDispatchTestData
{
    public static async Task<Guid> AddAsync(ApplicationDeploymentServiceTests.DeploymentFixture fixture)
    {
        var appId = Guid.NewGuid();
        var runner = new Runner { Id = Guid.NewGuid(), Name = "Concurrent Hosted Runner", Purpose = RunnerPurposes.Managed,
            ApiKeyId = Guid.NewGuid(), ApiInstanceId = fixture.ApiIdentity.ApiInstanceId,
            ApiCompatibilityVersion = RunnerApiCompatibilityVersions.Current,
            CapabilitiesJson = RunnerCapabilities.Serialize(RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed), RunnerPurposes.Managed) };
        var target = AppDeploymentTarget.CreateDefault(appId, runner.Id);
        target.ExecutionRunner = runner;
        fixture.Db.WebApps.Add(new WebApp { Id = appId, Name = "Concurrent deployment", Summary = "", Description = "",
            Definition = "{}", ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract()),
            DeploymentTargets = { target } });
        fixture.Db.Runners.Add(runner);
        var packagePath = Path.Combine(fixture.RootPath, $"{appId:N}.zip");
        _ = DeploymentPackageArchiveFactory.Create(appId, packagePath, hasRuntime: false);
        var operation = await DeploymentOperationTestData.CreateAsync(fixture.Db, appId, fixture.OwnerId,
            DeploymentOperationStatuses.Pending, packagePath);
        return operation.Id;
    }
}
