using Automaton.Node.Capabilities;
using NUnit.Framework;

namespace Automaton.Node.Tests;

[TestFixture]
public sealed class NodeCapabilityValidatorTests
{
    private static NodeCapabilityReport SupportedReport() => new(
        "linux", "x64", "docker", "28.0", "v2",
        [new(Guid.NewGuid(), Guid.NewGuid().ToString("D"), "xfs", 42)]);

    [Test]
    public void MissingReportIsUnknownAndIneligible()
    {
        var result = NodeCapabilityValidator.Validate(null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Report, Is.Null);
            Assert.That(result.Diagnostics, Does.Contain(NodeCapabilityDiagnostic.ReportMissing));
            Assert.That(result.CanAdmitWorkloads, Is.False);
            Assert.That(result.EligibleWorkloadKinds, Is.Empty);
        });
    }

    [Test]
    public void SupportedSyntheticFactsNeverGrantAdmission()
    {
        var result = NodeCapabilityValidator.Validate(SupportedReport());
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Report, Is.Not.Null);
            Assert.That(result.CanAdmitWorkloads, Is.False);
            Assert.That(result.EligibleWorkloadKinds, Is.Empty);
        });
    }

    [TestCase(null, NodeCapabilityDiagnostic.EngineMissing)]
    [TestCase("", NodeCapabilityDiagnostic.EngineMissing)]
    [TestCase("podman", NodeCapabilityDiagnostic.EngineUnsupported)]
    public void MissingOrUnsupportedEngineIsNotInvented(string? engine, NodeCapabilityDiagnostic code)
    {
        var result = NodeCapabilityValidator.Validate(SupportedReport() with { Engine = engine });
        Assert.That(result.Report, Is.Null);
        Assert.That(result.Diagnostics, Does.Contain(code));
    }

    [TestCase("windows", "x64", "v2", NodeCapabilityDiagnostic.OperatingSystemUnsupported)]
    [TestCase("linux", "arm64", "v2", NodeCapabilityDiagnostic.ArchitectureUnsupported)]
    [TestCase("linux", "x64", "v1", NodeCapabilityDiagnostic.CgroupModeUnsupported)]
    [TestCase(null, null, null, NodeCapabilityDiagnostic.CgroupModeMissing)]
    public void UnsupportedPlatformFactsFailClosed(string? os, string? architecture,
        string? cgroup, NodeCapabilityDiagnostic code)
    {
        var report = SupportedReport() with { OperatingSystem = os, Architecture = architecture, CgroupMode = cgroup };
        Assert.That(NodeCapabilityValidator.Validate(report).Diagnostics, Does.Contain(code));
    }

    [TestCase(null, NodeCapabilityDiagnostic.EngineVersionMissing)]
    [TestCase("secret\nvalue", NodeCapabilityDiagnostic.InvalidText)]
    public void VersionDiagnosticsDoNotEchoInput(string? version, NodeCapabilityDiagnostic code)
    {
        var result = NodeCapabilityValidator.Validate(SupportedReport() with { EngineVersion = version });
        Assert.That(result.Report, Is.Null);
        Assert.That(result.Diagnostics, Does.Contain(code));
        Assert.That(string.Join(",", result.Diagnostics), Does.Not.Contain("secret"));
    }

    [Test]
    public void TextBoundIsInclusiveAndExcessIsRejected()
    {
        var report = SupportedReport() with { EngineVersion = new string('v', NodeCapabilityValidator.MaximumTextLength) };
        Assert.That(NodeCapabilityValidator.Validate(report).IsValid, Is.True);
        Assert.That(NodeCapabilityValidator.Validate(report with { EngineVersion = report.EngineVersion + "v" }).Diagnostics,
            Does.Contain(NodeCapabilityDiagnostic.InvalidText));
    }

    [Test]
    public void StorageBoundIsInclusiveAndExcessIsRejectedBeforeInspection()
    {
        var storage = Enumerable.Range(1, NodeCapabilityValidator.MaximumStorageIdentities)
            .Select(i => new NodeStorageCapability(Guid.NewGuid(), Guid.NewGuid().ToString("D"), "xfs", i)).ToArray();
        var report = SupportedReport() with { StorageIdentities = storage };
        Assert.That(NodeCapabilityValidator.Validate(report).IsValid, Is.True);
        var oversized = storage.Append(null!).ToArray();
        Assert.That(NodeCapabilityValidator.Validate(report with { StorageIdentities = oversized }).Diagnostics,
            Is.EqualTo(new[] { NodeCapabilityDiagnostic.TooManyStorageIdentities }));
    }

    [Test]
    public void MissingStorageAndNullEntriesAreInvalid()
    {
        Assert.That(NodeCapabilityValidator.Validate(SupportedReport() with { StorageIdentities = null }).Diagnostics,
            Does.Contain(NodeCapabilityDiagnostic.StorageMissing));
        Assert.That(NodeCapabilityValidator.Validate(SupportedReport() with { StorageIdentities = [] }).Diagnostics,
            Does.Contain(NodeCapabilityDiagnostic.StorageMissing));
        Assert.That(NodeCapabilityValidator.Validate(SupportedReport() with { StorageIdentities = [null!] }).Diagnostics,
            Does.Contain(NodeCapabilityDiagnostic.InvalidStorageIdentity));
    }

    [TestCase("not-a-uuid", 1)]
    [TestCase("00000000-0000-0000-0000-000000000000", 1)]
    [TestCase("40000000-0000-0000-0000-000000000001", 0)]
    [TestCase("40000000-0000-0000-0000-000000000001", null)]
    public void InvalidQuotaIdentityIsRejected(string uuid, int? project)
    {
        var report = SupportedReport() with { StorageIdentities = [new(Guid.NewGuid(), uuid, "xfs", project)] };
        Assert.That(NodeCapabilityValidator.Validate(report).Diagnostics,
            Does.Contain(NodeCapabilityDiagnostic.InvalidStorageIdentity));
    }

    [Test]
    public void FilesystemAndVolumeIdentityAreValidated()
    {
        var report = SupportedReport() with { StorageIdentities = [new(Guid.Empty, Guid.NewGuid().ToString("D"), "ext4", 1)] };
        var result = NodeCapabilityValidator.Validate(report);
        Assert.That(result.Diagnostics, Does.Contain(NodeCapabilityDiagnostic.InvalidStorageIdentity));
        Assert.That(result.Diagnostics, Does.Contain(NodeCapabilityDiagnostic.FilesystemUnsupported));
    }

    [Test]
    public void SharedQuotaDomainAliasesAreAllowedButDuplicateVolumesAreRejected()
    {
        var volume = SupportedReport().StorageIdentities![0];
        var report = SupportedReport() with { StorageIdentities = [volume, volume with { VolumeId = Guid.NewGuid() }] };
        Assert.That(NodeCapabilityValidator.Validate(report).IsValid, Is.True);
        Assert.That(NodeCapabilityValidator.Validate(report with { StorageIdentities = [volume, volume] }).Diagnostics,
            Does.Contain(NodeCapabilityDiagnostic.DuplicateVolumeIdentity));
    }

    [Test]
    public void AcceptedSnapshotIsIndependentOfMutableCallerCollection()
    {
        var original = SupportedReport();
        var storage = original.StorageIdentities!.ToArray();
        var result = NodeCapabilityValidator.Validate(original with { StorageIdentities = storage });
        var expected = storage[0];
        storage[0] = expected with { ProjectId = 0 };
        Assert.That(result.Report!.StorageIdentities![0], Is.EqualTo(expected));
        var exposed = (IList<NodeStorageCapability>)result.Report.StorageIdentities;
        Assert.That(() => exposed[0] = storage[0], Throws.TypeOf<NotSupportedException>());
    }
}
