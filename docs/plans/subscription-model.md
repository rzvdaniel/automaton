# Automaton Studio Subscription Model

## Executive Summary

This document outlines a comprehensive subscription model for Automaton Studio that integrates three key revenue-generating features:

1. **Platform AI Credits** - AI assistance for building apps, flows, and custom steps
2. **Cloud Runners** - Managed runner instances on shared infrastructure
3. **Core Platform Features** - Web apps, flows, projects, publishing, and team collaboration

The model follows industry best practices from platforms like Vercel, GitHub, Replit, and Cursor, with a focus on:
- **Low barrier to entry** - Generous free tier to attract users
- **Clear value proposition** - Obvious benefits at each tier
- **Add-on flexibility** - Purchase additional AI credits or Cloud Runners as needed
- **Enterprise scalability** - Custom solutions for large organizations

### Related Documents

This plan integrates and expands upon:
- **Platform AI Credits**: [platform-provided-ai-credits.md](./platform-provided-ai-credits.md)
- **Cloud Runners**: [cloud-runner-deployment-plan-v2.md](./cloud-runner-deployment-plan-v2.md)

---

## Subscription Tiers Overview

### Quick Comparison Table

| Feature | Free | Pro | Enterprise |
|---------|------|-----|------------|
| **Price** | $0/mo | $20/mo | Custom |
| **Published Apps** | 1 public app | 10 apps (public/private) | Unlimited |
| **Active Flows** | 5 flows | 100 flows | Unlimited |
| **Custom Steps** | 10 steps | 100 steps | Unlimited |
| **Projects** | 1 project (3 members) | 10 projects (10 members each) | Unlimited |
| **Flow Executions** | 100/month | 10,000/month | Unlimited |
| **AI Credits (Monthly)** | 10,000 tokens | 500,000 tokens | 5,000,000 tokens |
| **AI Add-on Credits** | ❌ Not available | ✅ $10 per 250k tokens | ✅ Custom pricing |
| **Cloud Runners (Included)** | 1 runner (0.25 CPU, 256MB) | 3 runners (0.5 CPU, 512MB) | 10+ runners (custom specs) |
| **Cloud Runner Add-ons** | ❌ Not available | ✅ $5/mo per runner | ✅ Custom pricing |
| **BYOK (Bring Your Own AI Keys)** | ✅ Unlimited | ✅ Unlimited | ✅ Unlimited |
| **Self-hosted Runners** | ✅ Unlimited | ✅ Unlimited | ✅ Unlimited |
| **Storage per App** | 10MB | 100MB | 1GB+ |
| **API Rate Limit** | 100/hour | 1,000/hour | 10,000/hour |
| **Analytics Retention** | 7 days | 90 days | 365 days |
| **Support** | Community | Email (48h) | Priority + Dedicated |
| **SSO** | ❌ | ❌ | ✅ SAML/OAuth |
| **Custom Domain** | ❌ | ✅ | ✅ |
| **White-labeling** | ❌ | ❌ | ✅ |

---

## Tier Details

### Free Tier ($0/month)

**Target Audience**: Hobbyists, students, developers evaluating the platform

**Core Features**:
- 1 published web app (public access only)
- 5 active flows (unlimited drafts)
- 10 custom steps
- 1 project with up to 3 team members
- 100 flow executions per month
- Community support (Discord, forums)

**AI Features**:
- 10,000 AI tokens per month (~25-50 AI chat interactions)
- Platform-managed AI keys (Anthropic/Google)
- Standard models (Claude Haiku, Gemini Flash)
- BYOK option for unlimited AI usage
- Rate limit: 100 AI requests per day

**Cloud Infrastructure**:
- 1 included Cloud Runner
  - 0.25 CPU cores
  - 256MB RAM
  - EU or US region selection
- Unlimited self-hosted runners (bring your own infrastructure)

**Storage & Bandwidth**:
- 10MB per published app
- 100MB total storage
- 1GB bandwidth per month

**Limitations**:
- No private apps (published apps are public)
- No custom domains
- 7 days analytics retention
- No add-on purchases
- No team member invitations beyond project limit

**Upgrade Path**: 
When users exceed limits, show contextual upgrade prompts:
- "You've reached 5 flows. Upgrade to Pro for 100 flows."
- "8,500 of 10,000 AI tokens used. Upgrade for 500k tokens or add your own API key."
- "1 Cloud Runner active. Upgrade to Pro for 3 runners + ability to add more."

---

### Pro Tier ($20/month)

