using Automaton.Client.Auth.Models;
using Microsoft.Extensions.Configuration;

namespace Automaton.Studio.Pages.Authentication.Config;

public class ConfigurationService
{
    private readonly IConfiguration configuration;
    private readonly ClientAuthConfig authenticationConfig = new();

    public bool UserSignUp => authenticationConfig.UserSignUp;
    public bool NoUserSignUp => !UserSignUp;
    public string LoginUserUrl => authenticationConfig.LoginUserUrl;

    public ConfigurationService(IConfiguration configuration)
    {
        this.configuration = configuration;

        this.configuration.GetSection(nameof(ClientAuthConfig)).Bind(authenticationConfig);
    }
}
