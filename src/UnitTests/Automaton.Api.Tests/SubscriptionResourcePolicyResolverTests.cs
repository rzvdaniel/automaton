using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class SubscriptionResourcePolicyResolverTests
{
    [Test]
    public async Task ResolveAsync_UsesTheSpecificationSelectedByTheEffectiveSubscription()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var selected = CreateSpecification("selected", new HostedRunnerResources
        {
            CpuLimit = 0.75m,
            MemoryLimitMb = 1024,
            DiskLimitMb = 3072,
            PidsLimit = 192,
            MaximumApps = 7
        });
        var otherActiveSpecification = CreateSpecification("other", new HostedRunnerResources
        {
            CpuLimit = 2m,
            MemoryLimitMb = 4096,
            DiskLimitMb = 8192,
            PidsLimit = 512,
            MaximumApps = 30
        });
        var plan = CreatePlan(selected.Id);
        db.HostedRunnerSpecifications.AddRange(selected, otherActiveSpecification);
        db.SubscriptionPlans.Add(plan);
        db.Users.Add(CreateUser(userId, plan.Id));
        await db.SaveChangesAsync();

        var resolver = CreateResolver(db);
        var resolution = await resolver.ResolveAsync(userId, null, CancellationToken.None);

        Assert.That(resolution.Succeeded, Is.True);
        Assert.That(resolution.Specification!.Id, Is.EqualTo(selected.Id));
        Assert.That(resolution.Policy, Is.EqualTo(new SubscriptionResourcePolicy(
            0.75m, 1024, 3072, 192, 1, 7)));
    }

    [Test]
    public async Task ResolveAsync_FailsWhenPlanOrCapabilityDoesNotProvideEligibleSpecification()
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var noCapabilityPlan = CreatePlan(null, enabled: false);
        var noSpecificationPlan = CreatePlan(null);
        var missingSpecificationPlan = CreatePlan(Guid.NewGuid());
        var inactive = CreateSpecification("inactive");
        inactive.IsActive = false;
        var inactivePlan = CreatePlan(inactive.Id);
        db.SubscriptionPlans.AddRange(noCapabilityPlan, noSpecificationPlan, missingSpecificationPlan, inactivePlan);
        db.HostedRunnerSpecifications.Add(inactive);
        db.Users.AddRange(
            CreateUser(userId, noCapabilityPlan.Id),
            CreateUser(Guid.NewGuid(), noSpecificationPlan.Id),
            CreateUser(Guid.NewGuid(), missingSpecificationPlan.Id),
            CreateUser(Guid.NewGuid(), inactivePlan.Id));
        await db.SaveChangesAsync();

        var resolver = CreateResolver(db);

        Assert.That((await resolver.ResolveAsync(Guid.NewGuid(), null, CancellationToken.None)).Failure,
            Is.EqualTo(SubscriptionResourcePolicyResolutionFailure.NoEffectivePlan));
        Assert.That((await resolver.ResolveAsync(userId, null, CancellationToken.None)).Failure,
            Is.EqualTo(SubscriptionResourcePolicyResolutionFailure.CapabilityDisabled));
        Assert.That((await resolver.ResolveAsync(
                db.Users.Single(x => x.SubscriptionPlanId == noSpecificationPlan.Id).Id,
                null,
                CancellationToken.None)).Failure,
            Is.EqualTo(SubscriptionResourcePolicyResolutionFailure.SpecificationNotConfigured));
        Assert.That((await resolver.ResolveAsync(
                db.Users.Single(x => x.SubscriptionPlanId == missingSpecificationPlan.Id).Id,
                null,
                CancellationToken.None)).Failure,
            Is.EqualTo(SubscriptionResourcePolicyResolutionFailure.SpecificationNotFound));
        Assert.That((await resolver.ResolveAsync(
                db.Users.Single(x => x.SubscriptionPlanId == inactivePlan.Id).Id,
                null,
                CancellationToken.None)).Failure,
            Is.EqualTo(SubscriptionResourcePolicyResolutionFailure.SpecificationInactive));
    }

    [TestCase(nameof(HostedRunnerResources.PidsLimit), 0)]
    [TestCase(nameof(HostedRunnerResources.MemoryReservationMb), 1024)]
    [TestCase(nameof(HostedRunnerResources.DockerLogMaxFiles), 0)]
    public async Task ResolveAsync_FailsForInvalidPersistedResourceValues(string propertyName, int invalidValue)
    {
        using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var resources = new HostedRunnerResources();
        typeof(HostedRunnerResources).GetProperty(propertyName)!.SetValue(resources, invalidValue);
        var specification = CreateSpecification("invalid", resources);
        var plan = CreatePlan(specification.Id);
        db.HostedRunnerSpecifications.Add(specification);
        db.SubscriptionPlans.Add(plan);
        db.Users.Add(CreateUser(userId, plan.Id));
        await db.SaveChangesAsync();

        var resolution = await CreateResolver(db).ResolveAsync(userId, null, CancellationToken.None);

        Assert.That(resolution.Succeeded, Is.False);
        Assert.That(resolution.Failure,
            Is.EqualTo(SubscriptionResourcePolicyResolutionFailure.InvalidSpecificationResources));
    }

    [Test]
    public void ProvisioningDependsOnPolicyResolverAndNormalProvisionRequestHasNoResourceOverrides()
    {
        var constructor = typeof(HostedRunnerProvisioningService).GetConstructors().Single();
        var parameters = constructor.GetParameters().Select(x => x.ParameterType).ToArray();

        Assert.That(parameters, Does.Contain(typeof(ISubscriptionResourcePolicyResolver)));
        Assert.That(parameters, Does.Not.Contain(typeof(IHostedRunnerSpecificationResolver)));
        Assert.That(typeof(ProvisionHostedRunnerRequest).GetProperties(), Is.Empty);
    }

    private static ISubscriptionResourcePolicyResolver CreateResolver(ApplicationDbContext db)
        => new SubscriptionResourcePolicyResolver(new HostedRunnerSpecificationResolver(db));

    private static ApplicationDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static HostedRunnerSpecification CreateSpecification(string code, HostedRunnerResources? resources = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            Resources = resources ?? new HostedRunnerResources()
        };

    private static SubscriptionPlan CreatePlan(Guid? specificationId, bool enabled = true)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = "Test plan",
            IsActive = true,
            CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities
            {
                HostedRunner = new HostedRunnerCapability
                {
                    Enabled = enabled,
                    SpecificationId = specificationId
                }
            })
        };

    private static ApplicationUser CreateUser(Guid userId, Guid planId)
        => new()
        {
            Id = userId,
            Email = $"{userId:N}@automaton.test",
            UserName = userId.ToString("N"),
            PublicUsername = $"user-{userId.ToString("N")[..12]}",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            SubscriptionPlanId = planId
        };
}
