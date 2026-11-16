using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Direct access to the Runner's runtime gateway (#1181). The installer publishes the gateway on the host's private
/// bridge address and gives the Runner a secret that only the reverse proxy adds. Without the secret, only the Runner's
/// own loopback tunnel may use the gateway, so no other container on the host can skip an app's sign-in.
/// </summary>
internal static class RunnerGatewayAccess
{
    public const string SecretHeader = "X-Automaton-Gateway-Secret";
    public const string SecretEnvironmentVariable = "AUTOMATON_GATEWAY_SECRET";
    public const string UrlEnvironmentVariable = "AUTOMATON_RUNTIME_GATEWAY_URL";

    public static string? Secret => Clean(Environment.GetEnvironmentVariable(SecretEnvironmentVariable));

    public static string? Url => Clean(Environment.GetEnvironmentVariable(UrlEnvironmentVariable));

    /// <summary>The URL and secret to report to the server, or null when the installer did not publish the gateway.</summary>
    public static (string Url, string Secret)? Published()
        => Url is { } url && Secret is { } secret ? (url, secret) : null;

    public static bool IsAllowed(HttpContext context) => IsAllowed(context, Secret);

    internal static bool IsAllowed(HttpContext context, string? secret)
    {
        // No secret configured (an older install): the gateway stays as it was.
        if (secret is null) return true;
        if (HasSecret(context, secret)) return true;
        // The Runner's own tunnel connects over loopback inside the container; Docker-published traffic never does.
        return context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
    }

    public static bool HasSecret(HttpContext context, string? secret)
    {
        if (secret is null) return false;
        var provided = context.Request.Headers[SecretHeader].ToString();
        return provided.Length > 0 && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(secret));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
