using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Automaton.Runner.Console.Connection;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections.Client;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Automaton.Runner.Console.Services;

internal static class HubConnectionFactory
{
    private const string RunnerIdHeader = "RunnerId";
    private const string RunnerNameHeader = "RunnerName";
    private const string RunnerVersionHeader = "RunnerVersion";
    private const string RunnerMachineNameHeader = "RunnerMachineName";
    private const string RunnerPurposeHeader = "RunnerPurpose";
    private const string RunnerCapabilitiesHeader = "RunnerCapabilities";
    private const string RunnerHostIdHeader = "RunnerHostId";
    private const string HostedRunnerIdHeader = "HostedRunnerId";
    private const string RunnerImageDigestHeader = "RunnerImageDigest";
    private const string RunnerSpecificationVersionHeader = "RunnerSpecificationVersion";
    private const string RunnerApiCompatibilityVersionHeader = "RunnerApiCompatibilityVersion";
    private const string RunnerOperatingSystemHeader = "RunnerOperatingSystem";
    private const string RunnerArchitectureHeader = "RunnerArchitecture";

    public static HubConnection Create(ConfigurationService configService, string apiKey, string hubUrl)
        => new HubConnectionBuilder()
            .WithUrl(hubUrl, options => ConfigureHeaders(options, configService, apiKey))
            .WithAutomaticReconnect(new JitteredReconnectPolicy())
            .Build();

    private static void ConfigureHeaders(HttpConnectionOptions options, ConfigurationService configService, string apiKey)
    {
        options.HttpMessageHandlerFactory = inner => new HubRateLimitHandler(inner);
        options.Headers.Add("X-API-Key", apiKey);
        options.Headers.Add(RunnerIdHeader, configService.RunnerId);
        options.Headers.Add(RunnerNameHeader, configService.ApplicationName);
        options.Headers.Add(RunnerVersionHeader, GetRunnerReleaseVersion());
        options.Headers.Add(RunnerMachineNameHeader, Environment.MachineName);
        options.Headers.Add(RunnerImageDigestHeader, Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_IMAGE_DIGEST") ?? string.Empty);
        options.Headers.Add(RunnerSpecificationVersionHeader, Environment.GetEnvironmentVariable("AUTOMATON_CONTAINER_SPECIFICATION_VERSION") ?? string.Empty);
        options.Headers.Add(RunnerApiCompatibilityVersionHeader, HubService.CurrentRunnerApiCompatibilityVersion);
        options.Headers.Add(RunnerOperatingSystemHeader, RuntimeInformation.OSDescription);
        options.Headers.Add(RunnerArchitectureHeader, RuntimeInformation.OSArchitecture.ToString());
        AddRunnerCapabilities(options);
        AddOptionalHeader(options, RunnerHostIdHeader, "AUTOMATON_RUNNER_HOST_ID");
        AddOptionalHeader(options, HostedRunnerIdHeader, "AUTOMATON_HOSTED_RUNNER_ID");
    }

    private static void AddRunnerCapabilities(HttpConnectionOptions options)
    {
        var purpose = RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"));
        options.Headers.Add(RunnerPurposeHeader, purpose);
        var configuredCapabilities = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES")?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        options.Headers.Add(
            RunnerCapabilitiesHeader,
            string.Join(',', RunnerCapabilities.Normalize(configuredCapabilities, purpose)));
    }

    private static void AddOptionalHeader(HttpConnectionOptions options, string headerName, string environmentVariable)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
            options.Headers.Add(headerName, value);
    }

    private static string GetRunnerReleaseVersion()
        => typeof(HubService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(HubService).Assembly.GetName().Version?.ToString()
            ?? "unknown";
}
