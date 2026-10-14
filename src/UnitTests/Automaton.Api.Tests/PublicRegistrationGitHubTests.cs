using System.Reflection;
using Automaton.Api.Authentication.GitHub;
using Automaton.Api.Controllers;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public class PublicRegistrationGitHubTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task PausedRegistration_GitHubBlocksNewUsersAndResolvesExistingUsers(bool existing)
    {
        var user = existing ? new ApplicationUser
        {
            Id = Guid.NewGuid(), Email = "user@example.com", PublicUsername = "existing"
        } : null;
        using var manager = new RegistrationUserManager(user);
        var controller = CreateController(manager);
        var method = typeof(GitHubAuthController).GetMethod("ResolveUserAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = await (Task<(ApplicationUser? User, string? Error)>)method.Invoke(controller,
            new object[] { "user@example.com", "github-user", "User" })!;
        Assert.That(result.User, Is.SameAs(user));
        Assert.That(result.Error, Is.EqualTo(existing ? null : ConfigurationService.RegistrationPausedMessage));
    }

    private static GitHubAuthController CreateController(UserManager<ApplicationUser> manager)
    {
        var users = new UsersService(manager, null!, null!, null!, null!, null!);
        var configuration = new ConfigurationService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:UserSignUp"] = "false" })
            .Build());
        return new GitHubAuthController(null!, configuration,
            Options.Create(new GitHubOAuthOptions()), NullLogger<GitHubAuthController>.Instance,
            null!, null!, users);
    }

    private sealed class RegistrationUserManager(ApplicationUser? user) : UserManager<ApplicationUser>(
        new AdminPlanStubUserStore(), Microsoft.Extensions.Options.Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
        [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!,
        NullLogger<UserManager<ApplicationUser>>.Instance)
    {
        public override Task<ApplicationUser?> FindByEmailAsync(string email) => Task.FromResult(user);
    }
}
