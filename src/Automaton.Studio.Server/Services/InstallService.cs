using Automaton.Studio.Server.Core.Commands;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

public class InstallService
(
    UserManager<ApplicationUser> userManager,
    IEmailService emailService,
    ConfigurationService configurationService,
    ApplicationDbContext dbContext,
    ILogger<InstallService> logger,
    UsersService usersService
)
{    
    public async Task Install(InstallCommand systemAdminConfig, CancellationToken cancellationToken)
    {
        try
        {
            await AddRoles(cancellationToken);

            await AddSystemAdministrator(systemAdminConfig);

            await SendRegistrationEmail(systemAdminConfig, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occured during installation, rolling back.");

            await RollBackSystemAdministrator(systemAdminConfig);

            throw;
        }   
    }

    public async Task<bool> InstallationComplete()
    {
        return await dbContext.Users.AnyAsync(); 
    }

    private async Task SendRegistrationEmail(InstallCommand installCommand, CancellationToken cancellationToken)
    {
        var userDetails = new EmailData
        {
            Template = "AutomatonRegisterUser",
            From = new EmailAccount { Email = configurationService.EmailConfig.EmailSupport },
            To = [new EmailAccount { Email = installCommand.Email, Name = installCommand.Name }],
            Variables =
            {
                { "FullName", installCommand.Name },
                { "WebsiteUrl", configurationService.WebsiteUrl },
                { "SupportEmail", configurationService.EmailConfig.EmailSupport }
            }
        };

        await emailService.Send(userDetails, cancellationToken);
    }

    public async Task AddSystemAdministrator(InstallCommand systemAdminConfig)
    {
        var result = await CreateUser(systemAdminConfig);

        if (!result.Succeeded)
            throw new Exception("Could not create system administrator");

        var admin = await GetUserByEmailOrUserName(systemAdminConfig.Email) ??
            throw new Exception("Could not find system administrator");

        await AddUserToRolesAsync(admin.Id, [UserRole.SystemAdministrator, UserRole.Administrator, UserRole.User]);
    }

    private async Task RollBackSystemAdministrator(InstallCommand systemAdminConfig)
    {
        var admin = await GetUserByEmailOrUserName(systemAdminConfig.Email) ??
            throw new Exception("Could not find system administrator"); ;

        var result = await userManager.DeleteAsync(admin);

        if (!result.Succeeded)
            throw new Exception("Could not delete system administrator");
    }

    private async Task<IdentityResult> CreateUser(InstallCommand systemAdminConfig)
    {
        if (userManager.Users.Any(u => u.Email == systemAdminConfig.Email))
        {
            throw new Exception($"User with email address {systemAdminConfig.Email} already registered");
        }

        var publicUsername = await usersService.GenerateUniquePublicUsernameAsync(systemAdminConfig.Name);
        var newUser = new ApplicationUser
        {
            UserName = systemAdminConfig.Email,
            Email = systemAdminConfig.Email,
            Name = systemAdminConfig.Name,
            PublicUsername = publicUsername
        };

        var result = await userManager.CreateAsync(newUser, systemAdminConfig.Password);

        return result;
    }

    private async Task<ApplicationUser?> GetUserByEmailOrUserName(string emailOrUserName)
    {
        var user = await userManager.FindByEmailAsync(emailOrUserName) ??
            await userManager.FindByNameAsync(emailOrUserName);

        return user;
    }

    private async Task<ApplicationUser?> GetUserById(string userId)
    {
        var result = await userManager.FindByIdAsync(userId);

        return result;
    }

    private async Task AddUserToRolesAsync(Guid userId, IEnumerable<string> roles)
    {
        var user = await GetUserById(userId) ??
            throw new Exception($"User with id {userId} not found");

        var result = await userManager.AddToRolesAsync(user, roles);
        if (!result.Succeeded)
        {
            throw new Exception("Error in adding user to roles!");
        }
    }

    private async Task<ApplicationUser?> GetUserById(Guid userId)
    {
        var result = await GetUserById(userId.ToString());

        return result;
    }

    private async Task AddRoles(CancellationToken cancellationToken)
    {
        var roles = new string[] { UserRole.SystemAdministrator, UserRole.Administrator, UserRole.User };

        foreach (string role in roles)
        {
            if (!dbContext.Roles.Any(r => r.Name == role))
            {
                dbContext.Roles.Add(new ApplicationRole { Name = role, NormalizedName = role.ToUpper() });
            }
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}