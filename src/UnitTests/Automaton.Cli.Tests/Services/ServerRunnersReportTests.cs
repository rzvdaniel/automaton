using System.Net;
using System.Text;
using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class ServerRunnersReportTests
{
    [Test]
    public async Task EveryRunnerIsNamedWithItsState()
    {
        var output = await ReportAsync("""[{"id":"1","name":"main","upgradeAvailable":false},{"id":"2","name":"edge","upgradeAvailable":true}]""");

        Assert.That(output, Does.Contain("Runner main: on the current image."));
        Assert.That(output, Does.Contain("Runner edge is on an old image. Upgrade it with: automaton runners upgrade 2 --wait"));
    }

    [Test]
    public async Task NoRunnersIsSaidOutLoud()
    {
        Assert.That(await ReportAsync("[]"), Does.Contain("No Runners are registered."));
    }

    private static async Task<string> ReportAsync(string runnersJson)
    {
        using var http = new HttpClient(new Handler(runnersJson)) { BaseAddress = new Uri("https://api.example.test/") };
        using var client = new AutomatonApiClient(http, "ak_test");
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try { await ServerCommand.ReportRunnersAsync(client, false, false, CancellationToken.None); }
        finally { Console.SetOut(original); }
        return writer.ToString();
    }

    private sealed class Handler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
