using Automaton.Studio.Models;
using Serilog;
using System.Text.RegularExpressions;

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

    public async Task<ICollection<AppListItem>> List(Guid? projectId = null)
    {
        try
        {
            var url = configService.AppsUrl;
            if (projectId.HasValue)
            {
                url += $"?projectId={projectId.Value}";
            }

            var result = await httpClient.GetAsync(url);

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

    public async Task<AppModel> LoadBySlug(string slug)
    {
        var response = await httpClient.GetAsync($"{configService.AppsUrl}/slug/{slug}");

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

    public async Task<string> PublishAsync(Guid id, string publicUrl, bool requiresAuthentication = true, Guid? executionRunnerId = null)
    {
        var request = new { PublicUrl = publicUrl, RequiresAuthentication = requiresAuthentication, ExecutionRunnerId = executionRunnerId };
        var response = await httpClient.PostAsJsonAsync($"{configService.AppsUrl}/{id}/publish", request);

        response.EnsureSuccessStatusCode();

        return publicUrl;
    }

    public async Task UnpublishAsync(Guid id)
    {
        var response = await httpClient.PostAsync($"{configService.AppsUrl}/{id}/unpublish", null);

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Generate a URL slug from display name (client-side preview)
    /// </summary>
    public static string GenerateSlugPreview(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "app";

        // Convert to lowercase and replace spaces/underscores with hyphens
        var slug = displayName.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('_', '-');

        // Remove special characters (keep only alphanumeric and hyphens)
        slug = Regex.Replace(slug, @"[^a-z0-9\-]", "");

        // Remove multiple consecutive hyphens
        slug = Regex.Replace(slug, @"\-+", "-");

        // Trim hyphens from start and end
        slug = slug.Trim('-');

        // Ensure we have something
        if (string.IsNullOrEmpty(slug))
            slug = "app";

        return slug;
    }

    // Keep old methods for backward compatibility
    public async Task Publish(Guid id, string publicUrl)
    {
        await PublishAsync(id, publicUrl);
    }

    public async Task Unpublish(Guid id)
    {
        await UnpublishAsync(id);
    }

    // GitHub integration helpers
    public async Task<T?> GetFromApiAsync<T>(string endpoint)
    {
        var response = await httpClient.GetAsync($"{configService.BaseUrl}/api/{endpoint}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsAsync<T>();
    }

    public async Task<T?> PostToApiAsync<T>(string endpoint, object? data)
    {
        var response = await httpClient.PostAsJsonAsync($"{configService.BaseUrl}/api/{endpoint}", data);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsAsync<T>();
    }

    public async Task PutToApiAsync(string endpoint, object data)
    {
        var response = await httpClient.PutAsJsonAsync($"{configService.BaseUrl}/api/{endpoint}", data);
        response.EnsureSuccessStatusCode();
    }
}
