using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Models;
using Serilog;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using static System.Console;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerRegistrationWorkflow
{
    private readonly ConfigurationService configService;
    private readonly FileStorageService storageService;
    private readonly HttpClient httpClient;
    private readonly ILogger logger;

    public RunnerRegistrationWorkflow(
        ConfigurationService configService,
        FileStorageService storageService,
        HttpClient httpClient,
        ILogger logger)
    {
        this.configService = configService;
        this.storageService = storageService;
        this.httpClient = httpClient;
        this.logger = logger;
    }
    public async Task<bool> RegisterRunnerAsync(string runnerName, bool ignoreStoredRunnerId = false)
    {
        try
        {
            var identity = await ResolveRunnerRegistrationIdentityAsync(runnerName, ignoreStoredRunnerId);
            if (identity.Recovered) return true;
            var runnerData = await CreateRunnerRegistrationDataAsync(identity.Id, runnerName);
            using var response = await SendRunnerRegistrationAsync(identity, runnerName, runnerData);
            return await HandleRunnerRegistrationResponseAsync(identity, runnerName, response);
        }
        catch (Exception ex)
        {
            // Log technical details to file logs only
            logger.Error(ex, "Error during runner registration. Error: {ErrorType}", ex.GetType().Name);

            // Show friendly message to user
            WriteLine($"? Registration error: {ex.Message}");
            WriteLine("Check the log files for detailed technical information.");
            return false;
        }
    }

    private async Task<RunnerRegistrationIdentity> ResolveRunnerRegistrationIdentityAsync(
        string runnerName,
        bool ignoreStoredRunnerId)
    {
        var storedId = ignoreStoredRunnerId ? null : storageService.GetRunnerId();
        if (!string.IsNullOrEmpty(storedId) && Guid.TryParse(storedId, out var parsedId))
        {
            WriteLine($"?? Found existing runner ID: {parsedId}");
            WriteLine($"?? Updating runner name to: {runnerName}");
            return new RunnerRegistrationIdentity(parsedId, true, false);
        }
        return await FindRunnerByNameAsync(runnerName);
    }

    private async Task<RunnerRegistrationIdentity> FindRunnerByNameAsync(string runnerName)
    {
        WriteLine($"?? Checking if runner '{runnerName}' already exists on server...");
        try
        {
            var response = await httpClient.GetAsync(
                $"{configService.BaseUrl}/api/runners/by-name/{Uri.EscapeDataString(runnerName)}");
            if (!response.IsSuccessStatusCode) return CreateNewRunnerIdentity();
            var details = await response.Content.ReadAsAsync<RunnerDetails>();
            if (details is null) return CreateNewRunnerIdentity();
            await RecoverRunnerIdentityAsync(details.Id, runnerName);
            return new RunnerRegistrationIdentity(details.Id, true, true);
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Could not check for existing runner by name, will create new: {ErrorMessage}", ex.Message);
            return CreateNewRunnerIdentity();
        }
    }

    private async Task RecoverRunnerIdentityAsync(Guid runnerId, string runnerName)
    {
        WriteLine($"?? Found existing runner on server with ID: {runnerId}");
        WriteLine("?? Will reuse this runner locally");
        await storageService.SaveRunnerConfigAsync(runnerId.ToString(), runnerName);
        logger.Warning("Runner recovery successful: {RunnerName} (ID: {RunnerId})", runnerName, runnerId);
        WriteLine($"? Runner '{runnerName}' recovered successfully!");
    }

    private static RunnerRegistrationIdentity CreateNewRunnerIdentity()
    {
        var identity = new RunnerRegistrationIdentity(Guid.NewGuid(), false, false);
        WriteLine($"?? Creating new runner with ID: {identity.Id}");
        return identity;
    }

    private async Task<object> CreateRunnerRegistrationDataAsync(Guid runnerId, string runnerName)
    {
        var purpose = RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"));
        return new
        {
            Id = runnerId,
            Name = runnerName,
            Version = Environment.Version.ToString(),
            MachineName = Environment.MachineName,
            Purpose = purpose,
            Capabilities = RunnerCapabilities.Normalize(
                Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES")?
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                purpose),
            HostedRunnerId = Guid.TryParse(Environment.GetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID"), out var hostedRunnerId)
                ? (Guid?)hostedRunnerId
                : null,
            InstallationId = await storageService.GetOrCreateInstallationIdAsync()
        };
    }

    private async Task<HttpResponseMessage> SendRunnerRegistrationAsync(
        RunnerRegistrationIdentity identity,
        string runnerName,
        object runnerData)
    {
        var route = $"{configService.BaseUrl}/{configService.RunnersUrl}";
        var json = JsonSerializer.Serialize(runnerData);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = identity.IsUpdate ? $"{route}/{identity.Id}" : route;
        var operation = identity.IsUpdate ? "update" : "registration";
        WriteLine($"?? Making {operation} request to: {url}");
        WriteLine($"?? Runner data: ID={identity.Id}, Name={runnerName}");
        WriteLine($"?? Request headers: X-API-Key = {(httpClient.DefaultRequestHeaders.Contains("X-API-Key") ? "Present" : "Missing")}");
        return identity.IsUpdate
            ? await httpClient.PutAsync(url, content)
            : await httpClient.PostAsync(url, content);
    }

    private async Task<bool> HandleRunnerRegistrationResponseAsync(
        RunnerRegistrationIdentity identity,
        string runnerName,
        HttpResponseMessage response)
    {
        WriteLine($"?? Server response: {response.StatusCode}");
        if (response.IsSuccessStatusCode)
        {
            await storageService.SaveRunnerConfigAsync(identity.Id.ToString(), runnerName);
            var operation = identity.IsUpdate ? "updated" : "registered";
            logger.Debug("Runner {RunnerName} {Action} with ID {RunnerId}", runnerName, operation, identity.Id);
            WriteLine($"? Runner {operation} successfully!");
            return true;
        }
        var retryResult = await RetryWithoutRejectedIdentityAsync(identity, runnerName, response);
        if (retryResult is not null) return retryResult.Value;
        await ReportRegistrationFailureAsync(identity, response);
        return false;
    }

    private async Task<bool?> RetryWithoutRejectedIdentityAsync(
        RunnerRegistrationIdentity identity,
        string runnerName,
        HttpResponseMessage response)
    {
        if (!identity.IsUpdate || response.StatusCode is not (
            System.Net.HttpStatusCode.NotFound or
            System.Net.HttpStatusCode.Forbidden or
            System.Net.HttpStatusCode.Unauthorized))
            return null;
        logger.Warning(
            "Stored runner ID {RunnerId} for {RunnerName} was rejected with status {StatusCode}. Retrying registration without the stale local ID.",
            identity.Id, runnerName, response.StatusCode);
        WriteLine($"?? Stored runner ID was rejected by the server ({response.StatusCode}). Recovering runner identity...");
        return await RegisterRunnerAsync(runnerName, ignoreStoredRunnerId: true);
    }

    private async Task ReportRegistrationFailureAsync(
        RunnerRegistrationIdentity identity,
        HttpResponseMessage response)
    {
        var action = identity.IsUpdate ? "update" : "registration";
        var error = await response.Content.ReadAsStringAsync();
        logger.Warning("Runner {Action} failed: {StatusCode} - {Error}", action, response.StatusCode, error);
        WriteLine($"? {(identity.IsUpdate ? "Update" : "Registration")} failed: {response.StatusCode}");
        if (!string.IsNullOrEmpty(error)) WriteLine($"?? Server response body: {error}");
        ReportRegistrationFailureGuidance(response.StatusCode);
    }

    private static void ReportRegistrationFailureGuidance(System.Net.HttpStatusCode statusCode)
    {
        if (statusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            WriteLine();
            WriteLine("?? Authentication troubleshooting:");
            WriteLine("1. Verify the API key is valid (run 'auth' command with a valid key first)");
            WriteLine("2. Check if the API key has expired in Automaton Studio");
            WriteLine("3. Ensure the credential was issued by the Runner enrollment or provisioning workflow");
            WriteLine("4. Verify the Automaton Studio server is running and accessible");
        }
        else if (statusCode == System.Net.HttpStatusCode.Forbidden)
        {
            WriteLine("The API key is valid but doesn't have permission for runner registration.");
            WriteLine("?? Re-enroll or reprovision the Runner from Automaton Studio to obtain a valid system credential.");
        }
    }

    private sealed record RunnerRegistrationIdentity(Guid Id, bool IsUpdate, bool Recovered);

}
