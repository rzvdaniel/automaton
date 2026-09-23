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

    [Test]
    public async Task HubConnection_RejectsUntrustedServerCertificate()
    {
        await using var server = new LoopbackTlsServer();
        var root = Path.Combine(Path.GetTempPath(), "automaton-runner-tls-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousDirectory = Environment.CurrentDirectory;
        var names = new[] { "DOTNET_ENVIRONMENT", "AUTOMATON_SERVER_URL", "AUTOMATON_API_KEY", "USERPROFILE" };
        var previousValues = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            Environment.CurrentDirectory = root;
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
            Environment.SetEnvironmentVariable("AUTOMATON_SERVER_URL", $"https://localhost:{server.Port}");
            Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", "ak_test-runner-certificate");
            Environment.SetEnvironmentVariable("USERPROFILE", null);

            using var host = Program.CreateHostBuilder([]).Build();
            await host.Services.GetRequiredService<FileStorageService>()
                .SaveRunnerConfigAsync(Guid.NewGuid().ToString(), "test-runner");
            var hub = host.Services.GetRequiredService<HubService>();
            try
            {
                Assert.That(await hub.ConnectAsync(), Is.True);
                await server.Completion.WaitAsync(TimeSpan.FromSeconds(10));

                Assert.That(server.AcceptedConnection, Is.True);
                Assert.That(server.ReceivedApplicationData, Is.False,
                    "SignalR must not send its negotiate request after an untrusted TLS handshake.");
                Assert.That(hub.IsConnected(), Is.False);
            }
            finally
            {
                await hub.DisconnectAsync();
            }
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            foreach (var (name, value) in previousValues)
                Environment.SetEnvironmentVariable(name, value);
            Directory.Delete(root, recursive: true);
        }
    }
}
