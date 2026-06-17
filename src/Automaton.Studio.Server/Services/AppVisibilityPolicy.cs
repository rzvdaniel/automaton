using Automaton.Api.Entities;
using Automaton.Api.Enums;

namespace Automaton.Api.Services;

public static class AppVisibilityPolicy
{
    public static bool IsMarketplaceVisible(WebApp app)
    {
        return app.IsPublished &&
               app.ReviewStatus == AppReviewStatuses.Approved &&
               app.AccessStatus == AppAccessStatuses.Active;
    }

    public static bool IsPubliclyAccessible(WebApp app)
    {
        return app.IsPublished &&
               app.AccessStatus == AppAccessStatuses.Active;
    }
}
