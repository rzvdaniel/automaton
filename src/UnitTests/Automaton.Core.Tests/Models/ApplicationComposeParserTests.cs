using Automaton.Core.Models;
using NUnit.Framework;

namespace Automaton.Core.Tests.Models;

public sealed class ApplicationComposeParserTests
{
    [Test]
    public void Parse_AcceptsImageServicesOnePublicPortInternalNetworkingAndManagedVolumes()
    {
        var compose = ApplicationComposeParser.Parse("""
            services:
              web:
                image: nginx:1.27
                working_dir: /srv/web
                ports:
                  - "8080"
                environment:
                  APP_MODE: production
                volumes:
                  - content:/usr/share/nginx/html
                depends_on:
                  - content-api
              content-api:
                image: ghcr.io/example/content-api:v1
                expose:
                  - "9000"
                volumes:
                  - content:/var/lib/content
            volumes:
              content: {}
            """);

        Assert.That(compose.PublicService, Is.EqualTo("web"));
        Assert.That(compose.Services, Has.Count.EqualTo(2));
        Assert.That(compose.Services.Single(service => service.Name == "web").Port, Is.EqualTo(8080));
        Assert.That(compose.Volumes, Is.EqualTo(new[] { "content" }));
    }

    [TestCase("build: .")]
    [TestCase("privileged: true")]
    [TestCase("secrets: [db_password]")]
    [TestCase("volumes: [/var/run/docker.sock:/var/run/docker.sock]")]
    public void Parse_RejectsUnsupportedServiceFields(string field)
    {
        var yaml = $"services:\n  web:\n    image: nginx:latest\n    ports: [\"8080\"]\n    {field.Replace("\n", "\n    ", StringComparison.Ordinal)}";
        Assert.Throws<InvalidOperationException>(() => ApplicationComposeParser.Parse(yaml));
    }

    [Test]
    public void Parse_RejectsHostPortMappingAndMultiplePublicServices()
    {
        Assert.Throws<InvalidOperationException>(() => ApplicationComposeParser.Parse("""
            services:
              web:
                image: nginx
                ports: ["127.0.0.1:8080:80"]
            """));

        Assert.Throws<InvalidOperationException>(() => ApplicationComposeParser.Parse("""
            services:
              web:
                image: nginx
                ports: ["8080"]
              other:
                image: nginx
                ports: ["8081"]
            """));
    }

    [Test]
    public void Parse_RejectsAnInternalServiceThatAlsoDeclaresAPort()
    {
        Assert.Throws<InvalidOperationException>(() => ApplicationComposeParser.Parse("""
            services:
              web:
                image: nginx
                ports: ["8080"]
              worker:
                image: worker
                ports: ["9000"]
            """));
    }

    [Test]
    public void Parse_RejectsComposeSecretAndEnvironmentInterpolation()
    {
        Assert.Throws<InvalidOperationException>(() => ApplicationComposeParser.Parse("""
            services:
              web:
                image: nginx
                ports: ["8080"]
                environment:
                  API_PASSWORD: ${API_PASSWORD}
            """));
    }

    [TestCase("command: \"python -m http.server 8080\"")]
    [TestCase("entrypoint: \"python -m http.server 8080\"")]
    public void Parse_RejectsScalarCommandAndEntrypoint(string field)
    {
        var yaml = $"services:\n  web:\n    image: python:3.12\n    {field}";
        Assert.Throws<InvalidOperationException>(() => ApplicationComposeParser.Parse(yaml));
    }

    [Test]
    public void Parse_PreservesListFormCommandAndEntrypointArguments()
    {
        var compose = ApplicationComposeParser.Parse("""
            services:
              web:
                image: python:3.12
                ports: ["8080"]
                command: [python, -m, http.server, "8080"]
                entrypoint: [/usr/local/bin/python]
            """);

        Assert.That(compose.Services.Single().Command, Is.EqualTo(new[] { "python", "-m", "http.server", "8080" }));
        Assert.That(compose.Services.Single().Entrypoint, Is.EqualTo(new[] { "/usr/local/bin/python" }));
    }
}
