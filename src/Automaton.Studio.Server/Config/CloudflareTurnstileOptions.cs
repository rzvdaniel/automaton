namespace Automaton.Studio.Server.Config;

public class CloudflareTurnstileOptions
{
    public const string SectionName = "CloudflareTurnstile";

    public bool Enabled { get; set; }

    public bool AllowBypassWhenNotConfiguredInDevelopment { get; set; } = true;

    public string SiteKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;
}
