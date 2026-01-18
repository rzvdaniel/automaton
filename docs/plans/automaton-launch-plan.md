# Automaton Launch Plan: Web Apps MVP

## Vision

An AI-powered platform where users describe apps in plain English, get working web applications with Python backends, and can run them on Automaton's infrastructure or their own.

**Tagline:** "Create AI-powered apps in minutes. Run them anywhere."

---

## What You're Building (MVP Scope)

| Included | Excluded (for now) |
|----------|-------------------|
| AI app generation (HTML/CSS/JS + Python steps) | App store / marketplace |
| Hosted runner on your infrastructure | Payment processing for user apps |
| Self-hosted runner (Docker + console) | Public flow designer |
| App publishing at automaton.monster/username/app | Team collaboration |
| Persistent user data (volumes) | White-label features |
| Basic app analytics (visits) | Custom domains |

Flows exist internally to power steps, but users interact only with the app builder.

---

## Hosting Architecture

### Your Infrastructure (Hetzner)

| Component | Server | Cost |
|-----------|--------|------|
| Automaton Studio + API | CPX21 or existing | ~€8.50/mo |
| Hosted runners | CPX31 (4 vCPU, 8GB) | ~€15/mo |
| **Total** | | **~€23.50/mo** |

### Runner Container Lifecycle (Free Tier)

```
User request arrives
    ↓
Container exists and running? → Execute immediately
    ↓ (no)
Container exists but stopped? → Start (1-3 sec), execute
    ↓ (no)
No container? → Create from base image + mount user volume (5-15 sec), execute
    ↓
Reset idle timer
    ↓
After 1 hour idle → Delete container, preserve volume
```

### User Data Persistence

- Each user gets a Docker volume: `user-{id}-data`
- Mounted at `/app/data` in their container
- Survives container deletion
- Size limited by tier

---

## AI Token System

Users need AI to generate apps. Rather than requiring users to bring their own API keys (which causes 70%+ drop-off), Automaton provides AI tokens included with each tier.

### Model Pricing Reference

| Model | Input (per 1M tokens) | Output (per 1M tokens) | Blended Estimate |
|-------|----------------------|------------------------|------------------|
| GPT-4o-mini | $0.15 | $0.60 | ~$0.40/1M |
| Claude Haiku | $0.25 | $1.25 | ~$0.80/1M |
| GPT-4o | $2.50 | $10.00 | ~$7/1M |
| Claude Sonnet | $3.00 | $15.00 | ~$10/1M |
| Claude Opus | $15.00 | $75.00 | ~$50/1M |

### Token Cost Per App Generation

| App Complexity | Tokens Used | Cost (GPT-4o-mini) | Cost (GPT-4o) |
|----------------|-------------|--------------------| --------------|
| Simple (form, basic logic) | ~5K | $0.002 | $0.035 |
| Medium (dashboard, API calls) | ~15K | $0.006 | $0.10 |
| Complex (multi-page, auth) | ~40K | $0.016 | $0.28 |
| Iteration/edit | ~3K | $0.001 | $0.02 |

### Model Availability by Tier

| Tier | Available Models |
|------|------------------|
| Free | GPT-4o-mini, Claude Haiku |
| Starter | + GPT-4o, Claude Sonnet |
| Pro | + Claude Opus |
| BYOK (any tier) | Any model via user's own API key |

Free users get capable but cost-effective models. Premium models require paid tier or BYOK.

### Token Usage UX

Display usage clearly in the UI:

```
┌─────────────────────────────────────┐
│  AI Tokens: 67,234 / 100,000        │
│  ████████████░░░░░░░░  67%          │
│                                     │
│  Resets in 12 days                  │
│  [Add your own API key] [Upgrade]   │
└─────────────────────────────────────┘
```

**When running low:**
> "You have ~15K tokens remaining. This app might use ~10K. Continue?"

**When exhausted:**
> "You've used your free tokens this month. Add your own API key (free, unlimited) or upgrade to Starter for 1M tokens/month."

### Implementation Reference

```csharp
public class TokenUsage
{
    public string UserId { get; set; }
    public int MonthlyAllowance { get; set; }  // Based on tier
    public int UsedThisMonth { get; set; }
    public DateTime ResetDate { get; set; }
    public bool HasOwnApiKey { get; set; }
}

public async Task<bool> CanExecute(string userId, int estimatedTokens)
{
    var usage = await GetUsage(userId);
    
    if (usage.HasOwnApiKey)
        return true;  // No limits with BYOK
    
    return (usage.UsedThisMonth + estimatedTokens) <= usage.MonthlyAllowance;
}

public async Task RecordUsage(string userId, int inputTokens, int outputTokens)
{
    var usage = await GetUsage(userId);
    usage.UsedThisMonth += inputTokens + outputTokens;
    await SaveUsage(usage);
}
```

---

## Tier Structure

