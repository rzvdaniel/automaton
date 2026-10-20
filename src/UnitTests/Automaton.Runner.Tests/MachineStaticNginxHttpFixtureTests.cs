using System.Diagnostics;
using System.Net;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class MachineStaticNginxHttpFixtureTests
{
    [Test]
    public async Task NginxServesIndexAssetsAndSpaFallbackWithoutRewritingTheReleasePrefixAgain()
    {
        if (!await DockerImageAvailableAsync())
            Assert.Ignore("The local Docker daemon or nginx:alpine image is unavailable.");

        var appId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        var root = Path.Combine(Path.GetTempPath(), "automaton-static-nginx", Guid.NewGuid().ToString("N"));
        var siteRoot = Path.Combine(root, "sites", appId.ToString("D"), releaseId.ToString("D"));
        var configPath = Path.Combine(root, "conf", "default.conf");
        var containerName = $"automaton-static-nginx-test-{Guid.NewGuid():N}";
        Directory.CreateDirectory(Path.Combine(siteRoot, "assets"));
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(Path.Combine(siteRoot, "index.html"), "spa entry");
        await File.WriteAllTextAsync(Path.Combine(siteRoot, "assets", "site.css"), "body { color: green; }");
        await File.WriteAllTextAsync(configPath,
            MachineStaticSitePublisher.BuildNginxConfiguration(new Dictionary<Guid, Guid> { [appId] = releaseId }, 8080));

        try
        {
            await RunDockerAsync([
                "run", "--detach", "--name", containerName,
                "--publish", "127.0.0.1::8080/tcp",
                "--mount", $"type=bind,source={Path.Combine(root, "sites")},target=/sites,readonly",
                "--mount", $"type=bind,source={configPath},target=/etc/nginx/conf.d/default.conf,readonly",
                "nginx:alpine"]);
            var portOutput = await RunDockerAsync(["port", containerName, "8080/tcp"]);
            var hostPort = int.Parse(portOutput.Trim().Split(':')[^1], System.Globalization.CultureInfo.InvariantCulture);
            var prefix = $"/{appId:D}/{releaseId:D}/";
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{hostPort}"),
                Timeout = TimeSpan.FromSeconds(5)
            };
            var rootResponse = await client.GetAsync(prefix);
            var assetResponse = await client.GetAsync(prefix + "assets/site.css");
            var fallbackResponse = await client.GetAsync(prefix + "client/route");

            Assert.Multiple(() =>
            {
                Assert.That(rootResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(assetResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(fallbackResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
            Assert.That(await rootResponse.Content.ReadAsStringAsync(), Is.EqualTo("spa entry"));
            Assert.That(await assetResponse.Content.ReadAsStringAsync(), Is.EqualTo("body { color: green; }"));
            Assert.That(await fallbackResponse.Content.ReadAsStringAsync(), Is.EqualTo("spa entry"));
        }
        finally
        {
            await RunDockerAsync(["rm", "--force", containerName], allowFailure: true);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<bool> DockerImageAvailableAsync()
    {
        try
        {
            await RunDockerAsync(["info"]);
            await RunDockerAsync(["image", "inspect", "nginx:alpine"]);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<string> RunDockerAsync(IReadOnlyList<string> arguments, bool allowFailure = false)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidOperationException("Could not start the Docker CLI.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await output;
        var stderr = await error;
        if (process.ExitCode != 0 && !allowFailure)
            throw new InvalidOperationException($"Docker command failed ({string.Join(' ', arguments)}): {stderr}");
        return stdout;
    }
}
