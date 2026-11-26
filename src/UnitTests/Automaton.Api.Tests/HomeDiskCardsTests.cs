using Automaton.Api.Home;
using Automaton.Core.Models;

namespace Automaton.Api.Tests;

[TestFixture]
public class HomeDiskCardsTests
{
    private const long Gib = 1024L * 1024 * 1024;

    private static MachineHostHealth Host(params HostDisk[] disks) => new(DateTime.UtcNow, null, null, null, null, null, null, null, null, null, null,
        100 * Gib, 50 * Gib, null, null, null, null, null, null, null, null, null, Disks: disks.Length == 0 ? null : disks);

    [Test]
    public void OnlyTheFullDiskGetsACardAndItIsNamed()
    {
        var cards = HomeNeedsYouService.DiskCards([Host(new HostDisk("/", 38 * Gib, 5 * Gib), new HostDisk("/mnt/docker-data", 40 * Gib, 29 * Gib))]);
        Assert.That(cards.Select(card => card.Key), Is.EqualTo(new[] { "disk-low:/" }));
        Assert.That(cards[0].Title, Does.Contain("main disk"));
        Assert.That(cards[0].Detail, Does.Contain("10 GiB"));
    }

    [Test]
    public void DockerDiskOver80PercentWarnsByItself()
    {
        var cards = HomeNeedsYouService.DiskCards([Host(new HostDisk("/", 100 * Gib, 50 * Gib), new HostDisk("/mnt/docker-data", 40 * Gib, 6 * Gib))]);
        Assert.That(cards.Single().Title, Is.EqualTo("The Docker disk is low"));
        Assert.That(cards.Single().Detail, Does.Contain("/mnt/docker-data"));
    }

    [Test]
    public void MainDiskUnderTenGibWarnsBelowEightyPercent()
    {
        var cards = HomeNeedsYouService.DiskCards([Host(new HostDisk("/", 15 * Gib, 8 * Gib))]);
        Assert.That(cards, Has.Count.EqualTo(1));
    }

    [Test]
    public void HealthyDisksAndNoReportGiveNoCards()
    {
        Assert.That(HomeNeedsYouService.DiskCards([Host(new HostDisk("/", 100 * Gib, 50 * Gib)), null]), Is.Empty);
    }

    [Test]
    public void HostWithoutDiskListIsJudgedByItsSingleDisk()
    {
        var host = Host() with { DiskTotalBytes = 100 * Gib, DiskAvailableBytes = 5 * Gib };
        Assert.That(HomeNeedsYouService.DiskCards([host]).Single().Title, Is.EqualTo("The disk is low"));
    }
}