| | Free | Starter ($7/mo) | Pro ($15/mo) |
|---|------|-----------------|--------------|
| **AI tokens/month** | 100K | 1M | 5M |
| **AI models** | 4o-mini, Haiku | + 4o, Sonnet | + Opus |
| **Bring your own key** | ✓ Unlimited | ✓ Unlimited | ✓ Unlimited |
| Published apps | 1 | 5 | Unlimited |
| Storage | 100MB | 500MB | 2GB |
| Container idle timeout | 1 hour | 24 hours | Never (always on) |
| Cold start | Yes (5-15 sec) | Yes (1-3 sec) | No |
| Execution timeout | 30 sec | 60 sec | 120 sec |
| Requests/day | 100 | 1,000 | 10,000 |
| "Powered by Automaton" badge | Yes | No | No |
| Self-hosted runner | ✓ | ✓ | ✓ |

**Self-hosted runner is always free and unlimited** — this is the key differentiator.

**Bring Your Own Key (BYOK)** — Available on all tiers for users who want unlimited generations or access to specific models.

---

## User Journey

### Path A: Hosted (majority of users)

1. Sign up (email or OAuth)
2. "Describe your app" → AI generates frontend + Python steps
3. Preview in browser (using hosted runner)
4. Click "Publish" → Live at `automaton.monster/username/myapp`
5. Share link, get visitors
6. Hit limits → Upgrade to paid or deploy own runner

### Path B: Self-hosted (technical users)

1. Sign up
2. Create app with AI
3. Download runner (Docker or console)
4. Configure runner with API key
5. Publish app → Backend runs on their infrastructure
6. Full control, no limits, free forever

---

## Technical Checklist

### Must Complete Before Launch

- [ ] **Container orchestrator** — Spin up/down containers per user, mount volumes, enforce limits
- [ ] **Idle timeout manager** — Background job to delete containers after inactivity
- [ ] **Volume management** — Create, size-limit, and clean up user volumes
- [ ] **Cold start UX** — Loading screen: "Starting your app environment..."
- [ ] **Request queuing** — Handle bursts when container is starting
- [ ] **Resource limits** — Memory, CPU, execution time per container
- [ ] **Health checks** — Detect and restart unhealthy containers
- [ ] **Runner base image** — Minimal Python image with common packages
- [ ] **Rate limiting** — Per-user request limits
- [ ] **Basic analytics** — Visit counter per app
- [ ] **AI token tracking** — Track usage per user, enforce limits, monthly reset
- [ ] **AI token UI** — Display remaining tokens, warnings when low
- [ ] **BYOK support** — Allow users to add their own API keys
- [ ] **Model selection** — Restrict models by tier, default to cost-effective option

### Should Complete Before Launch

- [ ] **Polished onboarding** — Guided first-app creation
- [ ] **Example apps** — 3-5 demos showing capabilities
- [ ] **Error handling** — Clear messages when runner fails, times out, etc.
- [ ] **Runner install docs** — Bulletproof Docker one-liner and console setup
- [ ] **Public app gallery** — Optional listing for users who want visibility

### Can Wait

- [ ] Custom domains
- [ ] Team accounts
- [ ] App versioning / rollback
- [ ] Advanced analytics

---

## Launch Strategy

### Phase 1: Private Alpha (2-4 weeks)

- Invite 10-20 people personally
- Watch them use it, note every friction point
- Fix critical issues
- **Goal:** 10 published apps that aren't yours

### Phase 2: Soft Launch (2-4 weeks)

Post to targeted communities:

- **r/selfhosted** — "AI app builder where you own the backend"
- **r/webdev** — "Built a tool to generate full-stack apps with AI"
- **IndieHackers** — Share your journey
- **Dev.to / Hashnode** — Write about the architecture

Respond to every comment, help every user.

**Goal:** 50 signups, 20 published apps

### Phase 3: Public Launch

- Show HN post (once you have social proof)
- Product Hunt (optional, diminishing returns)

**Goal:** Validate if people will pay

---

## Success Metrics

### Validation Phase (First 30 Days)

| Metric | Target |
|--------|--------|
| Signups | 50-100 |
| Apps created | 30+ |
| Apps published | 15+ |
| Users who return after day 1 | 30%+ |
| Self-hosted runner installs | 5+ |

### Revenue Phase (Months 2-3)

| Metric | Target |
|--------|--------|
| Paying customers | 5-10 |
| MRR | €35-150 |
| Churn | <10%/month |

---

## Messaging Framework

### Homepage Headline

> "Build web apps by describing them. Run them anywhere."

### Subheadline

> "AI generates your frontend and Python backend. Host on our servers for free, or deploy to your own infrastructure for complete control."

### Key Differentiators

1. **Speed** — "From idea to live app in minutes"
2. **Ownership** — "Your code, your data, your infrastructure"
3. **No lock-in** — "Export anytime, self-host forever"
4. **Free to start** — "No credit card required"

---

## Competitive Positioning

### How Automaton Differs from Lovable/Bolt

