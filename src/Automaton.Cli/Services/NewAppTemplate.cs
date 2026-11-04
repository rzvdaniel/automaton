using Automaton.Core.Models;

namespace Automaton.Cli.Services;

internal sealed record NewAppTemplate(ApplicationContract Contract, IReadOnlyDictionary<string, string> Files)
{
    internal static NewAppTemplate Create(string name) => name switch
    {
        "node" => new(Runtime("node:22-slim", "node", "server.mjs"), new Dictionary<string, string>
        {
            ["server.mjs"] = """
                import { createServer } from 'node:http';

                createServer((request, response) => {
                  response.setHeader('Content-Type', 'text/plain');
                  response.end(request.url === '/health' ? 'ok' : 'Hello from Automaton!');
                }).listen(Number(process.env.AUTOMATON_PORT), '0.0.0.0');
                """
        }),
        "python" => new(Runtime("python:3.12-slim", "python", "server.py"), new Dictionary<string, string>
        {
            ["server.py"] = """
                import os
                from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

                class Handler(BaseHTTPRequestHandler):
                    def do_GET(self):
                        body = b"ok" if self.path == "/health" else b"Hello from Automaton!"
                        self.send_response(200)
                        self.send_header("Content-Type", "text/plain")
                        self.send_header("Content-Length", str(len(body)))
                        self.end_headers()
                        self.wfile.write(body)

                ThreadingHTTPServer(("0.0.0.0", int(os.environ["AUTOMATON_PORT"])), Handler).serve_forever()
                """
        }),
        "dotnet" => DotNet(),
        "static" => Static(),
        _ => throw new InvalidOperationException("Choose a template: node, python, dotnet, or static.")
    };

    private static ApplicationContract Runtime(string image, string executable, string entryFile) => new()
    {
        Runtime = new ApplicationRuntimeSpec { Image = image, Command = Command(executable, entryFile) },
        Health = new ApplicationHealthSpec { Path = "/health" }
    };

    private static NewAppTemplate DotNet()
    {
        var contract = Runtime("mcr.microsoft.com/dotnet/aspnet:10.0", "dotnet", "App.dll");
        contract.Runtime.Command!.WorkingDirectory = "dist";
        contract.Build = new ApplicationBuildSpec
        {
            Image = "mcr.microsoft.com/dotnet/sdk:10.0",
            Command = Command("dotnet", "publish", "App.csproj", "-c", "Release", "-o", "dist"),
            OutputDirectory = "dist"
        };
        return new(contract, new Dictionary<string, string>
        {
            ["App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                </Project>
                """,
            ["Program.cs"] = """
                var port = int.Parse(Environment.GetEnvironmentVariable("AUTOMATON_PORT")!);
                var builder = WebApplication.CreateBuilder(args);
                builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
                var app = builder.Build();
                app.MapGet("/", () => "Hello from Automaton!");
                app.MapGet("/health", () => "ok");
                app.Run();
                """
        });
    }

    private static NewAppTemplate Static() => new(new ApplicationContract
    {
        Build = new ApplicationBuildSpec
        {
            Image = "alpine:3.22",
            Command = Command("sh", "-c", "mkdir -p dist && cp index.html health dist/"),
            OutputDirectory = "dist"
        },
        Health = new ApplicationHealthSpec { Path = "/health" }
    }, new Dictionary<string, string>
    {
        ["index.html"] = """
            <!doctype html>
            <html lang="en">
            <meta charset="utf-8">
            <title>Hello from Automaton</title>
            <h1>Hello from Automaton!</h1>
            </html>
            """,
        ["health"] = "ok"
    });

    private static ApplicationCommandSpec Command(string executable, params string[] arguments)
        => new() { Executable = executable, Arguments = [.. arguments] };

    internal void Write(string directory, Action saveConfig)
    {
        foreach (var name in Files.Keys.Prepend("automaton.json"))
        {
            if (Path.Exists(Path.Combine(directory, name)))
                throw new IOException($"The template would overwrite '{name}'. Choose an empty directory or omit --template.");
        }

        var created = new List<string>();
        try
        {
            foreach (var (name, contents) in Files)
            {
                var path = Path.Combine(directory, name);
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                created.Add(path);
                using var writer = new StreamWriter(file);
                writer.WriteLine(contents);
            }
            saveConfig();
        }
        catch
        {
            foreach (var path in created) File.Delete(path);
            throw;
        }
    }
}
