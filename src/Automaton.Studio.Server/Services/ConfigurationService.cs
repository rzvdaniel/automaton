using Automaton.Studio.Server.Config;
using Common.Authentication;

namespace Automaton.Studio.Server.Services;

public class ConfigurationService
{
    private readonly IConfiguration configuration;
    private readonly AuthenticationSettings authenticationSettings = new();
    public readonly AppConfig AppConfig = new();
    public readonly EmailConfig EmailConfig = new();
    private readonly UserPasswordConfig userPasswordConfig = new();
    private readonly UserLockoutConfig userLockoutConfig = new();

    public const string MsSqlDatabaseType = "MsSql";
    public const string MySqlDatabaseType = "MySql";

    public string DatabaseType => AppConfig.DatabaseType;
    public int RefreshTokenLifetime => AppConfig.RefreshTokenLifetime;
    public string WebsiteUrl => AppConfig.WebsiteUrl;
    public bool NoUserSignUp => !AppConfig.UserSignUp;

    public bool RequireDigit => userPasswordConfig.RequireDigit;
    public bool RequireLowercase => userPasswordConfig.RequireLowercase;
    public bool RequireUppercase => userPasswordConfig.RequireUppercase;
    public bool RequireNonAlphanumeric => userPasswordConfig.RequireNonAlphanumeric;
    public int RequiredLength => userPasswordConfig.RequiredLength;
    public bool LockoutAllowedForNewUsers => userLockoutConfig.AllowedForNewUsers;
    public int MaxFailedAccessAttempts => userLockoutConfig.MaxFailedAccessAttempts;
    public int DefaultLockoutTimeSpanMinutes => userLockoutConfig.DefaultLockoutTimeSpanMinutes;
    public CloudflareTurnstileOptions CloudflareTurnstileOptions { get; } = new();

    public ConfigurationService(IConfiguration configuration)
    {
        this.configuration = configuration;
        
        this.configuration.GetSection(nameof(AuthenticationSettings)).Bind(authenticationSettings);
        this.configuration.GetSection(nameof(AppConfig)).Bind(AppConfig);
        this.configuration.GetSection(nameof(UserPasswordConfig)).Bind(userPasswordConfig);
        this.configuration.GetSection(nameof(UserLockoutConfig)).Bind(userLockoutConfig);
        this.configuration.GetSection(nameof(EmailConfig)).Bind(EmailConfig);
        this.configuration.GetSection(CloudflareTurnstileOptions.SectionName).Bind(CloudflareTurnstileOptions);
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
