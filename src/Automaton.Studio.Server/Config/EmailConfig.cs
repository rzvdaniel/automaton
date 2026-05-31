namespace Automaton.Studio.Server.Config;

public class EmailConfig
{
    public string EmailAccount { get; set; }
    public string EmailPassword { get; set; }
    public string EmailHost { get; set; }
    public int EmailPort { get; set; }
    public bool EmailUseSsl { get; set; }
    public string SupportEmail { get; set; }
    public string? FromEmail { get; set; }
    public string? AdminEmail { get; set; }
    public bool SendEmailsInDevelopment { get; set; }
}
