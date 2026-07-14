using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/infrastructure-runner/installer")]
[AllowAnonymous]
public sealed class InfrastructureRunnerInstallerController : ControllerBase
{
    private const string ResourceSuffix = "Resources.install-infrastructure-runner.sh";

    [HttpGet("latest.sh")]
    [Produces("text/plain")]
    public IActionResult GetLatest()
        => Content(ReadInstaller(), "text/plain");

    [HttpGet("latest.sha256")]
    [Produces("text/plain")]
    public IActionResult GetLatestChecksum()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(ReadInstaller());
        return Content($"{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}  latest.sh\n", "text/plain");
    }

    private static string ReadInstaller()
    {
        var assembly = typeof(InfrastructureRunnerInstallerController).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName == null)
        {
            throw new InvalidOperationException("The Infrastructure Runner installer resource is not available.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The Infrastructure Runner installer resource could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
