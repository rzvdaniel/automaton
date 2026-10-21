using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Security;
using Automaton.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Controllers;

[Route("api/Account/[action]")]
public sealed class AccountSubscriptionController(ApplicationDbContext db) : BaseController
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetSubscriptionPlans()
    {
        var plans = await db.SubscriptionPlans
            .AsNoTracking()
            .Where(plan => plan.IsActive)
            .OrderBy(plan => plan.SortOrder)
            .ThenBy(plan => plan.Name)
            .Select(plan => new
            {
                plan.Id,
                plan.Name,
                plan.Description,
                plan.PriceUsd,
                plan.CapabilitiesJson,
                plan.IsDefault,
                plan.IsActive,
                plan.SortOrder
            })
            .ToListAsync();

        return Ok(plans
            .Where(plan => !PlanCapabilitiesSerializer.HasLegacyHostedRunnerEntitlement(plan.CapabilitiesJson))
            .Select(plan => new
            {
                plan.Id,
                plan.Name,
                plan.Description,
                plan.PriceUsd,
                Capabilities = PlanCapabilitiesSerializer.Deserialize(plan.CapabilitiesJson),
                plan.IsDefault,
                plan.IsActive,
                plan.SortOrder
            }));
    }
}
