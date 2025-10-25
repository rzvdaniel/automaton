using Automaton.Studio.Models;
using Serilog;

namespace Automaton.Studio.Services;

public class AppsService
{
    private readonly HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly ILogger logger;

    public AppsService(
        ConfigurationService configService,
        HttpClient httpClient
    )
    {
        logger = Log.ForContext<AppsService>();
        this.configService = configService;
        this.httpClient = httpClient;
    }

    public async Task<ICollection<AppListItem>> List()
    {
        try
        {
            var result = await httpClient.GetAsync(configService.AppsUrl);

            result.EnsureSuccessStatusCode();

            var apps = await result.Content.ReadAsAsync<ICollection<AppListItem>>();

            return apps;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when loading apps list.");
            throw;
        }
    }

    public async Task<AppModel> Load(Guid id)
    {
        var response = await httpClient.GetAsync($"{configService.AppsUrl}/{id}");

        response.EnsureSuccessStatusCode();

        var app = await response.Content.ReadAsAsync<AppModel>();

        return app;
    }

    public async Task<AppModel> Create(NewApp app)
    {
        var response = await httpClient.PostAsJsonAsync(configService.AppsUrl, app);

        response.EnsureSuccessStatusCode();

        var newApp = await response.Content.ReadAsAsync<AppModel>();

        return newApp;
    }

    public async Task Update(AppModel app)
    {
        var response = await httpClient.PutAsJsonAsync($"{configService.AppsUrl}/{app.Id}", app);

        response.EnsureSuccessStatusCode();
    }

    public async Task Delete(Guid id)
    {
        var response = await httpClient.DeleteAsync($"{configService.AppsUrl}/{id}");

        response.EnsureSuccessStatusCode();
    }

    public async Task Publish(Guid id, string publicUrl)
    {
        var request = new { PublicUrl = publicUrl };
        var response = await httpClient.PostAsJsonAsync($"{configService.AppsUrl}/{id}/publish", request);

        response.EnsureSuccessStatusCode();
    }

    public async Task Unpublish(Guid id)
    {
        var response = await httpClient.PostAsync($"{configService.AppsUrl}/{id}/unpublish", null);

        response.EnsureSuccessStatusCode();
    }
}
