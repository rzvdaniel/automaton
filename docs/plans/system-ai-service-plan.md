# AI-Powered App Name Generation via System AI Service

## Overview

Create a system-level AI service that uses a default AI provider/model to generate app names from user descriptions. This implements the foundation for the AI token system described in the launch plan - where Automaton provides AI tokens included with each tier rather than requiring users to bring their own API keys.

## Context (from Launch Plan)

- Users need AI to generate apps
- Requiring BYOK causes 70%+ drop-off
- System should provide AI tokens with each tier
- Default to cost-effective models (GPT-4o-mini, Claude Haiku)
- Track token usage per user

## Architecture Decision

### Approach: Simple AI Completion Endpoint

Instead of creating a full MCP tool, create a lightweight AI utility service with a simple endpoint for quick AI tasks like app name generation. This service will:

1. Use a system-configured default AI provider/model
2. Use the system's API key (not user's)
3. Track token usage per user (future: enforce quotas)
4. Be reusable for other simple AI tasks

## Implementation Plan

### Phase 1: System AI Configuration

**1. appsettings.json** - Add system AI config section

```json
{
  "SystemAi": {
    "DefaultProvider": "OpenAI",
    "DefaultModel": "gpt-4o-mini",
    "ApiKey": "sk-..." // System's API key
  }
}
```

**2. SystemAiConfig.cs** (`src/Automaton.Api/Config/`)

```csharp
public class SystemAiConfig
{
    public string DefaultProvider { get; set; } = "OpenAI";
    public string DefaultModel { get; set; } = "gpt-4o-mini";
    public string ApiKey { get; set; } = "";
}
```

### Phase 2: System AI Service

**3. ISystemAiService.cs** (`src/Automaton.Api/Services/`)

```csharp
public interface ISystemAiService
{
    Task<string> GenerateAppNameAsync(string description, string? userId = null);
    Task<string> CompleteAsync(string prompt, string? systemPrompt = null, string? userId = null);
}
```

**4. SystemAiService.cs** (`src/Automaton.Api/Services/`)

