using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Sinks.OpenTelemetry;

namespace Automaton.Core.Logs;

/// <summary>
/// Optional log shipping over OpenTelemetry (OTLP/HTTP). <c>Logging:Otlp:Endpoint</c> turns it on at a fixed address;
/// while it is empty, an installed app that receives logs can turn it on at runtime (<see cref="LogForwarding"/>).
/// The file and console sinks are never replaced.
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
    public static string? ResolveEndpoint(IConfiguration configuration) => ResolveEndpoint(configuration[EndpointKey]);

    /// <summary>Same rules as <see cref="ResolveEndpoint(IConfiguration)"/> for an address given directly.</summary>
    public static string? ResolveEndpoint(string? address)
    {
        var value = address?.Trim();
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
        // An address set by the admin always wins, even a wrong one: an app's address never replaces it.
        if (!string.IsNullOrWhiteSpace(configuration[EndpointKey]))
        {
            LogForwarding.Install(null);
            var endpoint = ResolveEndpoint(configuration);
            return endpoint is null ? logConfig : logConfig.WriteTo.OpenTelemetry(options => Configure(options, endpoint, serviceName));
        }

        // Nothing set: an installed app that receives logs is picked up while the process runs.
        var forwarding = new LogForwardingSink(address => CreateInner(address, serviceName));
        LogForwarding.Install(forwarding);
        return logConfig.WriteTo.Sink(forwarding);
    }

    private static ILogEventSink CreateInner(string endpoint, string serviceName)
        => new LoggerConfiguration().WriteTo.OpenTelemetry(options => Configure(options, endpoint, serviceName)).CreateLogger();

    private static void Configure(BatchedOpenTelemetrySinkOptions options, string endpoint, string serviceName)
    {
        options.Endpoint = endpoint;
        options.Protocol = OtlpProtocol.HttpProtobuf;
        options.ResourceAttributes = new Dictionary<string, object>
        {
            ["service.name"] = serviceName,
            ["host.name"] = Environment.MachineName
        };
    }
}
