namespace Automaton.Api.Config;

public class AppConfig
{
    public string DatabaseType { get; set; } = string.Empty;
    public bool UserSignUp { get; set; }
    public int RefreshTokenLifetime { get; set; }
    public string WebsiteUrl { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = string.Empty;
    public string AiApiEncryptionKey { get; set; } = string.Empty;
}
