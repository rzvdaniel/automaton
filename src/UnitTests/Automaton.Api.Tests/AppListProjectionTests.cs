using System.Text.Json;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;

namespace Automaton.Api.Tests;

public sealed class AppListProjectionTests
{
    [Test]
    public void ParsesPresentationTagsWithoutNormalizingStoredValue()
    {
        Assert.That(AppsService.ParseTags(" games, strategy ,, "), Is.EqualTo(new[] { "games", "strategy" }));
        Assert.That(AppsService.ParseTags(null), Is.Empty);
    }

    [Test]
    public void ListDtoKeepsRuntimeTimestampsSeparate()
    {
        var dto = new AppListItemDto
        {
            Id = Guid.NewGuid(),
            Name = "demo",
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-2),
            Runtime = new AppListRuntimeDto
            {
                FrontendAvailable = true,
                BackendAvailable = false,
                FrontendDeployedAtUtc = DateTime.UtcNow.AddHours(-1)
            }
        };

        Assert.That(dto.UpdatedAtUtc, Is.Not.EqualTo(dto.Runtime.FrontendDeployedAtUtc));
        Assert.That(dto.Runtime.BackendDeployedAtUtc, Is.Null);
    }

    [Test]
    public void RunnerConnectivityUsesCanonicalStringContract()
    {
        var json = JsonSerializer.Serialize(new AppListRunnerDto
        {
            Id = Guid.NewGuid(), Name = "runner", Purpose = "managed", ConnectivityStatus = RunnerStatus.Online
        });

        Assert.That(json, Does.Contain("\"ConnectivityStatus\":1"));
    }
}
