using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAutomatonInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AppConfig>(configuration.GetSection("AppConfig"));
        services.Configure<EmailConfig>(configuration.GetSection("EmailConfig"));

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseMySql(
                configuration.GetConnectionString("MySqlConnection"),
                new MySqlServerVersion(new Version(8, 0, 36)),
                mySqlOptions => mySqlOptions.MigrationsAssembly(typeof(DependencyInjection).Assembly.GetName().Name)));

        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<EmailConfirmationLinkBuilder>();

        return services;
    }
}