**Target Audience**: Professional developers, small teams, indie app builders

**Core Features**:
- 10 published apps (public or private with auth)
- 100 active flows
- 100 custom steps
- 10 projects with up to 10 team members each
- 10,000 flow executions per month
- Email support (48-hour response time)

**AI Features**:
- 500,000 AI tokens per month (~1,250-2,500 AI interactions)
- Platform-managed AI keys with model selection
  - Choose between Claude Sonnet/Haiku, Gemini Pro/Flash, GPT-4o
- Advanced AI features (code review, test generation)
- BYOK option for unlimited AI usage
- Rate limit: 1,000 AI requests per day
- 50% credit rollover (unused tokens carry to next month, max 50%)

**AI Add-on Credits**:
- Purchase additional tokens: **$10 per 250,000 tokens**
- No expiration on add-on credits
- Automatically used when monthly quota is exhausted

**Cloud Infrastructure**:
- 3 included Cloud Runners (per account, not per project)
  - 0.5 CPU cores each
  - 512MB RAM each
  - Multi-region deployment
- Unlimited self-hosted runners

**Cloud Runner Add-ons**:
- Purchase additional Cloud Runners: **$5/month per runner**
- Same specs as included runners (0.5 CPU, 512MB RAM)
- Can deploy in any available region
- Managed updates and monitoring

**Storage & Bandwidth**:
- 100MB per published app
- 2GB total storage
- 50GB bandwidth per month

**Additional Pro Features**:
- Custom domains for published apps
- Private apps with authentication
- Advanced analytics (90 days retention)
- Scheduled flow executions
- Webhook triggers
- Priority job queue
- Export flows/apps as templates

**Upgrade Path**:
"Need more Cloud Runners or higher limits? Enterprise plans start at $100/month with custom configurations."

---

### Enterprise Tier (Custom Pricing, starts at $100/month)

**Target Audience**: Large teams, organizations, businesses with specific requirements

**Core Features**:
- Unlimited published apps
- Unlimited flows
- Unlimited custom steps
- Unlimited projects with unlimited team members
- Unlimited flow executions
- Dedicated account manager
- Priority support (4-hour response SLA)
- Custom SLA options

**AI Features**:
- 5,000,000+ AI tokens per month (effectively unlimited for most use cases)
- Access to all AI models including latest releases
- Custom AI model fine-tuning (on roadmap)
- Dedicated AI infrastructure option
- BYOK with fallback to platform credits
- Rate limit: 10,000+ AI requests per day (custom)
- Full credit rollover

**AI Add-on Credits**:
- Custom pricing for additional tokens
- Volume discounts available
- Dedicated AI provider accounts (optional)

**Cloud Infrastructure**:
- 10+ included Cloud Runners (configurable)
  - Custom CPU/RAM configurations (up to 2 CPU, 2GB RAM per runner)
  - Dedicated server option
  - Geographic distribution
- Unlimited self-hosted runners
- VPN/Private network connectivity

**Cloud Runner Add-ons**:
- Custom pricing for additional runners
- Enterprise-grade specs available
- SLA guarantees
- Automatic scaling option

**Storage & Bandwidth**:
- 1GB+ per published app (configurable)
- 50GB+ total storage
- 500GB+ bandwidth per month
- CDN integration

**Enterprise-Only Features**:
- **SSO Integration** (SAML, OAuth, Azure AD, Okta)
- **White-labeling** (custom branding, logo, domain)
- **Audit logs** (comprehensive activity tracking)
- **Advanced permissions** (role-based access control)
- **API access** (full platform API)
- **Data residency** (choose data storage location)
- **Dedicated tenant** (isolated infrastructure option)
- **Custom integrations** (webhooks, APIs, connectors)
- **Training & onboarding** (dedicated sessions)
- **Contract terms** (annual, multi-year discounts)

**Pricing Examples**:
- **Small Team** (10 users, 50 flows, 20 runners): ~$100-150/month
- **Medium Business** (50 users, 200 flows, 100 runners): ~$400-600/month
- **Large Enterprise** (200+ users, unlimited usage, dedicated infrastructure): ~$1,500-3,000/month

---

## Add-on Pricing

### AI Credit Add-ons

Available for **Pro** and **Enterprise** tiers only.

| Package | Tokens | Price | Cost per 1k tokens |
|---------|--------|-------|-------------------|
| Starter Pack | 250,000 | $10 | $0.04 |
| Growth Pack | 1,000,000 | $35 | $0.035 |
| Business Pack | 5,000,000 | $150 | $0.03 |

