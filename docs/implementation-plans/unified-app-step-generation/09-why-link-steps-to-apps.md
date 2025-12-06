# Why Link Steps to Apps?

## Overview

This document explains the rationale behind the `AppCustomStep` relationship that explicitly links Custom Steps to Web Apps, rather than allowing apps to call any step by name.

---

## The Simple Question

> "Why can't an App just call any step by name? Why do we need explicit linking?"

Technically, an app **could** call any step owned by the user without explicit linking. However, the `AppCustomStep` relationship provides several important benefits that become critical as the system scales.

---

## Benefits of Explicit Linking

### 1. Security & Access Control ??

Without linking, you'd need to allow apps to call *any* step owned by the user. With explicit linking:

```csharp
// The execution service validates the step is linked to this app
var isLinked = await dbContext.AppCustomSteps
    .AnyAsync(acs => acs.AppId == appId && acs.CustomStepId == stepId);

if (!isLinked)
{
    return Unauthorized("Step is not linked to this app");
}
```

**This prevents:**
- A malicious/compromised app from executing steps it shouldn't
- Steps with sensitive operations (payment processing, admin commands) being callable from any app
- Future multi-tenant scenarios where apps might be shared but steps shouldn't be

---

### 2. Impact Analysis & Safe Deletion ???

When a user wants to delete or modify a step:

```csharp
// Query to find affected apps before changes
var appsUsingStep = await dbContext.AppCustomSteps
    .Where(acs => acs.CustomStepId == stepId)
    .Include(acs => acs.App)
    .Select(acs => acs.App)
    .ToListAsync();

if (appsUsingStep.Any())
{
    // Warn: "This step is used by 3 apps. Deleting it will break them."
}
```

**This enables:**
- Warning users before breaking changes
- Cascade notifications when a step is updated
- Dependency graph visualization
- "Where is this step used?" queries

---

### 3. Step Execution Scoping for Public Apps ??

For published apps that can be accessed by anonymous users:

```
App (public) ? Can only execute linked steps
           ? NOT all steps owned by the app creator
```

**This is critical because:**
- Without linking, anyone who finds your public app URL could potentially execute any of your backend steps
- Linked steps act as an explicit "allow list" for public execution
- You can have private utility steps that aren't exposed to any app

---

### 4. Version Tracking & Sync ??

The `AppCustomStep` junction can track versioning:

```csharp
public class AppCustomStep
{
    public Guid AppId { get; set; }
    public Guid CustomStepId { get; set; }
    public DateTime LinkedAt { get; set; }      // When was this bound?
    public int DisplayOrder { get; set; }        // Order in Backend Actions tab
    
    // Future enhancements:
    // public int? PinnedVersion { get; set; }   // Lock to specific version
    // public DateTime? LastExecuted { get; set; } // Usage tracking
}
```

**This enables:**
- "App X is using v3 of SendEmailStep, but v5 is available"
- Rollback capabilities
- Changelog tracking
- "Update all apps to latest step version" functionality

---

### 5. Rate Limiting & Quotas ??

From the API reference:

| Endpoint Type | Limit |
|---------------|-------|
| Step Execution (public) | 60 requests/minute **per app** |
| Step Execution (authenticated) | 120 requests/minute **per user** |

**Without app-step linking:**
- You couldn't enforce per-app limits
- Only per-user or global limits would be possible
- A single misbehaving app could exhaust the user's entire quota

---

### 6. Analytics & Observability ??

Linking enables powerful analytics:

```sql
-- Which apps are using this step the most?
SELECT a.Name, COUNT(*) as ExecutionCount
FROM StepExecutions se
JOIN AppCustomSteps acs ON se.AppId = acs.AppId AND se.StepId = acs.CustomStepId
JOIN Apps a ON acs.AppId = a.Id
GROUP BY a.Name
ORDER BY ExecutionCount DESC;

-- Which steps are unused? (No app links)
SELECT cs.Name
FROM CustomSteps cs
LEFT JOIN AppCustomSteps acs ON cs.Id = acs.CustomStepId
WHERE acs.AppId IS NULL;
```

**Use cases:**
- "This app is making 1000 calls to SendEmailStep—is that expected?"
- Cost attribution in future billing scenarios
- Identifying orphaned/unused steps
- Usage dashboards

---

### 7. AI Context for Code Generation ??

When the AI generates code, knowing linked steps helps:

```csharp
// From the MCP tools:
CreateTool("get_linked_steps", "Get list of steps already linked to this app")
```

**The AI can:**
- Avoid creating duplicate steps
- Suggest using existing linked steps instead of creating new ones
- Know what backend capabilities the app already has
- Generate proper `executeStep()` calls with correct step names

---

## Comparison: With vs Without Linking

| Without Linking | With Linking |
|-----------------|--------------|
| Any app calls any step | Explicit permissions |
| Can't warn before delete | Impact analysis |
| No per-app rate limits | Granular quotas |
| No version pinning | Can track/pin versions |
| Blind AI generation | AI knows app's capabilities |
| Public apps expose all steps | Public apps only expose linked steps |
| No usage analytics per app | Full observability |

---

## Data Model

The `AppCustomStep` entity is lightweight:

```csharp
public class AppCustomStep
{
    public Guid AppId { get; set; }           // FK to App
    public Guid CustomStepId { get; set; }    // FK to CustomStep
    public DateTime LinkedAt { get; set; }     // Audit trail
    public Guid LinkedByUserId { get; set; }   // Who linked it
    public int DisplayOrder { get; set; }      // UI ordering
}
```

**Storage impact:** ~50 bytes per link. Negligible overhead for significant benefits.

---

## When Could You Skip Linking?

For a **simple single-user, single-app scenario**, you could technically skip linking. However:

1. The overhead is minimal (one junction table)
2. Retrofitting linking later is harder than starting with it
3. Security best practices favor explicit permissions over implicit access

---

## Conclusion

The `AppCustomStep` relationship is a **lightweight investment** that enables:

- **Security**: Explicit allow-list for step execution
- **Safety**: Impact analysis before changes
- **Scalability**: Per-app rate limiting and quotas
- **Observability**: Usage analytics and dashboards
- **AI Intelligence**: Better code generation context
- **Future-proofing**: Version pinning, multi-tenant support

Even if you only use 20% of these features initially, the foundation will be there when you need it.

---

## Related Documentation

- [00-overview.md](./00-overview.md) - Feature overview
- [01-phase1-direct-step-execution.md](./01-phase1-direct-step-execution.md) - Step execution API
- [02-phase2-backend-actions-tab.md](./02-phase2-backend-actions-tab.md) - UI for managing links
- [06-data-models.md](./06-data-models.md) - Database schema details
