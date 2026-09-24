using Automaton.Api.Entities;
using Automaton.Api.Constants;
using Automaton.Api.Models;

namespace Automaton.Api.Controllers.Admin;

internal static class SubscriptionPlanUpdateApplier
{
    public static void Apply(
        SubscriptionPlan plan,
        UpdateSubscriptionPlanRequest request,
        bool isDefault)
    {
        ApplyDetails(plan, request);
        ApplyAccessSettings(plan, request, isDefault);
        plan.UpdatedAt = DateTime.UtcNow;
    }

    private static void ApplyDetails(SubscriptionPlan plan, UpdateSubscriptionPlanRequest request)
    {
        if (request.Name is not null)
        {
            plan.Name = request.Name;
        }

        if (request.Description is not null)
        {
            plan.Description = request.Description;
        }

        if (request.PriceUsd.HasValue)
        {
            plan.PriceUsd = request.PriceUsd.Value;
        }

        if (request.StripeProductId is not null)
        {
            plan.StripeProductId = request.StripeProductId == string.Empty ? null : request.StripeProductId;
        }

        if (request.StripePriceId is not null)
        {
            plan.StripePriceId = request.StripePriceId == string.Empty ? null : request.StripePriceId;
        }
    }

    private static void ApplyAccessSettings(
        SubscriptionPlan plan,
        UpdateSubscriptionPlanRequest request,
        bool isDefault)
    {
        if (request.IsActive.HasValue)
        {
            plan.IsActive = request.IsActive.Value;
        }

        plan.IsDefault = isDefault;

        if (request.Capabilities is not null)
        {
            plan.CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(request.Capabilities);
        }

        if (request.SortOrder.HasValue)
        {
            plan.SortOrder = request.SortOrder.Value;
        }
    }
}