**Add-on Credits Behavior**:
- ✅ No expiration (carry over indefinitely)
- ✅ Used after monthly quota is exhausted
- ✅ Stack with multiple purchases
- ✅ Shared across all projects in account
- ✅ Transparent usage tracking in dashboard

**Use Cases for Add-ons**:
- Seasonal spikes (e.g., hackathon, product launch)
- One-time large project without upgrading tier
- Buffer for teams that occasionally exceed quota

### Cloud Runner Add-ons

Available for **Pro** and **Enterprise** tiers only.

| Tier | Price | Specs | Regions |
|------|-------|-------|---------|
| Standard | $5/mo | 0.5 CPU, 512MB RAM | All available |
| Performance | $12/mo | 1.0 CPU, 1GB RAM | All available |
| Enterprise | Custom | 2.0+ CPU, 2GB+ RAM | Custom |

**Runner Add-on Behavior**:
- ✅ Billed monthly (prorated for partial months)
- ✅ Can be added/removed anytime
- ✅ Same management as included runners
- ✅ Multi-region deployment
- ✅ Automatic health monitoring

**Use Cases for Add-ons**:
- Geographic distribution (runners in multiple regions)
- High-availability setups (failover runners)
- Load balancing (distribute flow executions)
- Development vs. Production separation

---

## Feature Details

### Web Apps

**Publishing & Hosting**:
- **Free**: 1 public app, automaton.studio/web/{slug}
- **Pro**: 10 apps, custom domains supported
- **Enterprise**: Unlimited apps, white-label domains

**Access Control**:
- **Free**: Public only (no authentication)
- **Pro**: Public or Private (requires Automaton login)
- **Enterprise**: SSO integration, custom auth

**Progressive Web Apps (PWA)**:
- ✅ Available on all tiers
- Installable on mobile/desktop
- Offline support configuration

### Flows

**Execution Limits**:
- **Free**: 100 executions/month
- **Pro**: 10,000 executions/month
- **Enterprise**: Unlimited

**Scheduling**:
- **Free**: Manual execution only
- **Pro**: Cron-based scheduling
- **Enterprise**: Advanced scheduling + priority queue

**Triggers**:
- **Free**: Manual triggers only
- **Pro**: Webhooks, schedules, API calls
- **Enterprise**: All triggers + custom integrations

### Projects

**Team Collaboration**:
- **Free**: 1 project, 3 members max
- **Pro**: 10 projects, 10 members per project
- **Enterprise**: Unlimited projects, unlimited members

**Permissions**:
- **Free**: Basic (Owner, Member)
- **Pro**: Role-based (Owner, Admin, Member, Viewer)
- **Enterprise**: Custom roles + granular permissions

**Shared Resources**:
- All tiers: Flows, Apps, Custom Steps can be shared within projects
- Pro/Enterprise: Shared variables, secrets, AI keys

### Custom Steps

**Language Support**:
- All tiers: Python, PowerShell, Bash
- Enterprise: Custom runtime environments

**Testing**:
- All tiers: Built-in test runner with mock variables
- Pro/Enterprise: Integration testing across flows

**Distribution**:
- **Free**: Private to account
- **Pro**: Share within organization
- **Enterprise**: Marketplace publishing (roadmap)

---

## Migration & Grandfathering

### Existing Users

**Default Assignment**:
1. All existing users → **Free tier**
2. Historical usage data migrated
3. Existing published apps, flows, runners remain active

