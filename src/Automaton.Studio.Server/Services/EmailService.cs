using MailKit.Net.Smtp;
using MimeKit;
using Serilog;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Studio.Server.Services;

public class EmailService(ConfigurationService configurationService)
{
    private readonly Serilog.ILogger logger = Log.ForContext<EmailService>();
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ApplicationJson = "application/json";

    public async Task<bool> Send(EmailData emailDetails, CancellationToken cancellationToken)
    {
        try
        {
            var emailDetailsJson = JsonSerializer.Serialize(emailDetails);

            var client = new HttpClient();
            client.DefaultRequestHeaders.Add(ApiKeyHeader, configurationService.EmailConfig.EmailApiKey);

            var content = new StringContent(emailDetailsJson, Encoding.UTF8, ApplicationJson);
            var response = await client.PostAsync(configurationService.EmailConfig.EmailApiUrl, content, cancellationToken);

            EmailRender? emailRender = null;

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                emailRender = JsonSerializer.Deserialize<EmailRender>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            else
            {
                logger.Error("Failed to send email with details {0}. Server response: {1}", emailDetailsJson, response.Content);
                return false;
            }

            var mimeMessage = ComposeMimeMessage(emailDetails, emailRender);

            await SendEmail(mimeMessage, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {

            throw;
        }     
    }

    private static MimeMessage ComposeMimeMessage(EmailData emailDto, EmailRender renderedEmail)
    {
        var message = new MimeMessage();

        message.From.Add(new MailboxAddress(emailDto.From.Name, emailDto.From.Email));

        message.To.AddRange(emailDto.To.Select(x => new MailboxAddress(x.Name, x.Email)));

        message.Subject = renderedEmail.Subject;

        message.Body = new TextPart("html") { Text = renderedEmail.Html };

        return message;
    }

    private async Task SendEmail(MimeMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient();

        logger.Information($"Connect to email server {configurationService.EmailConfig.EmailHost} port {configurationService.EmailConfig.EmailPort} ssl {configurationService.EmailConfig.EmailUseSsl}");

        client.Connect(configurationService.EmailConfig.EmailHost, configurationService.EmailConfig.EmailPort, configurationService.EmailConfig.EmailUseSsl, cancellationToken);

        logger.Information("Authenticating to email server...");

        client.Authenticate(configurationService.EmailConfig.EmailAccount, configurationService.EmailConfig.EmailPassword, cancellationToken);

        logger.Information("Sending email...");

        await client.SendAsync(message);

        logger.Information("Email sent.");

        client.Disconnect(true, cancellationToken);
    }
}

public class EmailRender
{
    public required string Subject { get; set; }
    public required string Html { get; set; }
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
