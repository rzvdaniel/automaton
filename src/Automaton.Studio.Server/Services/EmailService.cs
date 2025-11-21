using Automaton.Studio.Server.Models;
using System.Text;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

public class EmailService(ConfigurationService configurationService, ILogger<EmailService> logger) : IEmailService
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ApplicationJson = "application/json";
    private readonly Serilog.ILogger logger = Serilog.Log.ForContext<EmailService>();

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
