# Platform-Provided AI Credits Implementation Plan

## Executive Summary

This document outlines the plan for implementing a platform-provided AI credits system for Automaton Studio, allowing users to utilize AI Chat panels in the Step Designer and App Designer without initially providing their own AI API keys. This feature will enable new users to start building Apps, Flows, and Custom Steps using AI assistance immediately after subscription, following industry best practices from platforms like Cursor, Vercel v0, GitHub Copilot, and Replit.

> **Note**: This document is part of the overall [Automaton Studio Subscription Model](./subscription-model.md). Please refer to that document for comprehensive subscription tier details, pricing, and how AI credits integrate with other platform features like Cloud Runners and core platform capabilities.

### Key Design Decisions

1. **Platform-Managed AI Keys** - Studio server stores and manages master AI API keys for supported providers
2. **Usage Tracking** - Track token consumption per user for quota management and billing
3. **Tiered Quotas** - Different AI credit allocations based on subscription tier (Free, Pro, Enterprise)
4. **Graceful Fallback** - Users can always add their own API keys for unlimited usage
5. **Provider Support** - Initially support Anthropic (Claude) and Google (Gemini)
6. **Transparent Usage** - Users can see their remaining credits and usage history
7. **Rate Limiting** - Prevent abuse with per-user rate limits

---

## Industry Standards & Best Practices

### Comparable Products

| Platform | Free Tier | Paid Tier | Approach |
|----------|-----------|-----------|----------|
| **Cursor** | 50 fast requests/month | Unlimited Pro ($20/mo) | Platform keys with fallback to user keys |
| **Vercel v0** | 200 credits/month | 3,000 credits/month ($20/mo) | Platform-managed, credits per generation |
| **GitHub Copilot** | No free tier | $10/mo or $100/year | Platform-managed, unlimited usage |
| **Replit** | Limited AI usage | Unlimited ($20/mo) | Platform-managed with quota |
| **Bolt.new** | 20 free prompts | Unlimited ($20/mo) | Platform-managed credits |

### Common Patterns

1. **Token-based quotas** - Most platforms use a credit/token system rather than request counts
2. **Monthly reset** - Quotas reset monthly to encourage consistent usage
3. **Transparent limits** - Users can see remaining credits/usage
4. **Upgrade prompts** - When limits are reached, suggest upgrading or adding own keys
5. **Provider abstraction** - Users don't need to know which AI provider is used
6. **Grace period** - Small overage allowance before hard cutoff

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│  Automaton.Studio (Blazor Frontend)                             │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  Step Designer / App Designer                              │ │
│  │                                                            │ │
│  │  ┌──────────────────────────────────────────────────────┐ │ │
│  │  │  AI Chat Panel                                       │ │ │
│  │  │                                                      │ │ │
│  │  │  Model Selector:                                    │ │ │
│  │  │  ○ Platform (default) - uses platform credits       │ │ │
│  │  │  ○ My API Keys - uses user's own keys              │ │ │
│  │  │                                                      │ │ │
│  │  │  [Remaining: 8,500 / 10,000 tokens] 💡             │ │ │
│  │  └──────────────────────────────────────────────────────┘ │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ HTTPS / SignalR
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  Automaton.Studio.Server                                        │
│                                                                 │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  AiCreditService                                           │ │
│  │  - GetUserQuota()                                          │ │
│  │  - GetRemainingCredits()                                   │ │
│  │  - ConsumeCredits(userId, tokensUsed)                      │ │
│  │  - GetUsageHistory()                                       │ │
│  └────────────────────────────────────────────────────────────┘ │
│                              │                                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  PlatformAiKeyManager                                      │ │
│  │  - GetPlatformApiKey(provider)                             │ │
│  │  - RotateKeys()                                            │ │
│  │  - IsProviderAvailable(provider)                           │ │
│  └────────────────────────────────────────────────────────────┘ │
│                              │                                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  AiProviderFactory (enhanced)                              │ │
│  │  - CreateProvider(provider, useSource)                     │ │
│  │  - EstimateTokens(prompt)                                  │ │
│  │  - TrackUsage(userId, provider, tokensUsed)                │ │
│  └────────────────────────────────────────────────────────────┘ │
│                              │                                  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  External AI Providers                                          │
│                                                                 │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐         │
│  │  Anthropic   │  │  Google AI   │  │  OpenAI      │         │
│  │  (Claude)    │  │  (Gemini)    │  │  (GPT)       │         │
│  └──────────────┘  └──────────────┘  └──────────────┘         │
└─────────────────────────────────────────────────────────────────┘
```

---

## Database Schema

### AiUsage Entity (New)

Tracks AI token consumption per user for quota management and analytics.

```csharp
public class AiUsage
{
    public Guid Id { get; set; }
    
