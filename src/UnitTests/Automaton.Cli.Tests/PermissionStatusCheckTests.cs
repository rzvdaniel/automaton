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
}
