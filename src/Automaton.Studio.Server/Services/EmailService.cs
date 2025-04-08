using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Studio.Server.Services;

public class EmailService(ConfigurationService configurationService)
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ApplicationJson = "application/json";
    private readonly Serilog.ILogger logger = Serilog.Log.ForContext<FlowsService>();

    public async Task<bool> Send(EmailData emailDetails, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(emailDetails);
            var content = new StringContent(json, Encoding.UTF8, ApplicationJson);

            var client = new HttpClient();
            client.DefaultRequestHeaders.Add(ApiKeyHeader, configurationService.EmailConfig.EmailApiKey);

            var response = await client.PostAsync(configurationService.EmailConfig.EmailApiUrl, content, cancellationToken);

            return response.IsSuccessStatusCode;

        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occured when sending the email {0}", JsonSerializer.Serialize(emailDetails));
            throw;
        }
    }
}

public class EmailData
{
    public required string Template { get; set; }
    public Dictionary<string, string> Variables { get; set; } = [];
    public required EmailAccount From { get; set; }
    public required IEnumerable<EmailAccount> To { get; set; } = [];
}

public class EmailAccount
{
    [JsonInclude]
    public required string Email { get; set; }
    [JsonInclude]
    public string? Name { get; set; }
}
