using Automaton.Core.Enums;
using Automaton.Runner.Console.Models;
using Serilog;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using static System.Console;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerEnrollmentWorkflow
{
    private readonly ConfigurationService configService;
    private readonly FileStorageService storageService;
    private readonly ApiKeyAuthenticationService apiKeyAuthService;
    private readonly HttpClient httpClient;
    private readonly ILogger logger;

    public RunnerEnrollmentWorkflow(
        ConfigurationService configService,
        FileStorageService storageService,
        ApiKeyAuthenticationService apiKeyAuthService,
        HttpClient httpClient,
        ILogger logger)
    {
        this.configService = configService;
        this.storageService = storageService;
        this.apiKeyAuthService = apiKeyAuthService;
        this.httpClient = httpClient;
        this.logger = logger;
    }
    internal async Task<bool> EnrollAsync()
    {
        var enrollmentToken = Environment.GetEnvironmentVariable("AUTOMATON_ENROLLMENT_TOKEN");
        if (string.IsNullOrWhiteSpace(enrollmentToken))
        {
            return false;
        }

        var runnerName = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_NAME");
        if (string.IsNullOrWhiteSpace(runnerName)) runnerName = $"machine-{Environment.MachineName}";

        var request = new RunnerEnrollmentExchangeRequest
        {
            EnrollmentToken = enrollmentToken,
            RunnerName = runnerName,
            RunnerVersion = Environment.Version.ToString(),
            MachineName = Environment.MachineName,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            ImageVersion = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_IMAGE")
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{configService.BaseUrl}/api/runner-enrollments/exchange",
                request);

            if (!response.IsSuccessStatusCode)
            {
                logger.Warning("Runner enrollment failed with status {StatusCode}", response.StatusCode);
                return false;
            }

            var enrollment = await response.Content.ReadFromJsonAsync<RunnerEnrollmentExchangeResponse>();
            if (enrollment == null || string.IsNullOrWhiteSpace(enrollment.ApiKey))
            {
                logger.Warning("Runner enrollment response did not include a durable credential");
                return false;
            }

            await apiKeyAuthService.SetEnrolledApiKeyAsync(enrollment.ApiKey);
            await storageService.SaveRunnerConfigAsync(enrollment.RunnerId.ToString(), enrollment.RunnerName);
            await storageService.SaveInstallationProfileAsync(new RunnerInstallationProfile
            {
                Purpose = enrollment.Purpose,
                ApplicationProfile = enrollment.ApplicationProfile,
                Capabilities = enrollment.Capabilities,
                InstallationId = enrollment.InstallationId
            });

            ApplyInstallationProfile(enrollment.Purpose, enrollment.Capabilities);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_ID", enrollment.RunnerId.ToString());
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_NAME", enrollment.RunnerName);
            Environment.SetEnvironmentVariable("AUTOMATON_ENROLLMENT_TOKEN", null);

            logger.Information(
                "Runner enrollment completed as runner {RunnerId}",
                enrollment.RunnerId);
            WriteLine($"✅ Runner enrolled successfully: {enrollment.RunnerName}");
            return true;
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Runner enrollment request failed");
            return false;
        }
    }

    public async Task ApplyInstallationProfileAsync()
    {
        var profile = await storageService.LoadInstallationProfileAsync();
        if (profile == null || string.IsNullOrWhiteSpace(profile.Purpose))
        {
            return;
        }

        var capabilities = RunnerCapabilities.Normalize(profile.Capabilities, profile.Purpose);
        if (!(profile.Capabilities ?? Array.Empty<string>())
            .SequenceEqual(capabilities, StringComparer.OrdinalIgnoreCase))
        {
            profile.Capabilities = capabilities;
            await storageService.SaveInstallationProfileAsync(profile);
        }

        ApplyInstallationProfile(profile.Purpose, capabilities);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE", profile.ApplicationProfile);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_INSTALLATION_ID", profile.InstallationId);

        var runnerId = storageService.GetRunnerId();
        Environment.SetEnvironmentVariable(
            "AUTOMATON_RUNNER_ID",
            Guid.TryParse(runnerId, out var parsedRunnerId) && parsedRunnerId != Guid.Empty
                ? parsedRunnerId.ToString()
                : null);
    }

    private static void ApplyInstallationProfile(string purpose, IReadOnlyList<string> capabilities)
    {
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", purpose);
        Environment.SetEnvironmentVariable(
            "AUTOMATON_RUNNER_CAPABILITIES",
            string.Join(',', RunnerCapabilities.Normalize(capabilities, purpose)));
    }

}
