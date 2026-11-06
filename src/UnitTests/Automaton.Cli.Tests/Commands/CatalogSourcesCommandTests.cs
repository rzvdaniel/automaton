using System.Net;
using System.Text;
using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class CatalogSourcesCommandTests
{
    private const string Hint = "automaton catalog sources add acme/apps --private --token-file ~/catalog-token";

    [Test]
    public void Sources_OffersListAddEnableDisableRemove() =>
        Assert.That(CatalogSourcesCommand.Build(new ConfigService()).Subcommands.Select(c => c.Name),
            Is.EquivalentTo(new[] { "list", "add", "enable", "disable", "remove" }));

    [Test]
    public void Add_HasNoOptionThatTakesTheTokenItself()
    {
        var add = CatalogSourcesCommand.Build(new ConfigService()).Subcommands.Single(c => c.Name == "add");
        var tokenOptions = add.Options.Where(o => o.Name.Contains("token")).ToList();
        Assert.That(tokenOptions.Select(o => o.Name), Is.EquivalentTo(new[] { "token-file", "token-stdin" }));
        Assert.That(tokenOptions.Single(o => o.Name == "token-file").ValueType, Is.EqualTo(typeof(string)));
        Assert.That(tokenOptions.Single(o => o.Name == "token-stdin").ValueType, Is.EqualTo(typeof(bool)));
        Assert.That(add.Description, Does.Contain("--token-file").And.Contain("--dry-run"));
    }

    [Test]
    public void TheToken_IsReadFromAFile_TrimmedAndNeverFromAnArgument()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "github_pat_abc\n");
            Assert.That(CatalogSourceToken.Read(path, false, false, Hint), Is.EqualTo("github_pat_abc"));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void TheToken_CanComeFromStandardInput() =>
        Assert.That(CatalogSourceToken.Read(null, true, false, Hint, stdin: new StringReader("github_pat_abc\r\n")), Is.EqualTo("github_pat_abc"));

    [Test]
    public void APublicSource_NeedsNoToken() =>
        Assert.That(CatalogSourceToken.Read(null, false, false, Hint), Is.Null);

    [Test]
    public void APrivateSource_InATerminal_AsksForTheTokenWithAPrompt() =>
        Assert.That(CatalogSourceToken.Read(null, false, true, Hint, inputRedirected: false, prompt: () => " github_pat_abc "), Is.EqualTo("github_pat_abc"));

    [Test]
    public void APrivateSource_WithoutAWayToAsk_StopsAndNamesTheCommandThePersonRuns()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => CatalogSourceToken.Read(null, false, true, Hint, inputRedirected: true));
        Assert.That(failure!.Message, Does.Contain(Hint).And.Contain("Contents: Read-only").And.Contain("Nothing was added"));
    }

    [TestCase("")]
    [TestCase("two words")]
    public void ABadToken_IsRefusedWithoutEchoingIt(string token)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => CatalogSourceToken.Read(null, true, false, Hint, stdin: new StringReader(token)));
        Assert.That(failure!.Message, Does.Not.Contain("two"));
    }

    [Test]
    public void TheHint_FillsInTheRepositoryAndQuotesAndCarriesNoSecret() =>
        Assert.That(CatalogSourcesCommand.AddHint("https://github.com/acme/apps", "dev", "Acme apps"),
            Is.EqualTo("automaton catalog sources add https://github.com/acme/apps --branch dev --name 'Acme apps' --private --token-file ~/catalog-token"));

    [Test]
    public async Task TheClient_SendsATokenInTheBody_ADryRunInTheQuery_AndExplainsAForbiddenKey()
    {
        var requests = new List<(HttpMethod Method, string Url, string Body)>();
        var status = HttpStatusCode.OK;
        using var http = new HttpClient(new Stub(async request =>
        {
            requests.Add((request.Method, request.RequestUri!.PathAndQuery, request.Content is null ? "" : await request.Content.ReadAsStringAsync()));
            return new HttpResponseMessage(status) { Content = new StringContent("{\"name\":\"Acme\",\"appCount\":2}", Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://api.example.test/") };
        using var client = new AutomatonApiClient(http, "ak_x");

        await client.AddCatalogSourceAsync("acme/apps", null, null, "github_pat_x", dryRun: true);
        await client.SetCatalogSourceEnabledAsync("abc", false);
        await client.RemoveCatalogSourceAsync("abc");
        status = HttpStatusCode.Forbidden;
        var denied = Assert.ThrowsAsync<InvalidOperationException>(() => client.ListCatalogSourcesAsync());

        Assert.Multiple(() =>
        {
            Assert.That(requests[0].Url, Is.EqualTo("/api/admin/catalog/sources?dryRun=true"));
            Assert.That(requests[0].Body, Does.Contain("github_pat_x"));
            Assert.That(requests[1], Is.EqualTo((HttpMethod.Put, "/api/admin/catalog/sources/abc/enabled", "{\"enabled\":false}")));
            Assert.That(requests[2].Method, Is.EqualTo(HttpMethod.Delete));
            Assert.That(denied!.Message, Does.Contain("system administrator"));
        });
    }

    private sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
