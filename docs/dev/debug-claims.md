# Debug Claims Endpoint (Development only)

This document contains a simple controller action you can use during development to inspect the current user's authentication status and claims. **Do not enable this in production.**

Recommended usage:
- Only include this endpoint when `ASPNETCORE_ENVIRONMENT` is `Development`.
- Prefer mapping a small development-only endpoint in `Program.cs` instead of leaving a controller action in production controllers.
- If you must add to a controller, make sure to restrict it to development and remove it before shipping.

Controller action (development-only):

```csharp
/// <summary>
/// Debug endpoint to check user claims
/// </summary>
[HttpGet("debug-claims")]
[AllowAnonymous]
public IActionResult DebugClaims()
{
    var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();
    var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
    var authType = User.Identity?.AuthenticationType;
    
    return Ok(new 
    { 
        IsAuthenticated = isAuthenticated,
        AuthenticationType = authType,
        Claims = claims 
    });
}
```

Alternative (preferred): map a development-only endpoint in `Program.cs`:

```csharp
if (app.Environment.IsDevelopment())
{
    app.MapGet("/debug/claims", (ClaimsPrincipal user) =>
    {
        return Results.Json(new
        {
            IsAuthenticated = user.Identity?.IsAuthenticated ?? false,
            AuthenticationType = user.Identity?.AuthenticationType,
            Claims = user.Claims.Select(c => new { c.Type, c.Value })
        });
    }).RequireAuthorization();
}
```

Notes:
- Use `.RequireAuthorization()` to ensure only authenticated users can call it (or `.AllowAnonymous()` if you specifically want to test unauthenticated requests).
- Add `[ApiExplorerSettings(IgnoreApi = true)]` or avoid adding to Swagger to prevent accidental exposure.
- Remove or guard this code before deploying to production.
