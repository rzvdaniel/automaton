namespace Automaton.Api.Models;

public record SubscriptionPlanResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal PriceUsd { get; init; }
    public string? StripeProductId { get; init; }
    public string? StripePriceId { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public PlanCapabilities Capabilities { get; init; } = PlanCapabilities.Default;
    public int SortOrder { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
public record CreateSubscriptionPlanRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal PriceUsd { get; init; }
    public string? StripeProductId { get; init; }
    public string? StripePriceId { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; } = true;
    public PlanCapabilities Capabilities { get; init; } = PlanCapabilities.Default;
    public int SortOrder { get; init; }
}

public record UpdateSubscriptionPlanRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public decimal? PriceUsd { get; init; }
    public string? StripeProductId { get; init; }
    public string? StripePriceId { get; init; }
    public bool? IsDefault { get; init; }
    public bool? IsActive { get; init; }
    public PlanCapabilities? Capabilities { get; init; }
    public int? SortOrder { get; init; }
}

public record UserPlanResponse
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public Guid? SubscriptionPlanId { get; init; }
    public string? PlanName { get; init; }
}

public record AssignPlanRequest
{
    public Guid? PlanId { get; init; }
}
