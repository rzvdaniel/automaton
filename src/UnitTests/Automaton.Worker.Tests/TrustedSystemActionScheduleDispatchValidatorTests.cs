using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Models;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

[TestFixture]
public sealed class TrustedSystemActionScheduleDispatchValidatorTests
{
    [Test]
    public async Task ValidWorkerAction_ReturnsCurrentManifest()
    {
        var manifest = Manifest();
        var result = await Validator(manifest).ValidateAsync(Schedule(), CancellationToken.None);
        Assert.That(result, Is.SameAs(manifest));
    }

    [Test]
    public void ActionRemoved_IsRejected()
        => Assert.That(
            () => Validator(null).ValidateAsync(Schedule(), CancellationToken.None),
            Throws.TypeOf<KeyNotFoundException>());

    [Test]
    public void SchedulableRemoved_IsRejected()
    {
        var manifest = Manifest();
        manifest.Spec.Schedulable = false;
        Assert.That(() => Validator(manifest).ValidateAsync(Schedule(), CancellationToken.None), Throws.InvalidOperationException);
    }

    [Test]
    public void WorkerScopeRemoved_IsRejected()
    {
        var manifest = Manifest();
        manifest.Spec.AllowedExecutionScopes = [ActionExecutionScopes.AppRunner];
        Assert.That(() => Validator(manifest).ValidateAsync(Schedule(), CancellationToken.None), Throws.InvalidOperationException);
    }

    [Test]
    public void ChangedInputSchema_IsRejected()
    {
        var manifest = Manifest();
        manifest.Spec.Input = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["required"] = new[] { "requiredValue" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["requiredValue"] = new Dictionary<string, object?> { ["type"] = "string" }
            }
        };
        Assert.That(() => Validator(manifest).ValidateAsync(Schedule(), CancellationToken.None), Throws.InvalidOperationException);
    }

    [TestCase(ActionScheduleOwnerTypes.User)]
    [TestCase(ActionScheduleOwnerTypes.System)]
    public void CorruptedOwnershipOrApplication_IsRejected(string ownerType)
    {
        var schedule = Schedule();
        schedule.OwnerType = ownerType;
        if (ownerType == ActionScheduleOwnerTypes.System)
            schedule.ApplicationId = Guid.NewGuid();
        Assert.That(() => Validator(Manifest()).ValidateAsync(schedule, CancellationToken.None), Throws.InvalidOperationException);
    }

    private static TrustedSystemActionScheduleDispatchValidator Validator(AppActionManifest? manifest)
        => new(new StubSource(manifest));

    private static ActionSchedule Schedule() => new()
    {
        OwnerType = ActionScheduleOwnerTypes.System,
        ApplicationId = InfrastructureAppTrust.PlatformAppId,
        ActionKey = "worker-action",
        ExecutionScope = ActionExecutionScopes.Worker,
        InputJson = "{}"
    };

    private static AppActionManifest Manifest() => new()
    {
        Metadata = new AppActionMetadata { Key = "worker-action" },
        Spec = new AppActionSpec
        {
            Schedulable = true,
            AllowedExecutionScopes = [ActionExecutionScopes.Worker],
            Input = new Dictionary<string, object?> { ["type"] = "object" }
        }
    };

    private sealed class StubSource(AppActionManifest? manifest) : ITrustedSystemActionManifestSource
    {
        public AppActionManifest? Find(string actionKey) => manifest;
    }
}
