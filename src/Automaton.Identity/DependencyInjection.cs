using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Identity.Abstractions;
using Automaton.Identity.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Identity;

public static class DependencyInjection
{
    public static IServiceCollection AddAutomatonIdentity(this IServiceCollection services)
    {
        services.AddScoped<IUserLookupService, UserLookupService>();
        services.AddScoped<IEmailConfirmationTokenService, EmailConfirmationTokenService>();
        services.AddScoped<UserRegistrationNotificationService>();

        return services;
    }

    public static IServiceCollection AddAutomatonWorkerIdentity(this IServiceCollection services)
    {
        services.AddDataProtection();

        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        return services.AddAutomatonIdentity();
    }
}
