using Automaton.Api.Config;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Automaton.Infrastructure.Email;

public sealed class EmailConfirmationLinkBuilder(IOptions<AppConfig> appConfigOptions)
{
    private readonly AppConfig _appConfig = appConfigOptions.Value;

    public string Build(Guid userId, string token)
    {
        var apiBaseUrl = _appConfig.ApiUrl.TrimEnd('/');
        var query = new Dictionary<string, string?>
        {
            ["userId"] = userId.ToString(),
            ["token"] = token
        };

        return QueryHelpers.AddQueryString($"{apiBaseUrl}/api/Account/ConfirmEmail", query);
    }
}
