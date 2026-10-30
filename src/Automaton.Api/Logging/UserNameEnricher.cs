using Serilog.Core;

namespace Automaton.Api.Logging;

public class UserNameEnricher : ILogEventEnricher
{
    readonly IHttpContextAccessor _httpContextAccessor;

    public UserNameEnricher(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void Enrich(Serilog.Events.LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (!(_httpContextAccessor.HttpContext?.User.Identity.IsAuthenticated ?? false))
        {
            return;
        }

        var user = _httpContextAccessor.HttpContext.User;
        var userName = user.Identity.Name;

        var userNameProperty = propertyFactory.CreateProperty("UserName", userName);
        logEvent.AddOrUpdateProperty(userNameProperty);
    }
}
