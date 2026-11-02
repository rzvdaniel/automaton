using System.Security.Authentication;
using Automaton.UnitTests.Shared;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
[NonParallelizable]
public sealed class RunnerCertificateValidationTests
{
    [Test]
    public async Task DevelopmentHttpClient_RejectsUntrustedServerCertificate()
    {
        var previousEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        try
        {
            await using var server = new LoopbackTlsServer();
            using var host = Program.CreateHostBuilder([]).Build();
            using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient();

            var exception = Assert.ThrowsAsync<HttpRequestException>(
                () => client.GetAsync($"https://localhost:{server.Port}/"));

            Assert.That(exception!.InnerException, Is.TypeOf<AuthenticationException>(), server.ServerException?.ToString());
            Assert.That(server.AcceptedConnection, Is.True);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousEnvironment);
        }
    }

}
