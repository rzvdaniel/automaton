using Automaton.Studio.Domain;
using Automaton.Studio.Models;

namespace Automaton.Studio.Services;

public class CustomStepsService
{
    private HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly ILogger logger;

    public CustomStepsService
    (
        ConfigurationService configService,
        HttpClient httpClient
    )
    {
        logger = Log.ForContext<CustomStepsService>();
        this.configService = configService;
        this.httpClient = httpClient;
    }

    public async Task<ICollection<CustomStep>> List(Guid? projectId = null)
    {
        try
        {
            var url = configService.CustomStepsUrl;
            if (projectId.HasValue)
            {
                url += $"?projectId={projectId.Value}";
            }

            var result = await httpClient.GetAsync(url);

            result.EnsureSuccessStatusCode();

            var steps = await result.Content.ReadAsAsync<ICollection<CustomStep>>();

            return steps;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when loading custom steps list.");
            throw;
        }   
    }

    public async Task<CustomStep> Load(Guid id)
    {
        var response = await httpClient.GetAsync($"{configService.CustomStepsUrl}/{id}");

        response.EnsureSuccessStatusCode();

        var customStep = await response.Content.ReadAsAsync<CustomStep>();

        return customStep;
    }

    public async Task<CustomStep> Create(NewCustomStep customStep)
    {
        var response = await httpClient.PostAsJsonAsync(configService.CustomStepsUrl, customStep);

        response.EnsureSuccessStatusCode();

        var newCustomStep = await response.Content.ReadAsAsync<CustomStep>();

        return newCustomStep;
    }

    public async Task Update(CustomStep customStep)
    {
        var response = await httpClient.PutAsJsonAsync($"{configService.CustomStepsUrl}/{customStep.Id}", customStep);

        response.EnsureSuccessStatusCode();
    }

    public async Task Delete(Guid id)
    {
        var response = await httpClient.DeleteAsync($"{configService.CustomStepsUrl}/{id}");

        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> Exists(string name)
    {
        var result = await httpClient.GetAsync($"{configService.CustomStepsUrl}/exists/{name}");

        var exists = result.StatusCode != System.Net.HttpStatusCode.NotFound;

        return exists;
    }

    /// <summary>
    /// Execute a custom step on specified runners via server API
    /// </summary>
    public async Task<IEnumerable<RunnerCustomStepResult>> Execute(Guid customStepId, IEnumerable<Guid> runnerIds, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.Information("Requesting execution of custom step {CustomStepId} on runners", customStepId);

            var requestBody = new
            {
                runnerIds = runnerIds.ToArray()
            };

            var response = await httpClient.PostAsJsonAsync(
                $"{configService.CustomStepsUrl}/{customStepId}/execute", 
                requestBody,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var results = await response.Content.ReadAsAsync<IEnumerable<RunnerCustomStepResult>>(cancellationToken);

            return results;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error executing custom step {CustomStepId} on runners", customStepId);
            throw;
        }
    }
}
