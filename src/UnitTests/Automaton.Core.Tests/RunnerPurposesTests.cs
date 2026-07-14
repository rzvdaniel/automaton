using Automaton.Core.Enums;

namespace Automaton.Core.Tests;

public class RunnerPurposesTests
{
    [Test]
    public void Normalize_DefaultsUnknownAndEmptyValuesToManaged()
    {
        Assert.That(RunnerPurposes.Normalize(null), Is.EqualTo(RunnerPurposes.Managed));
        Assert.That(RunnerPurposes.Normalize(string.Empty), Is.EqualTo(RunnerPurposes.Managed));
        Assert.That(RunnerPurposes.Normalize("unknown"), Is.EqualTo(RunnerPurposes.Managed));
    }

    [Test]
    public void Normalize_PreservesInfrastructurePurposeCaseInsensitively()
    {
        Assert.That(RunnerPurposes.Normalize("infrastructure"), Is.EqualTo(RunnerPurposes.Infrastructure));
    }
}
