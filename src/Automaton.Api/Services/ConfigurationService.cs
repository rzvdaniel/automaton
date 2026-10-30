using Automaton.Api.Authentication;
using Automaton.Api.Config;

namespace Automaton.Api.Services;

public class ConfigurationService
{
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
    public const string RegistrationPausedMessage = "Public registration is temporarily paused while we prepare Automaton for wider access.";

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
        configuration.GetSection(nameof(AuthenticationSettings)).Bind(authenticationSettings);
        configuration.GetSection(nameof(AppConfig)).Bind(AppConfig);
        configuration.GetSection(nameof(UserPasswordConfig)).Bind(userPasswordConfig);
        configuration.GetSection(nameof(UserLockoutConfig)).Bind(userLockoutConfig);
        configuration.GetSection(nameof(EmailConfig)).Bind(EmailConfig);
        configuration.GetSection(CloudflareTurnstileOptions.SectionName).Bind(CloudflareTurnstileOptions);
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
