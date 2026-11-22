using System.Text.Json;
using Automaton.Cli.Commands;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public sealed class ActionScheduleCommandTests
{
    private static readonly JsonElement Schedules = JsonDocument.Parse("""
        [
          {"id":"938a5dc9-1111-1111-1111-111111111111","applicationId":"aaaaaaaa-0000-0000-0000-000000000001","actionKey":"pick-up","name":"Pick up","scheduleType":"Cron","cronExpression":"0 2 * * *","timeZone":"UTC","enabled":true,"nextRunAtUtc":"2026-11-23T02:00:00Z","concurrencyPolicy":"SkipIfRunning","misfirePolicy":"Skip","executionScope":"AppRunner","inputJson":"{\"a\":1}"},
          {"id":"938a5dc9-2222-2222-2222-222222222222","applicationId":"aaaaaaaa-0000-0000-0000-000000000002","actionKey":"x","name":"X","scheduleType":"Interval","intervalSeconds":600,"timeZone":"UTC","enabled":false,"inputJson":"{}"},
          {"id":"11111111-3333-3333-3333-333333333333","applicationId":"aaaaaaaa-0000-0000-0000-000000000001","actionKey":"y","name":"Y","scheduleType":"Cron","cronExpression":"*/5 * * * *","timeZone":"UTC","enabled":true,"inputJson":"{}"}
        ]
        """).RootElement.Clone();

    private static List<JsonElement> All => Schedules.EnumerateArray().ToList();

    [TestCase("* * * * * *", "five fields")]
    [TestCase("0 2 * *", "five fields")]
    [TestCase("61 * * * *", "valid cron")]
    public void ParseCron_RefusesWithAReason(string cron, string reason)
        => Assert.That(() => ActionScheduleCommand.ParseCron(cron), Throws.InvalidOperationException.With.Message.Contain(reason));

    [Test]
    public void ParseCron_AcceptsStandardCron() => Assert.That(() => ActionScheduleCommand.ParseCron("*/15 * * * 1-5"), Throws.Nothing);

    [Test]
    public void CheckTimeZone_RefusesUnknownZones()
        => Assert.That(() => ActionScheduleCommand.CheckTimeZone("Mars/Base"), Throws.InvalidOperationException.With.Message.Contain("time zone"));

    [Test]
    public void NextRuns_ReturnsThreeFutureRuns()
        => Assert.That(ActionScheduleCommand.NextRuns(ActionScheduleCommand.ParseCron("0 2 * * *"), "Europe/Bucharest"), Has.Count.EqualTo(3));

    [Test]
    public void Pick_FindsByPrefix_AndRefusesAmbiguousOrUnknown()
    {
        Assert.That(ActionScheduleCommand.Pick(All, "11111111", null).GetProperty("actionKey").GetString(), Is.EqualTo("y"));
        Assert.That(() => ActionScheduleCommand.Pick(All, "938a5dc9", null), Throws.InvalidOperationException.With.Message.Contain("more of the ID"));
        Assert.That(ActionScheduleCommand.Pick(All, "938a5dc9-1111", "aaaaaaaa-0000-0000-0000-000000000001").GetProperty("actionKey").GetString(), Is.EqualTo("pick-up"));
        Assert.That(ActionScheduleCommand.Pick(All, "938a5dc9", "aaaaaaaa-0000-0000-0000-000000000002").GetProperty("actionKey").GetString(), Is.EqualTo("x"));
        Assert.That(() => ActionScheduleCommand.Pick(All, "deadbeef", null), Throws.InvalidOperationException.With.Message.Contain("No schedule"));
    }

    [Test]
    public void Table_ListsIdActionCronZoneStateAndNext()
    {
        var table = ActionScheduleCommand.Table(ActionScheduleCommand.Visible(All, "aaaaaaaa-0000-0000-0000-000000000001"));
        Assert.That(table, Does.Contain("ID").And.Contain("938a5dc9").And.Contain("pick-up").And.Contain("0 2 * * *").And.Contain("yes").And.Contain("2026-11-23 02:00"));
        Assert.That(table, Does.Not.Contain("(interval)"));
    }

    [Test]
    public void BuildEditRequest_ChangesOnlyTheGivenOptions()
    {
        var request = ActionScheduleCommand.BuildEditRequest(All[0], "*/15 * * * *", null, null, null, null);
        Assert.That(request["cronExpression"], Is.EqualTo("*/15 * * * *"));
        Assert.That(request["timeZone"], Is.EqualTo("UTC"));
        Assert.That(request["name"], Is.EqualTo("Pick up"));
        Assert.That(request["enabled"], Is.EqualTo(true));
        Assert.That(request["concurrencyPolicy"]?.ToString(), Is.EqualTo("SkipIfRunning"));
        Assert.That(((JsonElement)request["input"]!).GetProperty("a").GetInt32(), Is.EqualTo(1));
        var overlap = ActionScheduleCommand.BuildEditRequest(All[0], null, "Europe/Bucharest", null, "New", true);
        Assert.That(overlap["concurrencyPolicy"], Is.EqualTo("Allow"));
        Assert.That(overlap["timeZone"], Is.EqualTo("Europe/Bucharest"));
        Assert.That(overlap["cronExpression"]?.ToString(), Is.EqualTo("0 2 * * *"));
    }

    [Test]
    public void BuildEditRequest_TurnsAnIntervalScheduleIntoCron()
    {
        var request = ActionScheduleCommand.BuildEditRequest(All[1], "0 * * * *", null, null, null, null);
        Assert.That(request["scheduleType"], Is.EqualTo("Cron"));
        Assert.That(request["intervalSeconds"], Is.Null);
    }
}