| Aspect | Lovable | Automaton |
|--------|---------|-----------|
| Backend | Managed Supabase | User-hosted Python runners |
| Infrastructure | Fully managed | User brings their own (or uses hosted) |
| Lock-in | Can't disconnect once connected | Full portability, export anytime |
| Backend flexibility | Supabase edge functions only | Python/PowerShell/Batch, any packages |
| Data sovereignty | On Lovable's infrastructure | On user's infrastructure |
| Ongoing cost | Usage-based cloud billing | Self-hosted = free forever |

### Target Audiences

**Primary (launch):**
- Indie hackers and hobbyists who want to build quickly
- Developers curious about AI app generation
- Privacy-conscious users who want data control

**Secondary (growth):**
- Small teams / freelancers wanting cheap, controlled automation
- MSPs / agencies wanting white-label solutions
- Companies needing on-prem execution for compliance

---

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| Users abuse free compute (crypto mining, etc.) | Resource limits, execution timeouts, monitoring |
| Container orchestration is complex | Start simple (Docker CLI), evolve to Docker Swarm/K8s if needed |
| AI generates broken apps | Good error messages, easy iteration, example prompts |
| No one wants to self-host | That's fine — hosted tier generates revenue |
| Competitors copy the model | Execution speed matters; first-mover in this niche |
| AI costs spiral with paid users using premium models | Default to 4o-mini, make premium opt-in, encourage BYOK |
| Token limits frustrate users | Clear UI showing usage, generous limits, easy upgrade path |

---

## Budget Summary

### Monthly Costs at Launch

| Item | Cost |
|------|------|
| Hetzner runners (CPX31) | €15 |
| Hetzner API server (if separate) | €8.50 |
| Domain | ~€1 |
| AI API costs (see below) | €5-30 |
| **Total** | **~€30-55/month** |

### AI Cost Projections

**Worst-case scenario (100 free users, all max out 100K tokens):**
```
100 users × 100K tokens = 10M tokens
10M tokens × $0.40/1M (GPT-4o-mini) = $4/month
```

**More realistic scenario (100 free users, 30% active, average 50K tokens):**
```
30 active users × 50K tokens = 1.5M tokens = ~$0.60/month
```

**With paid users (10 Starter users averaging 500K tokens):**
```
10 users × 500K tokens = 5M tokens
5M tokens × $0.40/1M = $2/month (GPT-4o-mini)
5M tokens × $7/1M = $35/month (if all use GPT-4o)
```

**Key insight:** AI costs are very manageable with cheaper models. The real cost driver is if paid users heavily use premium models (GPT-4o, Claude Sonnet). Consider:
- Default to GPT-4o-mini for all generations
- Let users opt-in to premium models (consumes tokens faster)
- BYOK users cost you nothing

**Break-even:** 4-8 Starter customers or 2-4 Pro customers (depending on AI usage)

---

## Next Actions

1. **Build container orchestrator** — Core infrastructure for hosted runners
2. **Create 3 example apps** — Demonstrate what's possible
3. **Write runner install docs** — Docker one-liner that just works
4. **Recruit alpha testers** — 10-20 people from your network or communities
5. **Set a launch date** — Accountability helps

---

## Appendix: Container Orchestration Notes

### Docker Commands Reference

```bash
# Create persistent volume for user
docker volume create user-123-data

# Run container with resource limits
docker run -d \
  --name runner-user-123 \
  --memory=256m \
  --cpus=0.25 \
  --network=automaton-net \
  -v user-123-data:/app/data \
  -e USER_ID=123 \
  -e API_KEY=xxx \
  automaton-runner:latest

# Stop container (preserves filesystem)
docker stop runner-user-123

# Remove container (keeps volume)
docker rm runner-user-123

# Check volume size
docker system df -v | grep user-123-data

# Clean up unused volumes (careful!)
docker volume prune
```

### Orchestrator Pseudo-Code

```python
async def handle_request(user_id: str, request: Request):
    container = await get_container(user_id)
    
    if container and container.status == "running":
        return await forward_request(container, request)
    
    if container and container.status == "exited":
        await container.start()
        await wait_for_healthy(container, timeout=5)
        return await forward_request(container, request)
    
    # No container exists - create with existing volume
    volume = await get_or_create_volume(user_id)
    container = await create_container(user_id, volume)
    await wait_for_healthy(container, timeout=15)
    return await forward_request(container, request)

async def cleanup_idle_containers():
    """Runs every 10 minutes via background job"""
    containers = await get_all_runner_containers()
    
    for container in containers:
        idle_time = now() - container.last_request_time
        user_tier = await get_user_tier(container.user_id)
        
        if user_tier == "free" and idle_time > timedelta(hours=1):
            await container.remove()  # Delete container, keep volume
        elif user_tier == "starter" and idle_time > timedelta(hours=24):
            await container.stop()  # Just stop, keep container
        # Pro tier: never auto-stop
```

---

*Document created: January 2026*
*Last updated: January 2026*