**Grace Period** (30 days):
- Users exceeding Free tier limits get 30-day grace period
- During grace: No hard limits, but upgrade prompts shown
- After grace: Soft limits enforced (can't create new resources until compliance)

**Grandfathering**:
- Users who created 5+ flows before launch → Pro tier for 3 months free
- Early adopters (first 100 users) → Pro tier for 6 months free
- Active contributors (PRs, bug reports) → Pro tier for 12 months free

### New Users

**Onboarding**:
1. **Day 0**: Free tier with guided tour
2. **Day 1**: Email with tips + showcase apps
3. **Day 3**: Highlight AI features + first flow tutorial
4. **Day 7**: Usage report + upgrade benefits
5. **Day 14**: Case studies + success stories
6. **Day 30**: Special upgrade offer (20% off first month)

**Trial Offers**:
- **Pro Trial**: 14 days free Pro trial for new signups
- **No credit card required** during trial
- Auto-downgrade to Free tier after trial (not auto-charge)

---

## Billing & Payment

### Payment Methods

- Credit/Debit cards (Stripe)
- PayPal
- Wire transfer (Enterprise, annual plans only)
- Purchase orders (Enterprise only)

### Billing Cycles

- **Monthly**: Billed on signup date each month
- **Annual**: 2 months free (16% discount)
  - Pro: $200/year (vs. $240)
  - Enterprise: Custom discounts

### Usage-Based Billing

**Flow Executions** (Pro tier):
- Included: 10,000/month
- Overage: $0.001 per execution ($1 per 1,000)
- Capped at $50/month overage (automatic upgrade to Enterprise suggested)

**Bandwidth** (Pro tier):
- Included: 50GB/month
- Overage: $0.10 per GB
- Capped at $20/month overage

### Invoicing

- **Pro**: Email receipts
- **Enterprise**: Custom invoicing, NET 30 terms available

---

## Quota Management

### Monthly Quota Reset

**Reset Schedule**:
- Resets on the same day each month as subscription start date
- UTC midnight
- Email notification 3 days before reset
- Dashboard countdown widget

**Quota Behaviors**:

| Resource | Reset | Rollover | Overage |
|----------|-------|----------|---------|
| AI Credits (Free) | Monthly | ❌ No | Hard stop |
| AI Credits (Pro) | Monthly | ✅ 50% max | Use add-ons |
| AI Add-on Credits | Never | ✅ Forever | N/A |
| Flow Executions (Free) | Monthly | ❌ No | Hard stop |
| Flow Executions (Pro) | Monthly | ❌ No | Pay per use ($1/1k) |
| Cloud Runners | Monthly | N/A | N/A |
| Storage | Never | N/A | Soft limit |
| Bandwidth | Monthly | ❌ No | Pay per use |

### Quota Warnings

**Warning Thresholds**:
- 50% used: Informational email
- 75% used: Dashboard badge + email
- 90% used: Modal popup + email + SMS (opt-in)
- 100% used: Blocking modal with upgrade CTA

**Soft vs. Hard Limits**:
- **Hard limits**: AI credits (Free), Flow executions (Free)
  - Feature completely blocked when exceeded
  - "Upgrade or wait for reset" message
- **Soft limits**: Storage, published apps
  - Can't create new resources
  - Existing resources continue to function

---

## Analytics & Reporting

### Usage Dashboard

Available to all tiers (retention varies).

**Metrics Tracked**:
- AI token usage (daily/weekly/monthly charts)
- Flow execution counts
- Cloud Runner utilization (CPU, RAM, uptime)
- Published app traffic (views, unique visitors)
- API call volumes
- Storage consumption
- Bandwidth usage

**Data Retention**:
- **Free**: 7 days
- **Pro**: 90 days
- **Enterprise**: 365 days + export to data warehouse

### Cost Tracking

**Pro/Enterprise**:
- Projected monthly bill based on current usage
- Cost breakdown by feature (AI, runners, bandwidth)
- Budget alerts (email when approaching set limit)
- Historical spending charts

### Reports

**Pro**:
- Monthly usage summary (email)
- Quarterly trends report

**Enterprise**:
- Custom reporting (schedule, format, recipients)
- API access to raw usage data
- Data export (CSV, JSON, SQL)

---

## Subscription Management

### Upgrade

**Free → Pro**:
- Instant upgrade
- Prorated billing (pay for remainder of month)
- All limits immediately increased
- Add-on marketplace unlocked

**Pro → Enterprise**:
- Contact sales for custom quote
- Migration planning call
- Dedicated onboarding session
- Contract negotiation

### Downgrade

**Pro → Free**:
- Takes effect at end of current billing period
- 7-day warning before resources are limited
- Opportunity to export data
- Exceeding resources are disabled (not deleted)
  - E.g., 10 apps → disable 9, keep most recent
  - User chooses which to keep active

**Enterprise → Pro**:
- Rare, but supported
- 30-day transition period
- Data export assistance
- Custom plan to fit within Pro limits

### Cancellation

**Immediate Cancellation**:
- No refunds for current period
- Access until end of billing cycle
- Data export available for 30 days
- Automatic downgrade to Free tier

**Account Deletion**:
- 30-day retention period (recoverable)
- After 30 days: Permanent deletion
- Export all data before deletion
- Complies with GDPR right to erasure

---

## Competitive Analysis

### How Automaton Studio Compares

| Platform | Free Tier | Paid Tier | Strengths | Weaknesses |
|----------|-----------|-----------|-----------|------------|
| **Vercel** | Generous (hobbyists) | $20/seat | Best-in-class deployment | No AI features |
| **Replit** | Limited | $20/mo | Great IDE experience | No flow automation |
| **GitHub Codespaces** | 60 hours/mo | $0.18/hour | Deep Git integration | No visual flow designer |
| **Retool** | 5 users free | $10/user/mo | Powerful UI builder | Enterprise-focused |
| **n8n Cloud** | 5,000 executions | $20/mo | Strong integrations | Limited AI support |
| **Automaton** | **1 app, 5 flows, 1 runner** | **$20/mo** | **AI + Flows + Apps + Runners** | New platform |

**Automaton Studio Differentiation**:
1. ✅ **All-in-one**: Apps + Flows + AI in one platform
2. ✅ **Platform AI**: No need to bring AI keys initially
3. ✅ **Cloud Runners**: Managed execution without infrastructure
4. ✅ **Generous free tier**: Real value, not just trial
5. ✅ **Flexible add-ons**: Buy what you need, when you need it

---

## Revenue Projections

### Assumptions

- 10,000 users after 12 months
- 80% Free, 15% Pro, 5% Enterprise
- 30% of Pro users buy AI add-ons (avg $15/mo)
- 40% of Pro users buy runner add-ons (avg 2 runners = $10/mo)

### Year 1 Revenue Forecast

| Revenue Source | Users | ARPU | MRR | ARR |
|----------------|-------|------|-----|-----|
| Pro Subscriptions | 1,500 | $20 | $30,000 | $360,000 |
| AI Add-ons | 450 | $15 | $6,750 | $81,000 |
| Runner Add-ons | 600 | $10 | $6,000 | $72,000 |
| Enterprise | 500 | $150 | $75,000 | $900,000 |
| **Total** | **10,000** | **$11.78** | **$117,750** | **$1,413,000** |

### Operating Costs (Monthly)

| Cost Item | Monthly | Notes |
|-----------|---------|-------|
| Platform AI (Free users) | $4,000 | 8,000 users × 50% usage × $0.015/1k tokens |
| Platform AI (Pro users) | $3,375 | 1,500 users × 40% usage × $0.015/1k tokens |
| Cloud Runners (Free) | $5,840 | 8,000 runners × $0.73/runner |
| Cloud Runners (Pro included) | $3,285 | 4,500 runners × $0.73/runner |
| Cloud Runners (Pro add-ons) | $876 | 1,200 runners × $0.73/runner |
| Cloud Runners (Enterprise) | $3,650 | 5,000 runners × $0.73/runner |
| Infrastructure | $5,000 | Hosting, CDN, databases |
| Payment Processing (3%) | $3,533 | 3% of MRR |
| **Total Costs** | **$29,559** | |
| **Gross Profit** | **$88,191** | |
| **Margin** | **74.9%** | |

### Break-even Analysis

**Monthly break-even**: ~$30,000 MRR
- ~1,500 Pro users at $20/mo
- Or ~200 Enterprise users at $150/mo
- Or mix of tiers

**Expected time to break-even**: 4-6 months after launch

---

## Implementation Roadmap

### Phase 1: Foundation (Months 1-2)

**Database & Entities**:
- [ ] Create `UserSubscription` entity
- [ ] Migrate existing users to Free tier
- [ ] Add subscription tier checks to all services

**Billing Integration**:
- [ ] Set up Stripe account
- [ ] Implement Stripe Checkout
- [ ] Build webhook handlers (subscription.created, subscription.updated, etc.)
- [ ] Create billing portal

**AI Credits** (from [platform-provided-ai-credits.md](./platform-provided-ai-credits.md)):
- [ ] Implement `AiUsage`, `PlatformAiKey` entities
- [ ] Build `AiCreditService`, `PlatformAiKeyManager`
- [ ] Update AI Chat panels with quota display

**Cloud Runners** (from [cloud-runner-deployment-plan-v2.md](./cloud-runner-deployment-plan-v2.md)):
- [ ] Implement `CloudServer`, `CloudRunner` entities
- [ ] Build provisioning flows
- [ ] Create Cloud Runner management UI

### Phase 2: Subscription UI (Month 2)

**Subscription Pages**:
- [ ] `/subscription` - Current plan, usage, upgrade/downgrade
- [ ] `/subscription/upgrade` - Tier comparison, checkout
- [ ] `/subscription/billing` - Payment methods, invoices
- [ ] `/subscription/usage` - Detailed usage analytics

**Dashboard Updates**:
- [ ] Add subscription badge to navbar
- [ ] Show quota warnings in relevant pages
- [ ] Add "Upgrade" CTAs when limits approached

**Add-on Marketplace**:
- [ ] AI Credits store
- [ ] Cloud Runners store
- [ ] Shopping cart + checkout

### Phase 3: Quota Enforcement (Month 3)

**Quota Checks**:
- [ ] Flow execution limits
- [ ] AI token limits
- [ ] Published app limits
- [ ] Cloud Runner limits
- [ ] Storage limits

**Graceful Degradation**:
- [ ] Informative error messages
- [ ] Contextual upgrade prompts
- [ ] Grace period for downgrades

### Phase 4: Analytics & Reporting (Month 3)

**Usage Dashboard**:
- [ ] AI usage charts
- [ ] Flow execution graphs
- [ ] Cloud Runner metrics
- [ ] Cost projections

**Reports**:
- [ ] Monthly email summaries
- [ ] CSV export functionality
- [ ] API access (Enterprise)

### Phase 5: Enterprise Features (Month 4-5)

**SSO Integration**:
- [ ] SAML support
- [ ] OAuth (Azure AD, Okta, Google)
- [ ] JIT provisioning

**White-labeling**:
- [ ] Custom branding settings
- [ ] Logo upload
- [ ] Custom domain configuration

**Advanced Permissions**:
- [ ] Role-based access control
- [ ] Custom roles
- [ ] Audit logs

### Phase 6: Optimization (Month 6+)

**Cost Optimization**:
- [ ] AI model routing (cheap models for simple tasks)
- [ ] Response caching
- [ ] Runner auto-scaling

**Conversion Optimization**:
- [ ] A/B test pricing
- [ ] Improve upgrade prompts
- [ ] Onboarding improvements

**Feature Development**:
- [ ] New AI features (code review, test generation)
- [ ] Marketplace (templates, custom steps)
- [ ] Advanced analytics

---

## Marketing & Sales

### Positioning

**Tagline**: "Build, Automate, Deploy - All in One Platform"

**Value Propositions by Tier**:
- **Free**: "Start building AI-powered apps in minutes - no credit card required"
- **Pro**: "Ship faster with AI assistance and managed infrastructure"
- **Enterprise**: "Scale your automation with enterprise-grade security and support"

### Go-to-Market Strategy

**Free Tier as Lead Gen**:
1. Generous limits attract developers
2. Showcase real value (not just trial)
3. Collect feedback and testimonials
4. Build community (Discord, forums)

**Pro Tier Conversion Tactics**:
1. Contextual upgrade prompts when hitting limits
2. Show time/cost savings from AI features
3. Case studies from Pro users
4. Limited-time offers (20% off first month)

**Enterprise Outreach**:
1. Direct sales for companies 50+ employees
2. RFP response templates
3. Compliance certifications (SOC 2, ISO 27001)
4. Custom POCs and trials

### Content Marketing

**Blog Posts**:
- "Building your first flow in 5 minutes"
- "How AI Chat accelerates custom step development"
- "Deploying production apps on Cloud Runners"
- "Automaton vs. Zapier/n8n/Retool"

**Video Tutorials**:
- Quick start series
- Feature deep dives
- Customer success stories

**Developer Advocacy**:
- Conference talks
- Hackathon sponsorships
- Open source contributions

---

## Risk Mitigation

### Technical Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| AI cost overruns (abuse) | Medium | High | Aggressive rate limiting, anomaly detection, auto-suspend |
| Cloud Runner capacity issues | Medium | Medium | Multi-provider strategy, auto-scaling, waitlist system |
| Payment processing failures | Low | High | Stripe redundancy, retry logic, manual payment option |
| Quota calculation bugs | Medium | Medium | Extensive testing, gradual rollout, manual override capability |

### Business Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Low Free→Pro conversion | High | High | A/B test pricing, improve value props, targeted campaigns |
| Price sensitivity | Medium | Medium | Grandfather early users, offer annual discounts, add-on flexibility |
| Competitor undercutting | Medium | High | Focus on unique value (AI+Flows+Apps), feature velocity |
| Enterprise sales cycle | High | Medium | Hire sales team, improve self-serve, offer shorter contracts |

### Compliance Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| GDPR violations | Low | High | Data residency options, right to erasure, privacy policy |
| PCI DSS (payments) | Low | High | Use Stripe (PCI compliant), no card storage |
| SLA breaches | Medium | Medium | Over-provision infrastructure, monitoring, incident response |

---

## Success Metrics

### North Star Metric

**Monthly Recurring Revenue (MRR)** - Target: $100k MRR by Month 12

### Supporting Metrics

**Acquisition**:
- New signups per month (target: 1,000+)
- Activation rate (first flow created in 7 days: 60%+)
- Cost per acquisition (target: <$50)

**Engagement**:
- Weekly Active Users (WAU) (target: 40% of total users)
- Average flows per user (target: 5+)
- AI feature usage rate (target: 70% of users try AI)

**Retention**:
- Month-over-month retention (target: 80%+)
- Churn rate (target: <5% monthly)
- Net Promoter Score (target: 50+)

**Monetization**:
- Free→Pro conversion rate (target: 10%+)
- Pro→Enterprise conversion rate (target: 3%+)
- Average Revenue Per User (target: $10+)
- Lifetime Value (target: $500+)

### Dashboard KPIs

**Daily**:
- MRR
- New signups
- Active users
- AI/Runner costs

**Weekly**:
- Conversion funnel
- Churn analysis
- Feature usage
- Support tickets

**Monthly**:
- Revenue vs. forecast
- Unit economics
- Cohort analysis
- Product roadmap alignment

---

## FAQ

### General Questions

**Q: Can I start on Free and upgrade later?**
A: Yes! Upgrade anytime from your subscription page. Your limits increase immediately, and billing is prorated.

**Q: What happens to my data if I downgrade?**
A: Your data is never deleted. Resources exceeding Free tier limits are disabled (but preserved) until you upgrade again or manually delete them.

**Q: Are there student discounts?**
A: Yes! Students with a valid .edu email get 50% off Pro tier. Apply via the subscription page.

**Q: Can I use Automaton Studio for commercial projects on the Free tier?**
A: Yes! The Free tier is suitable for small commercial projects. We just ask that you consider upgrading if you're generating revenue.

### AI Credits

**Q: What counts as an AI "token"?**
A: Tokens are the unit of measure for AI processing. Roughly 1 token = 0.75 words. A typical chat interaction uses 200-500 tokens.

**Q: What happens when I run out of AI credits?**
A: On Free tier, AI features are blocked until next month. On Pro, you can purchase add-on credits or add your own AI API key for unlimited usage.

**Q: Do unused AI credits roll over?**
A: Pro tier: 50% rollover (max 50% of monthly quota). Enterprise: Full rollover. Add-on credits never expire.

**Q: Can I use my own AI API keys instead of platform credits?**
A: Yes! All tiers support BYOK (Bring Your Own Key). Use your own keys for unlimited AI usage at your AI provider's rates.

### Cloud Runners

**Q: How many Cloud Runners do I need?**
A: Most users need 1-3 runners. Use multiple runners for: geographic distribution, high availability, or load balancing.

**Q: Can I use self-hosted runners instead of Cloud Runners?**
A: Yes! All tiers support unlimited self-hosted runners. Cloud Runners are purely a convenience feature.

**Q: What regions are Cloud Runners available in?**
A: Currently: EU (Nuremberg), US (Ashburn). More regions coming based on demand.

**Q: Can I upgrade a Cloud Runner's specs?**
A: Not currently. Instead, add a new Performance runner ($12/mo for 1 CPU, 1GB RAM) and migrate your flows.

### Billing

**Q: Do you offer annual plans?**
A: Yes! Annual plans get 2 months free (16% discount). Pay $200/year for Pro instead of $240.

**Q: Can I pay with invoice instead of credit card?**
A: Enterprise customers can pay via invoice with NET 30 terms. Contact sales for details.

**Q: What happens if my payment fails?**
A: We retry 3 times over 7 days. If all fail, your account is downgraded to Free tier (no data loss).

**Q: Do you offer refunds?**
A: We offer prorated refunds within the first 7 days of upgrading. After that, no refunds, but you can cancel anytime.

### Enterprise

**Q: What's the minimum for Enterprise tier?**
A: Typically $100/month, but we can customize. Contact sales for a quote.

**Q: Do you offer on-premise deployments?**
A: Not currently, but it's on the roadmap for Enterprise customers. Join the waitlist.

**Q: Can we get a custom SLA?**
A: Yes! Enterprise customers can negotiate custom SLAs (e.g., 99.9% uptime, 4-hour response time).

---

## Appendix

### Appendix A: Detailed Cost Breakdown

**AI Token Costs** (per 1M tokens):

| Provider | Model | Input | Output | Blended |
|----------|-------|--------|--------|---------|
| Anthropic | Claude Sonnet 4 | $3.00 | $15.00 | ~$9.00 |
| Anthropic | Claude Haiku 4.5 | $1.00 | $5.00 | ~$3.00 |
| Google | Gemini 2.5 Pro | $2.50 | $10.00 | ~$6.25 |
| Google | Gemini Flash | $0.50 | $2.00 | ~$1.25 |

**Blended average** (assuming 50/50 Haiku/Flash for Free, Pro uses premium): **$0.015/1k tokens**

**Cloud Runner Costs** (Hetzner servers):

| Server Type | Monthly | Max Runners | Cost/Runner |
|-------------|---------|-------------|-------------|
| CX21 | €5.83 | 8 | €0.73 |
| CX31 | €10.59 | 15 | €0.71 |
| CX41 | €17.49 | 25 | €0.70 |
| CX51 | €32.49 | 50 | €0.65 |

**Average cost per runner**: **€0.70/month** (~$0.73 USD)

### Appendix B: Subscription Lifecycle Flowchart

```
[User Signs Up]
       |
       v
[Auto-assigned: Free Tier]
       |
       +---> [Uses Platform] ---+
       |                        |
       |                        v
       |                  [Approaches Limits]
       |                        |
       |                        v
       |                  [Upgrade Prompt]
       |                        |
       |         +--------------+---------------+
       |         |                              |
       |         v                              v
       |   [Adds Own Keys/Runners]        [Clicks Upgrade]
       |         |                              |
       |         v                              v
       |   [Continues Free]             [Chooses Tier]
       |                                        |
       |                         +--------------+----------------+
       |                         |                               |
       +<------------------------+                               v
                                 v                        [Contacts Sales]
                          [Selects Pro]                          |
                                 |                               v
                                 v                     [Enterprise Onboarding]
                          [Stripe Checkout]                      |
                                 |                               |
                                 v                               |
                          [Pro Subscription]                     |
                                 |                               |
                     +-----------+-----------+                   |
                     |                       |                   |
                     v                       v                   |
              [Uses Add-ons]          [Upgrades Again] ---------+
                     |                       |
                     v                       v
              [Continues Pro]      [Enterprise Customer]
```

### Appendix C: Sample API Responses

**Get User Subscription**:
```http
GET /api/subscription
Authorization: Bearer {token}

Response 200:
{
  "tier": "Pro",
  "status": "active",
  "currentPeriodStart": "2024-01-01T00:00:00Z",
  "currentPeriodEnd": "2024-02-01T00:00:00Z",
  "quotas": {
    "aiTokens": {
      "monthly": 500000,
      "used": 125000,
      "remaining": 375000,
      "resetDate": "2024-02-01T00:00:00Z"
    },
    "flowExecutions": {
      "monthly": 10000,
      "used": 2340,
      "remaining": 7660,
      "resetDate": "2024-02-01T00:00:00Z"
    },
    "cloudRunners": {
      "included": 3,
      "active": 5,
      "addOns": 2
    },
    "publishedApps": {
      "limit": 10,
      "active": 3,
      "remaining": 7
    }
  },
  "addOns": [
    {
      "type": "aiCredits",
      "tokens": 250000,
      "purchasedAt": "2024-01-15T10:30:00Z",
      "expiresAt": null
    },
    {
      "type": "cloudRunner",
      "quantity": 2,
      "pricePerUnit": 5.00,
      "totalMonthly": 10.00
    }
  ],
  "nextBillingDate": "2024-02-01T00:00:00Z",
  "nextBillingAmount": 45.00
}
```

**Purchase Add-on**:
```http
POST /api/subscription/addons
Authorization: Bearer {token}
Content-Type: application/json

{
  "type": "aiCredits",
  "packageId": "growth-pack"
}

Response 200:
{
  "success": true,
  "transactionId": "tx_abc123",
  "addOn": {
    "type": "aiCredits",
    "tokens": 1000000,
    "price": 35.00,
    "purchasedAt": "2024-01-20T14:22:00Z"
  },
  "newBalance": {
    "aiTokens": 1125000
  }
}
```

---

## Changelog

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 1.0 | 2024-12-20 | Initial draft | Automaton Team |

---

**Document Status**: Draft - Pending Review  
**Next Review Date**: TBD  
**Approvals Required**: Product, Engineering, Finance, Legal
