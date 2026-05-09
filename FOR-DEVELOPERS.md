# FOR-DEVELOPERS.md

This document explains the Automaton project in engaging, plain language. It's not a dry technical reference—it's a narrative about the decisions we made, lessons we learned, pitfalls we hit, and how good engineers think about building complex systems.

**Read this when:**
- You're new to the project and want to understand the "why" behind the architecture
- You're facing a problem and want context on how we've solved similar ones before
- You want to understand the philosophy that guides our code and documentation practices
- You're curious about what surprised us and what we'd tell our past selves

---

## Architecture Overview: Building a Workflow Automation Platform

Imagine you want to build a system where non-technical users can describe what they want in natural language ("Send me a Slack message when this spreadsheet updates"), and it automatically creates a workflow to do it. That's the core of Automaton.

But there's a twist: these workflows don't execute on our servers. They execute *on runners* distributed across your infrastructure. Think of runners as lightweight agents stationed in different places, ready to execute work on demand. This design choice solves a critical problem: **security and control**. Users keep their data and credentials in their own environment, and we just orchestrate the work.

So the architecture has three main parts:

1. **Frontend (Vue.js + TypeScript)** – The user-facing interface where people describe workflows and monitor execution
2. **Backend (ASP.NET Core 9.0)** – The orchestrator; stores workflows, coordinates execution, and communicates with runners
3. **Runners (Cross-platform console app)** – Distributed agents that actually execute the work

Think of it like a **conductor and musicians**:
- The frontend is the composer's notebook where the conductor sketches the piece
- The backend is the conductor, reading the score and directing
- The runners are the musicians, playing the notes on their instruments in their own concert halls

This separation means:
- **Security**: Runners can run anywhere (behind firewalls, in private clouds) because they initiate the connection to the backend, not the other way around
- **Scalability**: You can spin up as many runners as you need for different workloads
- **Flexibility**: Each runner can have different capabilities (Python on this one, PowerShell on that one)

---

## Key Technical Decisions & Why We Made Them

### 1. Vue.js Frontend Instead of Blazor