    // User association
    public Guid UserId { get; set; }
    public User User { get; set; }
    
    // AI Provider details
    public AiProviderType Provider { get; set; }      // Anthropic, Google, OpenAI
    public string Model { get; set; }                  // claude-sonnet-4, gemini-2.5-pro, etc.
    
    // Usage tracking
    public AiKeySource KeySource { get; set; }         // Platform or UserProvided
    public int TokensUsed { get; set; }                // Estimated tokens consumed
    public int InputTokens { get; set; }               // Input tokens (if available from API)
    public int OutputTokens { get; set; }              // Output tokens (if available from API)
    
    // Cost tracking
    public decimal EstimatedCost { get; set; }         // USD cost estimate
    
    // Context
    public string FeatureContext { get; set; }         // "StepDesigner", "AppDesigner", "FlowDesigner"
    public Guid? RelatedEntityId { get; set; }         // StepId, AppId, FlowId (optional)
    
    // Timestamps
    public DateTime CreatedAt { get; set; }
    
    // Request metadata
    public string? RequestId { get; set; }             // For debugging/correlation
    public bool WasSuccessful { get; set; }            // Whether the API call succeeded
    public string? ErrorMessage { get; set; }          // Error details if failed
}

public enum AiKeySource
{
    Platform = 1,      // Using platform-provided API keys
    UserProvided = 2   // Using user's own API keys
}
```

### UserSubscription Entity (New)

Manages user subscription tiers and AI credit quotas.

```csharp
public class UserSubscription
{
    public Guid Id { get; set; }
    
    // User association
    public Guid UserId { get; set; }
    public User User { get; set; }
    
    // Subscription tier
    public SubscriptionTier Tier { get; set; }         // Free, Pro, Enterprise
    
    // AI Credits quota
    public int MonthlyTokenQuota { get; set; }         // Total tokens per month
    public int TokensUsedThisMonth { get; set; }       // Tokens consumed in current period
    public DateTime QuotaResetDate { get; set; }       // When quota resets (monthly)
    
    // Subscription lifecycle
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }             // null = active indefinitely
    public bool IsActive { get; set; }
    
    // Billing (future integration)
    public string? StripeCustomerId { get; set; }      // For future Stripe integration
    public string? StripeSubscriptionId { get; set; }
    
    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum SubscriptionTier
{
    Free = 1,          // Free tier with limited AI credits
    Pro = 2,           // Pro tier with generous AI credits
    Enterprise = 3     // Enterprise with unlimited or very high limits
}
```

### PlatformAiKey Entity (New)

Stores platform-managed AI API keys for shared usage.

```csharp
public class PlatformAiKey
{
    public Guid Id { get; set; }
    
    // Provider details
    public AiProviderType Provider { get; set; }
    public string Name { get; set; }                   // "Primary Anthropic Key", "Backup Google Key"
    
    // Encrypted API key
    public string EncryptedApiKey { get; set; }
    
    // Key rotation & management
    public bool IsActive { get; set; }
    public int Priority { get; set; }                  // 1 = primary, 2 = backup, etc.
    public DateTime? LastRotatedAt { get; set; }
    
    // Usage tracking
    public long TotalTokensUsed { get; set; }          // Lifetime token usage
    public decimal TotalCostUsd { get; set; }          // Lifetime cost
    public DateTime? LastUsedAt { get; set; }
    
    // Rate limiting
    public int DailyTokenLimit { get; set; }           // Max tokens per day for this key
    public int TokensUsedToday { get; set; }
    public DateTime LastDailyReset { get; set; }
    
