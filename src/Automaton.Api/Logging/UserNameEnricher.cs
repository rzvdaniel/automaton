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
        var identity = _httpContextAccessor.HttpContext?.User.Identity;
        if (identity?.IsAuthenticated != true)
        {
            return;
        }

        var userNameProperty = propertyFactory.CreateProperty("UserName", identity.Name);
        logEvent.AddOrUpdateProperty(userNameProperty);
    }
}
