using Automaton.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Automaton.Api.Middleware;

/// <summary>
/// Returns 404 for public store endpoints when an administrator has disabled the app store.
/// </summary>
public sealed class RequireStoreEnabledAttribute : TypeFilterAttribute
{
    public RequireStoreEnabledAttribute() : base(typeof(RequireStoreEnabledFilter))
    {
    }

    private sealed class RequireStoreEnabledFilter(SettingsService settingsService) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (!(await settingsService.GetStoreSettingsAsync()).IsEnabled)
            {
                context.Result = new NotFoundObjectResult(new { error = "The app store is disabled on this server." });
                return;
            }

            await next();
        }
    }
}