    // Status monitoring
    public bool HasError { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTime? LastErrorAt { get; set; }
    
    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

---

## Proposed Subscription Tiers & Quotas

> **See also**: [Subscription Model](./subscription-model.md) for the complete tier comparison including Cloud Runners, published apps, flows, and other platform features.

Based on industry standards and cost considerations:

### Free Tier
- **Monthly AI Credits**: 10,000 tokens (~25-50 AI chat interactions)
- **Equivalent Cost**: ~$0.15-0.30/month in AI API costs
- **Purpose**: Allow new users to try AI features and build 1-2 small apps/flows
- **Restrictions**: 
  - Platform models only (user can't select specific model)
  - Rate limit: 100 requests/day
  - Standard response time
- **Add-ons**: ❌ Not available (upgrade to Pro for add-ons)

### Pro Tier ($20/month)
- **Monthly AI Credits**: 500,000 tokens (~1,250-2,500 AI chat interactions)
- **Equivalent Cost**: ~$7.50-15/month in AI API costs
- **Purpose**: Power users building multiple apps and complex flows
- **Benefits**:
  - Model selection (choose between Claude, Gemini, GPT)
  - Higher rate limit: 1,000 requests/day
  - Priority processing
  - Rollover of unused credits (up to 50%)
- **Add-ons**: ✅ Purchase additional tokens
  - **$10 per 250,000 tokens** (no expiration)
  - Automatically used when monthly quota exhausted
  - Stack unlimited add-on purchases

### Enterprise Tier (Custom pricing)
- **Monthly AI Credits**: 5,000,000+ tokens (unlimited in practice)
- **Purpose**: Organizations with heavy AI usage needs
- **Benefits**:
  - Unlimited AI usage via platform keys
  - Dedicated API keys option
  - Custom models/fine-tuning support
  - SSO integration
  - Priority support
- **Add-ons**: ✅ Custom volume pricing available

### Bring Your Own Key (BYOK)
- Available on **all tiers** (including Free)
- Users who add their own AI API keys get **unlimited usage** via their keys
- Platform credits are used as fallback if user's key fails
- No additional cost - user pays AI provider directly

---

## Implementation Phases

### Phase 1: Foundation (Week 1-2)

**Database & Entities**
- [ ] Create `AiUsage` entity and migration
- [ ] Create `UserSubscription` entity and migration
- [ ] Create `PlatformAiKey` entity and migration
- [ ] Add indexes for performance (UserId, CreatedAt, Provider)
- [ ] Create seed data for default subscription tiers

**Configuration**
- [ ] Add platform AI keys to appsettings.json (encrypted section)
- [ ] Create `PlatformAiConfig` section in configuration
  ```json
  "PlatformAiConfig": {
    "Enabled": true,
    "DefaultProvider": "Anthropic",
    "Providers": {
      "Anthropic": {
        "PrimaryKey": "env:PLATFORM_ANTHROPIC_KEY",
        "BackupKey": "env:PLATFORM_ANTHROPIC_KEY_2",
        "DailyTokenLimit": 1000000
      },
      "Google": {
        "PrimaryKey": "env:PLATFORM_GOOGLE_KEY",
        "DailyTokenLimit": 1000000
      }
    }
  }
  ```

### Phase 2: Core Services (Week 2-3)

**PlatformAiKeyManager Service**
- [ ] Create `IPlatformAiKeyManager` interface
- [ ] Implement `PlatformAiKeyManager` class
  - GetActiveKey(provider): Returns best available platform key
  - RotateToBackup(provider): Switch to backup key on failure
  - TrackKeyUsage(keyId, tokensUsed): Update usage stats
  - CheckDailyLimit(keyId): Verify key hasn't exceeded daily limit
- [ ] Add encryption/decryption for platform keys
- [ ] Add health monitoring for keys (automatic retry on errors)

**AiCreditService**
- [ ] Create `IAiCreditService` interface
- [ ] Implement `AiCreditService` class
  - GetUserSubscription(userId): Retrieve subscription details
  - GetRemainingCredits(userId): Calculate available tokens
  - CanConsumeCredits(userId, estimatedTokens): Check quota before request
  - ConsumeCredits(userId, provider, tokensUsed, context): Record usage
  - GetUsageHistory(userId, startDate, endDate): Retrieve usage logs
  - ResetMonthlyQuotas(): Background job to reset quotas monthly
- [ ] Add token estimation logic (prompt length → estimated tokens)

**Enhanced AiProviderFactory**
- [ ] Modify `AiProviderFactory` to support key source selection
- [ ] Add method `CreateProvider(provider, keySource, userId)`
- [ ] Integrate with `PlatformAiKeyManager` for platform keys
- [ ] Integrate with existing `AiApiKeyService` for user keys
- [ ] Add usage tracking hooks to record token consumption

### Phase 3: API Endpoints (Week 3)

**AiCreditsController** (New)
```csharp
[ApiController]
[Route("api/[controller]")]
public class AiCreditsController : ControllerBase
{
    [HttpGet("quota")]
    public async Task<ActionResult<UserQuotaResponse>> GetQuota()
    
    [HttpGet("usage")]
    public async Task<ActionResult<UsageHistoryResponse>> GetUsage(
        [FromQuery] DateTime? startDate, 
        [FromQuery] DateTime? endDate)
    
    [HttpGet("available-sources")]
    public async Task<ActionResult<AvailableSourcesResponse>> GetAvailableSources()
}
```

**Modified AiApiKeysController**
- [ ] Update `GetAvailableProvidersAsync` to include platform providers
- [ ] Add `keySource` parameter to indicate Platform vs UserProvided

### Phase 4: Frontend Integration (Week 4)

**AI Chat Panel Enhancement**
- [ ] Add key source selector to `AiChatPanel.razor`
  ```html
  <Radio.Group @bind-Value="selectedKeySource">
    <Radio Value="Platform">Platform Credits (8,500 remaining)</Radio>
    <Radio Value="UserProvided">My API Keys</Radio>
  </Radio.Group>
  ```
- [ ] Display remaining credits badge
- [ ] Show upgrade prompt when quota is low (<10%)
- [ ] Disable platform option when quota exhausted (fallback to user keys)

**Settings Page**
- [ ] Add AI Credits section to Settings page
- [ ] Display current subscription tier
- [ ] Show monthly quota and usage
- [ ] Add link to manage subscription
- [ ] Display usage chart (tokens over time)

**Subscription Management Page** (New)
- [ ] Create `/subscription` page
- [ ] Display tier comparison table
- [ ] Add upgrade/downgrade buttons
- [ ] Show billing history
- [ ] Integration placeholder for Stripe (future)

### Phase 5: Background Jobs (Week 4)

**Quota Reset Job**
- [ ] Create background service to reset monthly quotas
- [ ] Schedule: Run daily at midnight UTC
- [ ] Logic: If current date > QuotaResetDate, reset TokensUsedThisMonth and set new QuotaResetDate

**Usage Analytics Job**
- [ ] Aggregate daily/weekly/monthly usage statistics
- [ ] Store in `AiUsageAnalytics` table for quick reporting
- [ ] Calculate costs per user for internal monitoring

**Platform Key Health Monitor**
- [ ] Monitor platform key health (error rates, rate limits)
- [ ] Auto-rotate to backup keys on repeated failures
- [ ] Send alerts to admins when keys need attention

### Phase 6: Admin Dashboard (Week 5)

**AI Credits Admin Page** (New `/admin/ai-credits`)
- [ ] Overview dashboard
  - Total platform AI spend (daily/monthly)
  - Per-provider cost breakdown
  - Top users by consumption
  - Average tokens per user per tier
- [ ] Manage platform keys
  - Add/remove/rotate keys
  - View key health and usage
  - Configure daily limits
- [ ] User subscription management
  - View all subscriptions
  - Manually adjust quotas
  - Grant bonus credits
- [ ] Usage analytics
  - Export usage data
  - Cost forecasting
  - Anomaly detection (unusual usage patterns)

---

## Migration Strategy

### Existing Users
1. **Default tier assignment**: All existing users → Free tier (10,000 tokens/month)
2. **Migration script**: Create `UserSubscription` records for all existing users
3. **Backward compatibility**: Existing user API keys continue to work (no change)
4. **Opt-in upgrade**: Email existing users about new platform AI credits feature

### New Users
1. **On registration**: Automatically create Free tier subscription
2. **Onboarding flow**: Highlight AI features in welcome tour
3. **First run**: Show tooltip in AI Chat panel about platform credits

---

## Security Considerations

### Platform Key Security
1. **Encryption at rest**: Use AES-256 encryption for platform keys in database
2. **Environment variables**: Store production keys in secure environment variables
3. **Key rotation**: Rotate platform keys quarterly
4. **Access logging**: Log all platform key access for audit trail
5. **Least privilege**: Only `PlatformAiKeyManager` service can decrypt keys

### Rate Limiting
1. **Per-user limits**: Prevent single user from consuming all platform credits
2. **Per-key limits**: Protect platform keys from hitting provider rate limits
3. **Exponential backoff**: On rate limit errors, implement exponential backoff
4. **Circuit breaker**: Disable platform provider if error rate exceeds threshold

### Abuse Prevention
1. **Request validation**: Validate prompt content (no excessive length)
2. **Usage monitoring**: Flag users with abnormal usage patterns
3. **Temporary suspension**: Auto-suspend users who exceed fair use by 10x
4. **CAPTCHA**: Consider CAPTCHA for high-frequency AI requests

---

## Cost Management

### Estimated Monthly Costs

**Assumptions:**
- 1,000 active users
- 70% on Free tier, 25% on Pro tier, 5% on Enterprise tier
- Average usage: 50% of quota
- Token costs: $0.015 per 1,000 tokens (blended rate)

**Projected Costs:**
```
Free tier:  700 users × 10,000 tokens × 50% usage × $0.015/1k    = $52.50
Pro tier:   250 users × 500,000 tokens × 50% usage × $0.015/1k  = $937.50
Enterprise: 50 users × 5,000,000 tokens × 50% usage × $0.015/1k = $1,875.00

Total monthly platform AI cost: ~$2,865
Revenue (Pro + Enterprise): (250 × $20) + (50 × $100) = $10,000
Net margin: $7,135 (71% margin)
```

### Cost Optimization Strategies

1. **Smart provider selection**: Route requests to cheapest provider for the task
2. **Caching**: Cache common AI responses (e.g., "create a button component")
3. **Compression**: Reduce token usage by compressing context
4. **Batch processing**: Combine multiple small requests into one
5. **Model selection**: Use cheaper models (Haiku, Gemini Flash) for simple tasks
6. **Usage alerts**: Notify admins when costs exceed budget thresholds

---

## Monitoring & Analytics

### Key Metrics to Track

1. **Usage Metrics**
   - Daily Active Users (DAU) using AI features
   - Average tokens per user per day
   - Peak usage hours
   - Most popular features (StepDesigner vs AppDesigner)

2. **Cost Metrics**
   - Total platform AI spend (daily/monthly)
   - Cost per user
   - Cost per subscription tier
   - Cost by provider (Anthropic vs Google vs OpenAI)

3. **Performance Metrics**
   - AI response time (p50, p95, p99)
   - Error rate by provider
   - Platform key health score
   - Quota exhaustion rate

4. **Business Metrics**
   - Conversion rate (Free → Pro)
   - Churn rate by tier
   - AI feature adoption rate
   - Time to first AI interaction (new users)

### Dashboards

**Admin Dashboard**
- Real-time cost counter
- Usage heatmap
- Top 10 users by consumption
- Provider health status

**User Dashboard**
- Personal usage graph (last 30 days)
- Remaining credits gauge
- Usage breakdown by feature
- Upgrade recommendations

---

## Testing Strategy

### Unit Tests
- [ ] `AiCreditService` quota calculations
- [ ] `PlatformAiKeyManager` key selection logic
- [ ] Token estimation accuracy
- [ ] Encryption/decryption of platform keys

### Integration Tests
- [ ] End-to-end AI request flow (user → platform key → provider → response)
- [ ] Quota enforcement (reject request when quota exceeded)
- [ ] Key rotation on failure
- [ ] Usage recording accuracy

### Load Tests
- [ ] 100 concurrent AI requests
- [ ] Daily quota reset with 10,000 users
- [ ] Platform key rate limiting behavior

### Manual Tests
- [ ] Free tier user experience (quota warnings, upgrade prompts)
- [ ] Pro tier upgrade flow
- [ ] BYOK fallback when platform quota exhausted
- [ ] Admin dashboard usability

---

## Documentation

### User Documentation
- [ ] **Getting Started with AI Features** - Tutorial for new users
- [ ] **Understanding AI Credits** - Explain quotas, tiers, and billing
- [ ] **Bring Your Own Key (BYOK)** - Guide to adding personal API keys
- [ ] **Subscription Tiers Comparison** - Feature matrix and pricing
- [ ] **Usage Dashboard Guide** - How to monitor AI consumption

### Developer Documentation
- [ ] **AI Credit System Architecture** - System design and data flow
- [ ] **Platform AI Key Management** - How to add/rotate platform keys
- [ ] **Token Estimation Logic** - Algorithm for estimating token usage
- [ ] **Extending AI Providers** - How to add support for new AI providers
- [ ] **API Reference** - AiCreditsController endpoints

### Admin Documentation
- [ ] **Monitoring AI Costs** - Dashboard walkthrough
- [ ] **Managing Platform Keys** - Key rotation and health monitoring
- [ ] **Handling Quota Issues** - Troubleshooting user quota problems
- [ ] **Anomaly Detection** - Identifying and handling abuse

---

## Rollout Plan

### Beta Phase (Week 6)
1. **Internal testing**: Test with 10 internal team users
2. **Invite-only beta**: Invite 50 trusted users to test
3. **Gather feedback**: Survey beta users on UX and quota adequacy
4. **Monitor costs**: Validate cost projections with real usage data

### Soft Launch (Week 7)
1. **Enable for new users**: All new registrations get Free tier with AI credits
2. **Email existing users**: Announce feature with opt-in link
3. **Monitor closely**: Watch for errors, cost spikes, abuse
4. **Iterate quickly**: Fix issues within 24 hours

### Full Launch (Week 8)
1. **Enable for all users**: Migrate remaining existing users to Free tier
2. **Marketing campaign**: Blog post, social media, email announcement
3. **Upgrade incentives**: Offer 20% discount on first Pro month
4. **Track conversions**: Measure Free → Pro conversion rate

### Post-Launch (Ongoing)
1. **Weekly reviews**: Review usage, costs, and user feedback
2. **Monthly optimizations**: Tune quotas and pricing based on data
3. **Quarterly surveys**: Ask users about AI feature satisfaction
4. **Continuous improvement**: Add new AI features and models

---

## Future Enhancements

### Short-term (3-6 months)
- [ ] **Model selection for Pro users**: Let Pro users choose specific AI models
- [ ] **Prompt templates library**: Pre-built prompts for common tasks
- [ ] **AI suggestions**: Proactive AI suggestions while coding
- [ ] **Voice input**: Speak prompts instead of typing
- [ ] **Collaborative AI**: Share AI chat sessions with team members

### Medium-term (6-12 months)
- [ ] **Fine-tuned models**: Train custom models on user's codebase
- [ ] **AI workflows**: Chain multiple AI calls for complex tasks
- [ ] **Code review assistant**: AI-powered code review comments
- [ ] **Documentation generator**: Auto-generate docs from code
- [ ] **Test generation**: AI generates unit tests for flows/steps

### Long-term (12+ months)
- [ ] **Multi-modal AI**: Support image, video, audio inputs
- [ ] **Autonomous agents**: AI agents that complete tasks end-to-end
- [ ] **Custom AI providers**: Users can add their own AI endpoints
- [ ] **AI marketplace**: Share and sell AI prompts/templates
- [ ] **Enterprise SSO integration**: SAML, OAuth for enterprise users

---

## Risk Mitigation

### Technical Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| AI provider API outage | Medium | High | Multi-provider redundancy, graceful degradation |
| Runaway costs from abuse | Low | High | Aggressive rate limiting, usage monitoring, auto-suspension |
| Token estimation inaccuracy | Medium | Medium | Conservative estimates (1.2x buffer), periodic calibration |
| Key rotation downtime | Low | Medium | Blue-green key deployment, health checks before rotation |
| Database performance issues | Low | Medium | Proper indexing, usage data archival (>90 days to cold storage) |

### Business Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Low Free→Pro conversion | Medium | Medium | A/B test quota levels, optimize upgrade prompts |
| AI provider price increases | Medium | High | Diversify providers, BYOK as escape hatch |
| User backlash on limits | Low | Medium | Transparent communication, generous free tier |
| Competitor offers better terms | Medium | Medium | Monitor competition, adjust quotas/pricing quarterly |
| Regulatory compliance (GDPR, AI Act) | Low | High | Legal review, data retention policies, EU compliance |

---

## Success Metrics

### Immediate (First 30 days)
- 80%+ of new users try AI features at least once
- <1% error rate on AI requests
- Platform AI cost within 10% of projections
- Average response time <5 seconds

### Short-term (3 months)
- 5-10% Free → Pro conversion rate
- 50%+ monthly active users use AI features regularly
- Net Promoter Score (NPS) >50 for AI features
- 95%+ uptime for AI features

### Long-term (12 months)
- AI features are #1 cited reason for choosing Automaton
- 25%+ of revenue from Pro/Enterprise AI usage
- 10+ new AI-powered features launched
- Industry recognition (awards, mentions in dev community)

---

## Appendix

### Appendix A: Token Pricing (as of 2024)

| Provider | Model | Input (per 1M tokens) | Output (per 1M tokens) |
|----------|-------|----------------------|------------------------|
| Anthropic | Claude Sonnet 4 | $3.00 | $15.00 |
| Anthropic | Claude Haiku 4.5 | $1.00 | $5.00 |
| Google | Gemini 2.5 Pro | $2.50 | $10.00 |
| Google | Gemini 3 Pro | $5.00 | $20.00 |
| OpenAI | GPT-4o | $5.00 | $15.00 |
| OpenAI | GPT-5 | $10.00 | $30.00 |

### Appendix B: Sample API Requests

**Get User Quota**
```http
GET /api/aicredits/quota
Authorization: Bearer {jwt_token}

Response:
{
  "tier": "Free",
  "monthlyQuota": 10000,
  "tokensUsed": 1500,
  "tokensRemaining": 8500,
  "resetDate": "2024-02-01T00:00:00Z",
  "percentageUsed": 15.0
}
```

**Get Usage History**
```http
GET /api/aicredits/usage?startDate=2024-01-01&endDate=2024-01-31
Authorization: Bearer {jwt_token}

Response:
{
  "totalTokens": 1500,
  "totalCost": 0.0225,
  "usageByProvider": {
    "Anthropic": 1000,
    "Google": 500
  },
  "usageByFeature": {
    "StepDesigner": 800,
    "AppDesigner": 700
  },
  "dailyUsage": [
    { "date": "2024-01-01", "tokens": 50 },
    { "date": "2024-01-02", "tokens": 100 }
  ]
}
```

### Appendix C: Configuration Examples

**appsettings.Production.json**
```json
{
  "PlatformAiConfig": {
    "Enabled": true,
    "DefaultProvider": "Anthropic",
    "FallbackProvider": "Google",
    "Providers": {
      "Anthropic": {
        "PrimaryKeyEnvVar": "PLATFORM_ANTHROPIC_KEY",
        "BackupKeyEnvVar": "PLATFORM_ANTHROPIC_KEY_BACKUP",
        "DailyTokenLimit": 1000000,
        "PreferredModel": "claude-haiku-4.5"
      },
      "Google": {
        "PrimaryKeyEnvVar": "PLATFORM_GOOGLE_KEY",
        "DailyTokenLimit": 1000000,
        "PreferredModel": "gemini-2.5-pro"
      }
    }
  },
  "SubscriptionTiers": {
    "Free": {
      "MonthlyTokenQuota": 10000,
      "DailyRequestLimit": 100,
      "RateLimitPerMinute": 10
    },
    "Pro": {
      "MonthlyTokenQuota": 500000,
      "DailyRequestLimit": 1000,
      "RateLimitPerMinute": 50,
      "RolloverPercentage": 50
    },
    "Enterprise": {
      "MonthlyTokenQuota": 5000000,
      "DailyRequestLimit": 10000,
      "RateLimitPerMinute": 200,
      "RolloverPercentage": 100
    }
  }
}
```

---

## Conclusion

This implementation plan provides a comprehensive roadmap for adding platform-provided AI credits to Automaton Studio. By following industry best practices and implementing in phased iterations, we can deliver a compelling feature that:

1. **Lowers barriers to entry** - New users can start using AI features immediately
2. **Increases engagement** - AI-powered features drive daily active usage
3. **Generates revenue** - Tiered quotas encourage upgrades to Pro/Enterprise
4. **Reduces friction** - Users don't need to manage their own API keys initially
5. **Maintains flexibility** - BYOK option for power users and cost-conscious users

The proposed Free tier (10,000 tokens/month) is sufficient for casual users while encouraging upgrades, and the Pro tier (500,000 tokens/month) is competitive with industry standards. The technical architecture is sound, scalable, and secure, with proper monitoring and cost controls in place.

**Estimated total development time**: 5-6 weeks
**Estimated monthly operational cost**: $3,000-5,000 (for 1,000 active users)
**Expected ROI**: Break-even at ~300 Pro subscribers, with 71% margin at scale

---

**Document Version**: 1.0  
**Last Updated**: December 20, 2024  
**Author**: Automaton Development Team  
**Status**: Draft - Pending Review
