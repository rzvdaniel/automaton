using System.Text.Json;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests;

[TestFixture]
public sealed class PermissionStatusCheckTests
{
    [Test]
    public void NamesWaitingPermissionsWithTheApprovalLink()
    {
        using var status = JsonDocument.Parse("""
            {"permissions":[{"permission":"backups:read","approved":false,"status":"Pending"}],
             "waiting":["backups:read"],
             "approvalUrl":"https://studio.example.com/admin/apps/1?tab=permissions"}
            """);
        var warning = PermissionStatusCheck.Describe(status.RootElement);
        Assert.That(warning, Does.StartWith("permissions waiting for approval: backups:read;"));
        Assert.That(warning, Does.EndWith("https://studio.example.com/admin/apps/1?tab=permissions"));
    }

    [Test]
    public void SaysNothingWhenNothingWaits()
    {
        using var status = JsonDocument.Parse("""{"permissions":[{"permission":"backups:read","approved":true,"status":"Approved"}],"waiting":[],"approvalUrl":null}""");
        Assert.That(PermissionStatusCheck.Describe(status.RootElement), Is.Null);
    }

    [Test]
    public void PointsToStudioWhenTheServerHasNoStudioAddress()
    {
        using var status = JsonDocument.Parse("""{"permissions":[],"waiting":["backups:read"],"approvalUrl":null}""");
        Assert.That(PermissionStatusCheck.Describe(status.RootElement), Does.EndWith("in Studio: Admin → Apps"));
    }

    [Test]
    public async Task AFailedCheckIsNotReportedAsWaiting()
    {
        // Nothing listens on port 1, so the request fails; deploy must not suggest asking for an approval.
        var outcome = await PermissionStatusCheck.FindWaitingAsync("http://127.0.0.1:1", "key", Guid.NewGuid().ToString(),
            ["backups:read"], CancellationToken.None);
        Assert.That(outcome, Is.Not.Null);
        Assert.That(outcome!.Waiting, Is.False);
        Assert.That(outcome.Message, Does.StartWith("could not check requested permissions"));
    }

    [Test]
    public void AnUnexpectedShapeIsReportedByTheCaller()
    {
        // Describe throws for a non-object answer; FindWaitingAsync turns that into "could not check".
        using var status = JsonDocument.Parse("[1,2]");
        Assert.Throws<InvalidOperationException>(() => PermissionStatusCheck.Describe(status.RootElement));
    }
}
