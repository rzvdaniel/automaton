using Automaton.Studio.Server.Config;
using Common.Authentication;

namespace Automaton.Studio.Server.Services;

public class ConfigurationService
{
    private readonly IConfiguration configuration;
    private readonly AuthenticationSettings authenticationSettings = new();
    private readonly AppConfig appConfig = new();
    public readonly EmailConfig EmailConfig = new();
    private readonly UserPasswordConfig userPasswordConfig = new();

    public const string MsSqlDatabaseType = "MsSql";
    public const string MySqlDatabaseType = "MySql";

    public string DatabaseType => appConfig.DatabaseType;
    public int RefreshTokenLifetime => appConfig.RefreshTokenLifetime;
    public int RunnerRefreshTokenLifetime => appConfig.RunnerRefreshTokenLifetime;
    public string WebsiteUrl => appConfig.WebsiteUrl;
  
    public bool NoUserSignUp => !appConfig.UserSignUp;

    public bool RequireDigit => userPasswordConfig.RequireDigit;
    public bool RequireLowercase => userPasswordConfig.RequireLowercase;
    public bool RequireUppercase => userPasswordConfig.RequireUppercase;
    public bool RequireNonAlphanumeric => userPasswordConfig.RequireNonAlphanumeric;
    public int RequiredLength => userPasswordConfig.RequiredLength;

    public ConfigurationService(IConfiguration configuration)
    {
        this.configuration = configuration;
        
        this.configuration.GetSection(nameof(AuthenticationSettings)).Bind(authenticationSettings);
        this.configuration.GetSection(nameof(AppConfig)).Bind(appConfig);
        this.configuration.GetSection(nameof(UserPasswordConfig)).Bind(userPasswordConfig);
        this.configuration.GetSection(nameof(EmailConfig)).Bind(EmailConfig);
    }

    public bool IsDatabaseTypeMsSql()
    {
        return DatabaseType == MsSqlDatabaseType;
    }

    public bool IsDatabaseTypeMySql()
    {
        return DatabaseType == MySqlDatabaseType;
    }
}
