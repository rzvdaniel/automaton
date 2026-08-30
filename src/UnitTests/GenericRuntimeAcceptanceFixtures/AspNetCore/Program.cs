using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

var port = int.Parse(Environment.GetEnvironmentVariable("AUTOMATON_PORT")!);
var version = Environment.GetEnvironmentVariable("ACCEPTANCE_VERSION") ?? "unknown";
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

var app = builder.Build();
app.MapGet("/health", () => Results.Text("ready", "text/plain"));
app.MapGet("/hello", (HttpRequest request) => Results.Text(
    $"aspnet:{version}:{request.Path}{request.QueryString}:" +
    $"{Environment.GetEnvironmentVariable("AUTOMATON_APP_DATA_DIR")}",
    "text/plain"));
app.Run();
