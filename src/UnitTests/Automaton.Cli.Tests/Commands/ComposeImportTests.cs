using System.Text.Json;
using Automaton.Cli.Services;
using Automaton.Core.Models;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class ComposeImportTests
{
    // Adminer's compose file as published in the Docker Official Image documentation.
    private const string Adminer = """
        # Use root/example as user/password credentials
        services:
          adminer:
            image: adminer
            restart: always
            ports:
              - 8080:8080
        """;

    [Test]
    public void Adminer_BecomesAnAppTheRuntimeAccepts()
    {
        var result = ComposeImportService.Import(Adminer, "Adminer");

        var compose = ApplicationComposeParser.Parse(result.ComposeYaml);
        Assert.Multiple(() =>
        {
            Assert.That(compose.PublicService, Is.EqualTo("adminer"));
            Assert.That(compose.Services.Single().Port, Is.EqualTo(8080));
            Assert.That(result.Notes, Has.Some.Contain("restart"));
            var manifest = JsonDocument.Parse(result.ManifestJson).RootElement;
            Assert.That(manifest.GetProperty("name").GetString(), Is.EqualTo("Adminer"));
            Assert.That(manifest.GetProperty("application").GetProperty("health").GetProperty("path").GetString(), Is.EqualTo("/"));
        });
    }

    [Test]
    public void ListEnvironmentAndMappedDependencies_AreNormalized()
    {
        var result = ComposeImportService.Import("""
            services:
              web:
                image: app:1
                ports: ["127.0.0.1:3000:3000/tcp"]
                environment: ["MODE=prod"]
                depends_on: { db: { condition: service_started } }
                volumes: ["data:/data"]
              db:
                image: postgres:16
            volumes:
              data:
            """, "App");

        var compose = ApplicationComposeParser.Parse(result.ComposeYaml);
        Assert.Multiple(() =>
        {
            Assert.That(compose.Services.Single(s => s.Name == "web").Environment["MODE"], Is.EqualTo("prod"));
            Assert.That(compose.Services.Single(s => s.Name == "web").DependsOn, Is.EqualTo(new[] { "db" }));
            Assert.That(compose.Volumes, Is.EqualTo(new[] { "data" }));
        });
    }

    [TestCase("services:\n  a:\n    build: .\n    ports: ['80']", "build")]
    [TestCase("services:\n  a:\n    image: x\n    ports: ['80']\n  b:\n    image: y\n    ports: ['81']", "More than one")]
    [TestCase("services:\n  a:\n    image: x\n    ports: ['80']\n    env_file: .env", "env_file")]
    [TestCase("x: &a [1]\nservices:\n  a:\n    image: x\n    ports: ['80']\n    command: *a", "anchors")]
    [TestCase("services:\n  a:\n    image: x\n    privileged: true", "privileged")]
    [TestCase("services:\n  a:\n    image: x\n    ports: ['80']\n    volumes: ['/etc:/etc']", "host mounts")]
    [TestCase("services:\n  a:\n    image: x\n    ports: ['80']\n    environment: ['TOKEN=$SECRET']", "interpolation")]
    public void Unsafe_Compose_IsRefused(string yaml, string reason) =>
        Assert.That(() => ComposeImportService.Import(yaml, "App"), Throws.InvalidOperationException.With.Message.Contains(reason));
}
