using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Sinks.OpenTelemetry;

namespace Automaton.Core.Logs;

/// <summary>
/// Optional log shipping over OpenTelemetry (OTLP/HTTP). Off unless <c>Logging:Otlp:Endpoint</c> is set;
/// the file and console sinks are never replaced.
/// </summary>
public static class OtlpLogShipping
{
    public const string EndpointKey = "Logging:Otlp:Endpoint";
    private const string LogsPath = "/insert/opentelemetry/v1/logs";

    /// <summary>
    /// Returns the full logs URL for the configured endpoint, or null when unset or invalid.
    /// A bare base address (for example http://victorialogs:9428) gets the VictoriaLogs OTLP path;
    /// an address with its own path is used as given.
    /// </summary>
    public static string? ResolveEndpoint(IConfiguration configuration)
    {
        var value = configuration[EndpointKey]?.Trim();
        if (string.IsNullOrEmpty(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return uri.AbsolutePath is "" or "/"
            ? uri.GetLeftPart(UriPartial.Authority) + LogsPath
            : uri.GetLeftPart(UriPartial.Path);
    }

    public static LoggerConfiguration AddOtlpIfConfigured(this LoggerConfiguration logConfig, IConfiguration configuration, string serviceName)
    {
        var endpoint = ResolveEndpoint(configuration);
        if (endpoint is null)
        {
            return logConfig;
        }

        return logConfig.WriteTo.OpenTelemetry(options =>
        {
            options.Endpoint = endpoint;
            options.Protocol = OtlpProtocol.HttpProtobuf;
            options.ResourceAttributes = new Dictionary<string, object>
            {
                ["service.name"] = serviceName,
                ["host.name"] = Environment.MachineName
            };
        });
    }
}