- Injects `AiProviderRegistry` from Automaton.Mcp
- Uses system API key (not user's)
- Simple non-streaming completion (no tools needed)
- Logs token usage (future: store in DB for quota tracking)

Implementation:

```csharp
public class SystemAiService : ISystemAiService
{
    private readonly AiProviderRegistry _providerRegistry;
    private readonly SystemAiConfig _config;
    private readonly ILogger<SystemAiService> _logger;

    public async Task<string> GenerateAppNameAsync(string description, string? userId = null)
    {
        var prompt = $"Generate a short, catchy app name (2-4 words) for this app description. Return ONLY the name, nothing else.\n\nDescription: {description}";
        return await CompleteAsync(prompt, "You are a helpful assistant that generates concise app names.", userId);
    }

    public async Task<string> CompleteAsync(string prompt, string? systemPrompt, string? userId)
    {
        var provider = _providerRegistry.GetProvider(_config.DefaultProvider);
        // Call provider's simple completion method
        // Log token usage with userId for future quota tracking
        return response;
    }
}
```

### Phase 3: API Endpoint

**5. AiUtilityController.cs** (`src/Automaton.Api/Controllers/`)

```csharp
[ApiController]
[Route("api/ai/utility")]
[Authorize]
public class AiUtilityController : ControllerBase
{
    [HttpPost("generate-app-name")]
    public async Task<ActionResult<GenerateAppNameResponse>> GenerateAppName(
        [FromBody] GenerateAppNameRequest request)
    {
        var name = await _systemAiService.GenerateAppNameAsync(
            request.Description,
            User.GetUserId());
        return Ok(new GenerateAppNameResponse { Name = name });
    }
}

public record GenerateAppNameRequest(string Description);
public record GenerateAppNameResponse { public string Name { get; set; } }
```

### Phase 4: Frontend Integration

**6. ai-utility.service.ts** (`src/automaton-studio-vue/src/shared/services/`)

```typescript
class AiUtilityService {
  async generateAppName(description: string): Promise<string> {
    const response = await apiClient.post<{ name: string }>(
      '/api/ai/utility/generate-app-name',
      { description }
    );
    return response.data.name;
  }
}
export const aiUtilityService = new AiUtilityService();
```

**7. Update AppListView.vue** - Use AI service for name generation

```typescript
const handleGenerateApp = async () => {
  if (!aiDescription.value.trim()) return

  isCreating.value = true
  try {
    // Generate name using AI
    const appName = await aiUtilityService.generateAppName(aiDescription.value)

    // Create app with AI-generated name
    const app = await appsStore.createApp({ name: appName })

    // Navigate to editor with prompt
    const prompt = encodeURIComponent(aiDescription.value.trim())
    router.push(`/apps/${app.id}?prompt=${prompt}`)
    aiDescription.value = ''
  } catch (err) {
    // Handle error
  } finally {
    isCreating.value = false
  }
}
```

### Phase 5: Provider Enhancement (if needed)

**8. Add simple completion to providers**

If `OpenAiProvider` and `AnthropicProvider` don't have a simple completion method (non-streaming, no tools), add one:

```csharp
public interface IAiProvider
{
    // Existing streaming method with tools
    Task<ConversationResult> StartConversationAsync(...);

    // New: Simple completion without tools/streaming
    Task<CompletionResult> CompleteAsync(string prompt, string? systemPrompt = null);
}
```

## Files to Create/Modify

### New Files (5)

1. `src/Automaton.Api/Config/SystemAiConfig.cs`
2. `src/Automaton.Api/Services/ISystemAiService.cs`
3. `src/Automaton.Api/Services/SystemAiService.cs`
4. `src/Automaton.Api/Controllers/AiUtilityController.cs`
5. `src/automaton-studio-vue/src/shared/services/ai-utility.service.ts`

### Modified Files (3)

1. `appsettings.json` / `appsettings.Development.json` - Add SystemAi section
2. `src/Automaton.Api/Program.cs` - Register SystemAiConfig and SystemAiService
3. `src/automaton-studio-vue/src/features/apps/views/AppListView.vue` - Use AI for name generation

### Possibly Modified (if simple completion doesn't exist)

1. `src/Automaton.Mcp/Providers/IAiProvider.cs` - Add CompleteAsync interface
2. `src/Automaton.Mcp/Providers/OpenAiProvider.cs` - Implement CompleteAsync
3. `src/Automaton.Mcp/Providers/AnthropicProvider.cs` - Implement CompleteAsync

## System AI Provider for Chat (Phase 2)

### Overview

Allow users to use the system AI provider in the AI Chat Panel (App Editor), not just for utility tasks like app name generation. This provides a zero-friction AI experience.

### User Experience

**AI Chat Panel provider dropdown:**
1. "Automaton AI" (system) - Always available, first option
2. User's own API keys (if configured)

### Backend Logic (Security)

**Never trust frontend flags.** Backend determines which key to use:

```csharp
// In AiConversationController.StreamConversation
if (string.IsNullOrEmpty(request.ApiKeyId))
{
    // No user key provided → use system provider
    // 1. Check rate limit
    if (!_rateLimitService.CheckRateLimit(userId))
        return StatusCode(429, "Rate limit exceeded");

    // 2. Use system API key
    apiKey = _systemAiConfig.ApiKey;
    provider = _systemAiConfig.DefaultProvider;
    model = _systemAiConfig.DefaultModel;
}
else
{
    // User provided their own key ID
    // 1. Verify key belongs to user
    // 2. No rate limiting (their cost)
    apiKey = await _aiApiKeyService.GetDecryptedApiKeyAsync(userId, request.ApiKeyId);
}
```

### Rate Limiting for Chat

**Same pool as utility requests** (simpler approach for now):
- All system AI requests share the same rate limit pool
- 10 requests/minute per user (utility + chat combined)
- Future: Consider separate limits if needed (chat uses more tokens)

**Why same pool:**
- Simpler to implement and understand
- Prevents gaming (spam utility requests, then switch to chat)
- Can split later if usage patterns require it

### Frontend Changes

**AiAssistant.vue:**
- Add "Automaton AI" as first provider option
- When selected, don't send `apiKeyId` in request
- Show user's keys below (if any configured)

### Files to Modify

1. `AiConversationController.cs` - Add system provider support with rate limiting
2. `AiAssistant.vue` - Add "Automaton AI" option to provider dropdown
3. `useAiChat.ts` - Handle system provider selection

## Rate Limiting

### Current: Per-User Rate Limiting (Implemented)

```csharp
"MaxRequestsPerUserPerMinute": 10
```

- Each user limited to 10 requests/minute
- Prevents single user from spamming the system
- Uses sliding window algorithm with in-memory cache
- Returns 429 (Too Many Requests) when exceeded
- **Shared pool**: Utility requests (app name) + Chat requests use same limit

### Future: Global Rate Limiting (Not Yet Needed)

**When to implement:** When approaching 1000+ concurrent active users

**Why it would be needed:**
1. **Hitting AI provider limits** - OpenAI Tier 1: 500 RPM, Tier 2: 5,000 RPM
2. **Cost control** - Cap maximum spend during traffic spikes or bot attacks
3. **Graceful degradation** - Queue requests instead of random failures

**Capacity analysis:**

| Active Users | Est. Requests/min | OpenAI Tier 1 (500 RPM) | OpenAI Tier 2 (5000 RPM) |
|--------------|-------------------|-------------------------|--------------------------|
| 50           | ~50               | OK                      | OK                       |
| 500          | ~100              | OK                      | OK                       |
| 1000         | ~200              | OK                      | OK                       |
| 2500         | ~500              | Risk                    | OK                       |
| 5000+        | ~1000+            | Exceeded                | OK                       |

**Implementation approach (when needed):**
```csharp
public class SystemAiConfig
{
    // Existing
    public int MaxRequestsPerUserPerMinute { get; set; } = 10;

    // Future: Global limits
    public int MaxGlobalRequestsPerMinute { get; set; } = 400;  // Stay under Tier 1
    public bool EnableRequestQueuing { get; set; } = false;
}
```

**Recommendation:** Skip global rate limiting for now. Per-user limiting is sufficient protection against abuse. Global limits are a "scale problem" - implement when user base grows significantly. By then, you'll likely be on OpenAI Tier 2+ anyway.

## Token Usage Tracking (Future)

This implementation lays groundwork for full token quota system:

1. `CompleteAsync` logs userId with each request
2. Future: Create `TokenUsage` entity and track per-user usage
3. Future: Add quota checks before AI calls
4. Future: UI to show remaining tokens

## Verification

1. **Backend build**: `dotnet build` succeeds
2. **Frontend build**: `npm run build` succeeds
3. **Manual test**:
   - Enter description: "I would like a random quote app"
   - Click "Generate App"
   - App created with AI-generated name like "Random Quote Generator"
   - Redirected to editor with AI assistant auto-sending prompt
4. **Check logs**: Token usage logged for the request

## Benefits

1. **Better UX**: Professional app names instead of "I would like..."
2. **Foundation for launch**: System AI key ready for token quota system
3. **Cost-effective**: Uses GPT-4o-mini (~$0.001 per name generation)
4. **Reusable**: `CompleteAsync` can power other quick AI tasks
5. **No user friction**: Works immediately, no BYOK required

---

*Document created: January 2026*
