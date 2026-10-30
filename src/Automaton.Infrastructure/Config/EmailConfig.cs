namespace Automaton.Api.Config;

public class EmailConfig
{
    public string EmailAccount { get; set; } = string.Empty;
    public string EmailPassword { get; set; } = string.Empty;
    public string EmailHost { get; set; } = string.Empty;
    public int EmailPort { get; set; }
    public bool EmailUseSsl { get; set; }
    public string SupportEmail { get; set; } = string.Empty;
    public string? FromEmail { get; set; }
    public string? AdminEmail { get; set; }
    public bool SendEmailsInDevelopment { get; set; }
}
