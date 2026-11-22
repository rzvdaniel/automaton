using System.Text.Json;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public sealed class CatalogScheduleSyncTests
{
    private static List<JsonElement> Many(string json) => JsonDocument.Parse(json).RootElement.EnumerateArray().Select(item => item.Clone()).ToList();

    private const string Old = """[{"name":"Night shift","action":"run-job","cron":"0 22 * * *"},{"name":"upload","action":"upload","cron":"30 4 * * *"},{"name":"gone","action":"x","cron":"0 1 * * *"}]""";
    private const string New = """[{"name":"Pick-up","action":"run-job","cron":"0 22 * * *","input":{"job":"pick-up"}},{"name":"upload","action":"upload","cron":"*/15 * * * *"},{"name":"fresh","action":"y","cron":"0 2 * * *"}]""";

    private static string Row(string id, string name, string action, string cron, string input = "{}") =>
        $$"""{"id":"{{id}}","name":"App: {{name}}","actionKey":"{{action}}","cronExpression":"{{cron}}","timeZone":"Etc/UTC","inputJson":{{JsonSerializer.Serialize(input)}},"enabled":true}""";

    private static List<ScheduleChange> Plan(string existing, bool remove = true)
    {
        var all = Many(New);
        return CatalogScheduleSync.Plan("App", Many(existing), Many(Old), all, all, remove);
    }

    [Test]
    public void RenamesAnUnchangedDefault_UpdatesAnotherCron_AddsNew_RemovesDropped()
    {
        var plan = Plan($"[{Row("00000000-0000-0000-0000-000000000001", "Night shift", "run-job", "0 22 * * *")},{Row("00000000-0000-0000-0000-000000000002", "upload", "upload", "30 4 * * *")},{Row("00000000-0000-0000-0000-000000000003", "gone", "x", "0 1 * * *")}]");

        Assert.That(plan.Select(change => (change.Kind, change.Name)), Is.EquivalentTo(new[]
        {
            (ScheduleChange.Update, "Pick-up"), (ScheduleChange.Update, "upload"), (ScheduleChange.Add, "fresh"), (ScheduleChange.Remove, "gone")
        }));
        Assert.That(plan.Single(change => change.Name == "Pick-up").Detail, Does.Contain("rename Night shift → Pick-up").And.Contain("input added"));
        Assert.That(plan.Single(change => change.Name == "upload").Detail, Does.Contain("30 4 * * * → */15 * * * *"));
    }

    [Test]
    public void AScheduleEditedAfterTheInstallIsKeptAndListed()
    {
        var plan = Plan($"[{Row("00000000-0000-0000-0000-000000000001", "Night shift", "run-job", "0 22 * * *")},{Row("00000000-0000-0000-0000-000000000002", "upload", "upload", "0 9 * * *")}]");

        var upload = plan.Single(change => change.Name == "upload");
        Assert.That(upload.Kind, Is.EqualTo(ScheduleChange.Keep));
        Assert.That(plan.Count(change => change.Kind == ScheduleChange.Keep), Is.EqualTo(1));
        Assert.That(plan.Any(change => change.Kind == ScheduleChange.Update && change.Name == "Pick-up"), Is.True);
    }

    [Test]
    public void AnEditedDroppedDefaultIsKeptNotRemoved()
    {
        var plan = Plan($"[{Row("00000000-0000-0000-0000-000000000003", "gone", "x", "0 5 * * *")}]");
        Assert.That(plan.Single(change => change.Name == "gone").Kind, Is.EqualTo(ScheduleChange.Keep));
    }

    [Test]
    public void WithoutThePreviousManifest_NothingExistingIsTouched()
    {
        var all = Many(New);
        var plan = CatalogScheduleSync.Plan("App", Many($"[{Row("00000000-0000-0000-0000-000000000002", "upload", "upload", "30 4 * * *")}]"), null, all, all, true);
        Assert.That(plan.Where(change => change.Kind is ScheduleChange.Update or ScheduleChange.Remove), Is.Empty);
        Assert.That(plan.Count(change => change.Kind == ScheduleChange.Add), Is.EqualTo(2));
    }

    [Test]
    public void AnAlreadyCurrentScheduleNeedsNothing()
    {
        var all = Many("""[{"name":"upload","action":"upload","cron":"*/15 * * * *","input":{"b":1,"a":2}}]""");
        var plan = CatalogScheduleSync.Plan("App", Many($"[{Row("00000000-0000-0000-0000-000000000002", "upload", "upload", "*/15 * * * *", "{\"a\":2,\"b\":1}")}]"), all, all, all, true);
        Assert.That(plan, Is.Empty);
    }

    [Test]
    public void NamedSchedulesNeverRemoveAnything()
        => Assert.That(Plan($"[{Row("00000000-0000-0000-0000-000000000003", "gone", "x", "0 1 * * *")}]", remove: false).Any(change => change.Kind == ScheduleChange.Remove), Is.False);

    [Test]
    public async Task Apply_PutsTheNewCronNameInputAndKeepsTheSwitch_DeletesAndCreates()
    {
        var calls = new List<(string Method, string Path, string Body)>();
        using var http = new HttpClient(new Stub(request =>
        {
            calls.Add((request.Method.Method, request.RequestUri!.AbsolutePath, request.Content?.ReadAsStringAsync().Result ?? ""));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://api.example.com/") };
        using var client = new AutomatonApiClient(http, "test-key");
        var plan = Plan($"[{Row("00000000-0000-0000-0000-000000000001", "Night shift", "run-job", "0 22 * * *").Replace("\"enabled\":true", "\"enabled\":false")},{Row("00000000-0000-0000-0000-000000000003", "gone", "x", "0 1 * * *")}]");

        var (created, updated, removed) = await CatalogScheduleSync.ApplyAsync(client, "app-1", "App", plan);

        Assert.That((created, updated, removed), Is.EqualTo((new List<string> { "upload", "fresh" }, new List<string> { "Pick-up" }, new List<string> { "gone" })));
        var put = calls.Single(call => call.Method == "PUT");
        Assert.That(put.Path, Does.EndWith("00000000-0000-0000-0000-000000000001"));
        using var body = JsonDocument.Parse(put.Body);
        Assert.That(body.RootElement.GetProperty("name").GetString(), Is.EqualTo("App: Pick-up"));
        Assert.That(body.RootElement.GetProperty("input").GetProperty("job").GetString(), Is.EqualTo("pick-up"));
        Assert.That(body.RootElement.GetProperty("enabled").GetBoolean(), Is.False);
        Assert.That(calls.Count(call => call.Method == "DELETE"), Is.EqualTo(1));
        Assert.That(calls.Count(call => call.Method == "POST"), Is.EqualTo(2));
    }

    [Test]
    public async Task Install_CreatesEveryScheduleWithItsCronTimeZoneAndInput()
    {
        var posted = new List<JsonElement>();
        using var http = new HttpClient(new Stub(request =>
        {
            if (request.Method == HttpMethod.Get) return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json") };
            posted.Add(JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result).RootElement.Clone());
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://api.example.com/") };
        using var client = new AutomatonApiClient(http, "test-key");
        var application = JsonDocument.Parse("""{"schedules":[{"name":"Morning report","action":"run-job","cron":"0 6 * * *","timeZone":"Europe/Bucharest","input":{"job":"report"}},{"name":"Lessons","action":"run-job","cron":"0 3 * * *","input":{"job":"lessons"}}]}""").RootElement;

        var (created, _) = await CatalogSchedules.CreateAsync(client, "app-1", "Overseer", CatalogSchedules.Select(application, false, []));

        Assert.That(created, Is.EqualTo(new[] { "Morning report", "Lessons" }));
        Assert.That(posted[0].GetProperty("cronExpression").GetString(), Is.EqualTo("0 6 * * *"));
        Assert.That(posted[0].GetProperty("timeZone").GetString(), Is.EqualTo("Europe/Bucharest"));
        Assert.That(posted[0].GetProperty("input").GetProperty("job").GetString(), Is.EqualTo("report"));
        Assert.That(posted[1].GetProperty("input").GetProperty("job").GetString(), Is.EqualTo("lessons"));
    }

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