**The Decision:** We chose Vue 3 + TypeScript over ASP.NET Blazor (which would have kept everything in C#).

**The Why:**
- **Ecosystem**: Vue's ecosystem is mature and focused on web. We wanted best-in-class UI components, and PrimeVue gives us 90+ enterprise-grade components out of the box.
- **Separation of Concerns**: Keeping the frontend separate from the backend forced us to design a clean REST API and think about boundaries early. This is actually a *feature*.
- **Team Dynamics**: Frontend specialists can own the UI; backend engineers can focus on the API. Less coupling, clearer ownership.
- **Developer Experience**: Vite's hot module reload (HMR) is phenomenal. Changes appear instantly. Blazor's reload cycle is slower.

**The Tradeoff:**
We now maintain two separate applications with their own toolchains, dependency management, and deployment. This adds operational complexity, but the benefits outweigh it. The REST API boundary forced us to think clearly about what data flows where, and that clarity cascades through the entire system.

**Lesson:** Sometimes adding *boundaries* (by splitting into frontend and backend) actually *reduces* complexity in the long run, because you're forced to think about the seams.

### 2. Distributed Runners (Pull Model, Not Push)

**The Decision:** Runners connect *to* the backend (pulling work), not the other way around (backend pushing work to runners).

**The Why:**
- **Network Simplicity**: Users don't have to open inbound ports or punch holes in firewalls. The runner can be behind a corporate firewall, on a private VPC, anywhere. It initiates the connection.
- **Security**: The runner calls out; the backend never calls in. This is a fundamental security principle: minimize the attack surface by having agents pull rather than servers push.
- **Resilience**: If a runner goes offline, it's its responsibility to reconnect. If work piles up while it's offline, it's there waiting when it comes back. Simple state machine.

**The Technology:** SignalR (ASP.NET's real-time communication library) handles this beautifully. It's a WebSocket-based persistent connection, but it automatically handles reconnection, heartbeats, and fallback transports. Perfect for this use case.

**Lesson:** "Pull" models are often more robust than "push" models, especially in distributed systems. They shift responsibility to the agent (which knows how to reconnect) rather than the server (which doesn't know if an agent is reachable).

### 3. Project-Based Multi-Tenancy

**The Decision:** Introduced "Projects"—a way to group flows, apps, runners, and custom steps under shared settings and access control.

**The Why:**
- **Real-world Need**: Users wanted to organize work (e.g., "Marketing Project," "Engineering Project") and share resources with colleagues.
- **Access Control**: Projects are containers for role-based permissions. You can be an Admin in one project and a Viewer in another.
- **Isolation**: Resources in one project are separate from resources in another, but users can see all projects they're a member of.

**The Architecture:**
- Personal scope: Resources you own
- Project scope: Resources linked to a project that you're a member of
- A resource can be either Personal or Project, but not both
- When you delete a project, the resources stay (they're orphaned); they're not automatically deleted

**The Tradeoff:**
This added complexity to filtering logic. Many queries now need to ask: "Show me resources where I'm the owner OR I'm a project member with the right role." The `ResourceAuthorizationService` encapsulates this logic and makes it reusable across the app.

**Lesson:** Multi-tenancy seems simple at first ("just add a projectId"), but the filtering logic cascades through the entire codebase. Centralizing that logic in a service pays dividends.

### 4. Feature-Based Frontend Architecture

**The Decision:** Organize the Vue.js frontend by *features*, not by *type*.

**What Does This Mean?**
Instead of:
```
src/
├── components/        # All Vue components
├── services/          # All services
├── types/             # All types
└── stores/            # All stores
```

We do:
```
src/
├── features/
│   ├── flows/
│   │   ├── components/
│   │   ├── services/
│   │   ├── types/
│   │   └── stores/
│   ├── apps/
│   │   ├── components/
│   │   ├── services/
│   │   └── types/
│   └── runners/
│       ├── components/
│       ├── services/
│       └── types/
└── shared/            # Truly shared utilities
```

**Why:**
- **Scalability**: Each feature is self-contained. New developers can understand "flows" without learning about "apps."
- **Lazy Loading**: You can lazy-load entire features, improving initial page load.
- **Fewer Conflicts**: When two developers work on different features, there are fewer file conflicts.
- **Colocation**: Everything related to flows lives in `flows/`. You don't hunt across the codebase.

**Lesson:** Structure your codebase around *domains* (flows, apps, runners), not technical types. This makes the code reflect the *business logic* rather than the *technical stack*.

### 5. Web Apps Platform: Flows as Backend Services

**The Decision:** Turn Automaton into a platform where users can build and publish complete web applications, not just workflows. Apps are frontends (HTML/CSS/JS) that call Flows as their backend via REST APIs.

**The Why:**
Users were building workflows to process data, but they wanted *interfaces* for those workflows—a form to submit data, a dashboard to view results. Instead of forcing them to build separate applications and figure out how to call our API, we gave them a built-in app builder.

Think of it like this: **Flows are the backend; Apps are the frontend.** You design a flow to process an order, then build an app with a form that submits to that flow. The app is a thin UI layer; the flow does the heavy lifting.

**The Technology:**
- **Monaco Editor** (the code editor from VS Code) for editing HTML/CSS/JS with syntax highlighting and IntelliSense
- **Sandboxed iframe preview** so users see changes in real-time without reloading
- **PWA support** (Progressive Web App) so published apps can be installed on devices
- **`window.automatonApi` helper** injected into published apps, providing a clean interface to execute flows without dealing with raw API calls

**The Architecture:**
- Apps have a `PublicUrl` slug (e.g., `/web/order-tracker`)
- Published apps can be **Public** (no auth) or **Protected** (requires Automaton login)
- Each app links to flows it can execute; the runner selection happens client-side
- Apps are versioned: Draft (editable) vs. Published (immutable, served to users)

**Example Use Case:**
```javascript
// Inside a published app (HTML/JS)
const result = await window.automatonApi.executeFlow(flowId, {
  inputs: { orderId: '12345', customerId: 'cust_999' }
});

console.log(result.outputVariables); // { status: "shipped", trackingId: "..." }
```

The app doesn't know (or care) that the flow executes on a runner somewhere. It just calls a flow, gets a result, and displays it.

**The Tradeoff:**
We now maintain an app editor (Monaco integration), a publishing pipeline, access control for published apps, and API key scoping for app-level authentication. This is a lot of surface area.

But the value is massive: users can prototype entire applications (forms, dashboards, reports) without leaving Automaton, and the flows they've already built become instant backend services.

**Security Considerations:**
- Apps use `WebApp`-scoped API keys or short-lived tokens (never embed privileged keys in client-side code)
- Published apps run in a sandbox; they can't access other users' data
- CORS is configured per-app origin to prevent unauthorized domains from calling flows via the app's identity

**Lesson:** Turning backend services (flows) into consumable APIs unlocks an entire category of use cases. When you provide both the backend (workflows) and the frontend (apps), users can build complete solutions without stitching together multiple tools. This is the power of a **platform**, not just a product.

---

## Common Pitfalls & How We Fixed Them

### 1. Theme-Aware Styling (Dark/Light Mode)

**The Pitfall:**
We built dark mode support with PrimeVue, but components kept breaking in dark mode because developers hardcoded background colors:
```css
.my-panel {
  background-color: var(--p-surface-0);  /* Always white! */
}
```

In light mode, this looked fine. In dark mode, white text on a white background is invisible.

**The Fix:**
1. **Never hardcode backgrounds**. Inherit from the parent or use adaptive CSS variables.
2. **Use PrimeVue's theme variables**:
   - `--p-surface-ground` – The current theme's base background
   - `--p-text-color` – The current theme's text color
   - `--p-surface-hover` – Interactive hover state
3. **Document this clearly** in AGENTS.md so all developers follow the pattern.

**Lesson:** Theme support isn't just "add a dark mode toggle." It's a systemic commitment to never making assumptions about colors. It's tempting to hardcode `white` or `#f5f5f5`, but don't. Your future self in dark mode will thank you.

### 2. Iframe Sandbox and File Downloads

**The Pitfall:**
Apps and their preview are rendered inside a sandboxed `<iframe>`. When a published app tries to trigger a file download using a programmatic `<a download>` click, the browser silently discards it — no error, no network request, no download. The execution succeeds (200 OK from the server), the token is created and stored in `IMemoryCache`, the toast fires — but the file never arrives.

The root cause: the `sandbox` attribute did not include `allow-downloads`.

```html
<!-- ❌ Download clicks are silently blocked -->
<iframe sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-popups" />

<!-- ✅ Downloads work -->
<iframe sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-popups allow-downloads" />
```

**The Fix:**
Add `allow-downloads` to the `sandbox` attribute in:
- `WebAppView.vue` (published app iframe)
- `PreviewOverlay.vue` (editor preview iframe)

**Why it's subtle:** There is no console error. The JS runs fine. The server-side token is created and consumed correctly. The only symptom is that the download never starts. This makes it easy to spend a long time debugging the backend (token expiry, IMemoryCache, URL generation) when the problem is entirely in the frontend sandbox permissions.

**Lesson:** When apps need to trigger file downloads, always include `allow-downloads` in the iframe `sandbox`. Any time you add a new sandbox capability to apps, check both `WebAppView.vue` and `PreviewOverlay.vue`.

### 3. Nullable Reference Types in C#

**The Pitfall:**
We enabled nullable reference types (`#nullable enable`) across the backend, but our validation and null-checking were inconsistent. The compiler warned us about potential null dereferences, and we sometimes dismissed them.

Example:
```csharp
public async Task UpdateRunner(Guid id, RunnerDetails runner)
{
    var entity = dbContext.Runners.SingleOrDefault(x => x.Id == id);
    entity.Name = runner.Name;  // ⚠️ entity could be null!
    dbContext.SaveChanges();
}
```

We weren't consistently checking before dereferencing. The compiler warned us, but we'd add null-coalescing operators and move on without thinking about what should actually happen if the runner doesn't exist.

**The Fix:**
1. **Treat warnings as errors during development**. They're telling you something real.
2. **Check early and fail loudly**:
   ```csharp
   var entity = dbContext.Runners.SingleOrDefault(x => x.Id == id);
   if (entity == null)
       throw new InvalidOperationException($"Runner {id} not found");
   ```
3. **Document expectations**: If a value should never be null, make it non-nullable in the type system and fail fast if it is.

**Lesson:** Nullable reference types aren't just compiler features. They're a way to make your assumptions explicit. "This could be null" vs. "this will never be null" is important information.

### 4. Cloudflare Timeouts with a Healthy Origin

**The Pitfall:**
A production site can show `ERR_CONNECTION_TIMED_OUT` in the browser even when the Docker stack, Traefik, TLS, and the app itself are all healthy. This is especially confusing because the first instinct is usually "certificate problem" or "container is down."

In our case:
- Cloudflare had active edge certificates for `automaton.monster` and `*.automaton.monster`
- Traefik was listening on `80` and `443`
- `curl` against the origin IP returned `200 OK` when we sent the correct `Host` header
- the public browser request still timed out while the DNS record was proxied

This tells you the failure is in the Cloudflare-to-origin path, not in the app.

**The Fix:**
Use a short isolation sequence before changing application code:

```bash
# On the server: confirm Traefik is listening
sudo ss -tulpn | grep -E ':80|:443'

# Confirm the site works locally with the real host header
curl -I -H "Host: automaton.monster" http://127.0.0.1
curl -k -I -H "Host: automaton.monster" https://127.0.0.1

# Confirm the origin IP itself serves the site
curl -I -H "Host: automaton.monster" http://178.105.66.33
curl -k -I -H "Host: automaton.monster" https://178.105.66.33
```

If those return the expected redirect/`200 OK`, the origin is healthy.

Then in Cloudflare:
1. Temporarily switch the affected `A` record from **Proxied** to **DNS only**
2. Wait a minute or two
3. Test the site again

If the site loads on `DNS only`, the app is fine and the failing hop is Cloudflare proxying. In our incident, switching to `DNS only` and then back to **Proxied** cleared the bad state.

**Why it's subtle:** A timeout looks like "server down," but the server can be perfectly healthy. Cloudflare certificates can also be valid at the same time, which makes it easy to chase the wrong layer. The decisive test is whether the origin IP answers correctly with the expected `Host` header.

**Lesson:** When a proxied Cloudflare hostname times out, don't start by changing code or rebuilding containers. First prove whether the origin is healthy with `curl` plus the real `Host` header, then use `DNS only` as the quickest proxy-path isolation test.

### 5. Scope/Project Linkage and Authorization

**The Pitfall:**
When we added project scoping to runners (and apps, and flows), we initially treated scope as a simple property. But authorization isn't just "is this in a project?" It's "am I allowed to access this resource in this context?"

Example:
- If a runner is linked to Project A, and I'm an Admin in Project A, I can delete it.
- If a runner is linked to Project A, and I have no Project A membership, I can't see it.
- If a runner is Personal (not linked), and I own it, I can delete it. If someone else owns it, I can't.

We had scattered authorization checks. Some places checked if a runner was in a project; others checked if the user was a project member. This was brittle and inconsistent.

**The Fix:**
1. **Centralize authorization logic** in `ResourceAuthorizationService`.
2. **Create context-aware query methods**:
   ```csharp
   public IQueryable<Runner> GetRunnersQueryByContext(Guid? projectId)
   {
       if (projectId == null)
           // Personal mode: only my runners
           return dbContext.Runners.Where(r => r.RunnerUsers.Any(u => u.UserId == UserId));
       else
           // Project mode: only runners in that project (if I'm a member)
           return dbContext.ProjectRunners
               .Where(pr => pr.ProjectId == projectId && 
                           pr.Project.Members.Any(m => m.UserId == UserId && m.IsActive))
               .Select(pr => pr.Runner);
   }
   ```
3. **Use these queries everywhere** so authorization is consistent.
4. **Add DTOs with scope info** so the frontend knows what actions are available.

**Lesson:** Authorization isn't a feature you bolt on at the end. It's an architectural concern that should be designed in from the beginning. Centralize it; make it testable; make it consistent.

---

## Lessons Learned

### 1. Type Safety is a Multiplier

TypeScript on the frontend and C# with nullable references on the backend might seem like "extra work," but they catch errors at development time, not production time. When you have 25+ API controllers and 10+ features, type mismatches become invisible without tooling. We've spent countless hours debugging typos in property names ("displayName" vs. "display_name") that the type checker would have caught instantly.

**Takeaway:** Invest in a type-safe stack. The upfront cost pays dividends as the codebase grows.

### 2. Real-Time Communication is Hard, But Worth It

SignalR looks simple on the surface (persistent connection, send messages), but it requires thinking about reconnection, heartbeats, timeouts, and message ordering. We've hit issues like:
- A runner goes offline, the frontend doesn't know immediately
- The connection drops, queued messages are lost
- A runner reconnects with a new connection ID

Each of these has implications for how we structure the data. SignalR handles most of it, but you need to understand the patterns it uses.

**Takeaway:** If you're building real-time features, invest time in understanding your signaling layer. The plumbing matters.

### 3. Migrations Are Risky, But Essential

As we added projects and multi-tenancy, we needed database migrations. EF Core makes this easier, but it's still scary: "What if the migration fails halfway through production?" We've learned:
- Always test migrations locally with a copy of production data
- Have a rollback plan (previous migration, or manual SQL)
- Write migrations that are idempotent (can be run multiple times safely)
- Test both up and down migrations

**Takeaway:** Database schema is the contract between your code and your data. Treat migrations with respect.

### 4. Documentation as Code Saves Time

We discovered that when we document *why* decisions were made, future developers (including ourselves) make fewer mistakes. Seeing "we chose Vue for X and Y and Z" helps someone understand the tradeoffs. It prevents the question "why don't we just rewrite this in Blazor?" from being asked every month.

**Takeaway:** Document not just *how* to do things, but *why* you did them this way. Future you will be grateful.

---

## How We Think: Our Engineering Philosophy

### 1. Boundaries are Features

A well-designed API boundary between frontend and backend is not overhead; it's clarity. It forces you to think about what data moves where, what happens when a request fails, what happens when the network is slow. This clarity cascades through your entire system.

We resist the temptation to "just query the database from the frontend" or "just call a service method directly." Those boundaries exist for a reason.

### 2. Centralize Authorization, Distribute Features

Authorization is *inherently* centralized. One place (`ResourceAuthorizationService`) that answers "can you do this?" Everything else calls that service. This prevents authorization bypasses and inconsistencies.

Features, by contrast, are distributed. Each feature folder is self-contained and can be understood independently. This prevents knowledge silos and makes the codebase scalable.

### 3. Type Safety is Kindness

When we use TypeScript and C# (with nullable references), we're being kind to the next developer (who might be ourselves in 6 months). We're making the type system enforce our assumptions, so typos and logical errors are caught early.

This requires discipline—you have to write types that actually reflect your domain—but it's worth it.

### 4. Test What Matters

We don't aim for 100% test coverage. We aim for coverage of:
- Critical paths (flow execution, authorization)
- Edge cases (what happens if a runner disconnects?)
- Integration points (frontend calling backend, backend calling runners)

We skip testing simple getters, trivial UI components, and thin wrapper methods. Coverage is a metric, not a goal.

### 5. Post-Change Validation is Non-Negotiable

After every significant code change, we run:
- Frontend type-check (`npm run type-check`)
- Backend build (`dotnet build`)
- Optionally: lint, tests, preview build

This catches errors immediately, when they're cheapest to fix. It's encoded in a skill (`.github/skills/post-change-validation/SKILL.md`) so every developer (and AI agent) follows the pattern.

### 6. Learning is Continuous, Documentation is Sacred

Every bug we fix, every feature we implement, every pitfall we discover is a learning opportunity. We capture those lessons in:
- **AGENTS.md / CLAUDE.md** – Technical reference (what to do)
- **Skills** – Procedural workflows (how to do it)
- **FOR-DEVELOPERS.md** – Narrative lessons (why we do it)

This creates a learning culture where knowledge compounds over time.

---

## Surprises & "I Wish I'd Known This Earlier"

### 1. SignalR Connection IDs Change

When a runner reconnects, it gets a new connection ID. We initially stored the connection ID on the runner and assumed it was stable. Then runners would disconnect/reconnect and their connection ID would become stale. Now we map runnerId → connectionId at query time, not at storage time.

**Takeaway:** In distributed systems, expect IDs and addresses to change. Don't treat them as stable.

### 2. EF Core's Change Tracking is Subtle

EF Core tracks changes to entities in memory. If you fetch an entity, modify it, and then fetch it again without saving, you get the modified version from the tracker, not the database. This is usually good, but it catches developers off guard when they expect a fresh database read.

**Takeaway:** Understand your ORM's change tracking behavior. Know when to use `AsNoTracking()`.

### 3. Theme Variables Aren't Semantic

PrimeVue's CSS variables like `--p-surface-0`, `--p-surface-100`, etc., don't mean "dark surface" or "light surface." They're absolute (always light). The truly adaptive variables are `--p-surface-ground`, `--p-text-color`, etc. We learned this by debugging dark mode issues.

**Takeaway:** When using a UI framework, understand its semantics. Don't assume variables mean what you think they mean.

---

## What We'd Tell Our Past Selves

1. **Invest in a clean API boundary early.** It saves you countless headaches when the frontend and backend teams need to move at different paces.

2. **Authorization isn't a feature to add later; it's foundational.** Design it in from the beginning.

3. **Type safety is a feature, not a burden.** Use it. Embrace it.

4. **Runners are stateful agents, not stateless functions.** Think about reconnection, heartbeats, and message ordering from day one.

5. **Real-time updates (SignalR) make the UI feel alive, but they're complex.** Don't try to make everything real-time. Focus on the critical paths.

6. **Database migrations are scary, but they're manageable if you have discipline.** Test them. Have rollbacks. Document what you did.

7. **Feature-based organization scales better than type-based.** When you have 20+ components, feature folders make more sense than a monolithic "components" folder.

8. **Document the why, not just the how.** Your future self will thank you.

---

## The Bigger Picture

Automaton is a teaching project. Every decision we make, every lesson we learn, shapes not just the codebase but our engineering philosophy. We're learning how to:
- Build scalable frontends without coupling to the backend
- Architect backends that are flexible enough for multiple UI clients
- Distribute work across agents in different environments
- Use type systems to prevent entire classes of bugs
- Document knowledge so it compounds over time

This document will grow as we learn more. That's not a bug; it's the point. The day you stop learning is the day your code starts getting old.

---

## Further Reading

- **AGENTS.md** – Project overview, technical reference, and feature guide
- **CLAUDE.md** – Comprehensive development guidance
- **docs/** – Feature-specific technical documentation
- **.github/skills/** – Reusable workflow guides for common development tasks
- **src/automaton-studio-vue/REDESIGN_PLAN.md** – Complete Vue.js frontend specification


---

## Common Pitfalls & Lessons Learned

### EF Core Migrations: Always Use the CLI

**The mistake**: Writing migration files by hand to save time.

**What happens**: EF Core migrations come in pairs — <Name>.cs and <Name>.Designer.cs. The designer file contains the full model snapshot EF uses to validate that your migration applies cleanly to the current schema. If you write the .cs file by hand and skip the designer, Database.Migrate() silently ignores your migration at startup. You get no error — just a runtime crash later when EF tries to query a column that doesn't exist yet.

This is exactly the kind of bug that wastes hours: the migration *looks* like it ran, but the database is missing the column.

**The fix**: Always use the CLI:
```bash
dotnet ef migrations add <Name> --project src/Automaton.Studio.Server
```

**You never need to run dotnet ef database update** — Program.cs calls ApplyMigrations() at startup, which runs dbContext.Database.Migrate() automatically. Running it manually is harmless but redundant. More importantly: don't run it in CI/CD thinking it's necessary — it isn't.
