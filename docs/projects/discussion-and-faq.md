# Project/Organization Feature - Discussion & FAQ

## Overview

This document addresses common questions, design decisions, and discussion points for the Project/Organization multi-tenancy feature. It serves as a companion to the [Technical Approach Document](./project-organization-tad.md).

---

## Design Decisions & Rationale

### 1. Why "Project" instead of "Organization" or "Workspace"?

**Decision:** Use "Project" terminology in Phase 1.

**Rationale:**
- **Flexibility:** A "Project" can represent a team, department, or initiative
- **Scalability:** Later, we can introduce "Organizations" that contain multiple Projects
- **Industry precedent:** GitHub uses Repositories under Organizations; Jira uses Projects
- **User mental model:** "I'm working on the Marketing Automation project" is clear

**Future Evolution:**
```
Phase 1: Projects only
Phase 2: Organizations → Projects hierarchy
Phase 3: Enterprise tenants → Organizations → Projects
```

**Alternative considered:**
- "Workspace" (too generic)
- "Organization" (implies large scale; intimidating for small teams)
- "Team" (too limiting; projects can be cross-team)

---

### 2. Should users have both Personal Workspace AND Projects?

**Decision:** Yes, support dual mode.

**Rationale:**

**Pros of dual mode:**
- ✅ **Flexibility:** Users can prototype personally before adding to project
- ✅ **Privacy:** Personal experiments don't clutter project
- ✅ **Migration path:** Existing users keep personal workspace
- ✅ **Freelancers:** Independent contractors can have personal + client projects

**Cons of dual mode:**
- ❌ **Complexity:** Need to manage two contexts
- ❌ **Confusion:** "Where should I create this app?"
- ❌ **Code paths:** Support both Personal and Project scopes

**Mitigation:**
- Clear UI indicators (badge: "Personal" vs "Project: Marketing")
- Default context: Last used project (or personal if new user)
- Move/copy resources between personal ↔ project

**Alternative considered:**
- Force everything into projects (creates "Personal" project automatically)
- Rejected because: Users in 10 projects don't want 10+ projects cluttering UI

---

### 3. What happens when a resource creator leaves the project?

**Decision:** Resources stay with the project (project-owned, not user-owned).

**Rationale:**

**Scenario:**
1. Alice (Developer) creates "Sales Dashboard" app in "Marketing" project
2. Alice leaves company (removed from project)
3. Bob (Developer) should still have access to the app

**Implementation:**
- **Owner field:** Change from `CreatedBy` (implies ownership) to `CreatedBy` (audit only)
- **Access control:** Based on project membership, not individual ownership
- **Deletion:** Only ProjectAdmin can delete resources (not just creator)

**Alternative considered:**
- Creator retains ownership; resource becomes personal when they leave
- Rejected because: Enterprise workflows require continuity

**Edge case:** What if creator wants to take resource to new project?
- Solution: "Move to Project" or "Copy to Project" feature (Phase 2)

---

### 4. Should project membership be invite-only or can users request to join?

**Decision:** Phase 1 = Invite-only. Phase 2+ = Add request-to-join option.

**Rationale:**

**Invite-only (Phase 1):**
- ✅ **Security:** Admin controls who joins
- ✅ **Simplicity:** No approval workflow needed
- ✅ **Enterprise standard:** Most B2B tools are invite-only

**Request-to-join (Phase 2+):**
- ✅ **Discovery:** Users can find public/internal projects
- ✅ **Self-service:** Reduces admin overhead
- ❌ **Spam risk:** Need approval workflow

**Implementation plan:**
- Phase 1: Invite via email (create account if needed)
- Phase 2: Add "Request Access" button on project page (if public)
- Phase 3: Auto-approve based on email domain (@company.com)

---

### 5. How to handle API keys with Projects?

**Current:** API keys are user-scoped (FullAccess, Runner, Execute, ReadOnly).

**Decision:** Phase 1 = Keep user-scoped. Phase 2 = Add project-scoped keys.

**Rationale:**

**Phase 1 behavior:**
- User creates API key → Key has access to all projects user is member of
- Runner registered with key → Appears in all user's projects
- Simple, no breaking changes

**Phase 2 behavior:**
- **User-scoped key:** Access to all user's projects (current behavior)
- **Project-scoped key:** Access only to specific project
  - Use case: Runner dedicated to one project
  - Use case: CI/CD pipeline for specific team

**Implementation:**
```csharp
public class ApiKey
{
    public Guid Id { get; set; }
    public string Key { get; set; }
    public string Scope { get; set; }  // "FullAccess", "Runner", etc.
    
    // NEW: Optional project restriction
    public Guid? ProjectId { get; set; }  // NULL = all projects
    public Project? Project { get; set; }
}
```

**UI:**
- API Keys page: Toggle "Restrict to Project" → Select project
- Runner registration: Specify project during setup

---

### 6. Should AppUsers see *any* creation tools, or purely card-based apps?

**Decision:** Pure app-consumer experience (no dev tools).

**Rationale:**

**AppUser persona:**
- Sales rep using "Lead Tracker" app
- Support agent using "Ticket Dashboard"
- Manager viewing "Analytics Dashboard"

**They should NOT see:**
- Flow Designer
- Step Designer
- API Keys
- Runner management

**They SHOULD see:**
- Apps assigned to them (card-based UI)
- Favorites/Recent apps
- App execution history (their own)

**Exception:** If user is AppUser in Project A but Developer in Project B:
- When viewing Project A → App-consumer UI
- When viewing Project B → Full developer UI
- Context-sensitive navigation menu

**Implementation:**
```csharp
// In MainMenu.razor.cs
private async Task LoadMenuItems()
{
    var currentProject = await GetCurrentProject();
    var userRole = await GetUserRoleInProject(currentProject.Id);
    
    ShowDeveloperTools = userRole switch
    {
        ProjectRole.ProjectAdmin => true,
        ProjectRole.Developer => true,
        _ => false
    };
    
    ShowApps = userRole switch
    {
        ProjectRole.AppUser => true,
        ProjectRole.ProjectAdmin => true,
        ProjectRole.Developer => true,
        _ => false
    };
}
```

---

### 7. How to handle Runner assignment in multi-project scenarios?

**Scenario:**
- Runner "Runner-A" is added to "Marketing" project
- Should it also appear in "Sales" project?

**Decision:** Runners are project-scoped, but can be shared.

**Phase 1 (Simple):**
- Runner belongs to one project
- If user wants same runner in 2 projects → Register twice with different names

**Phase 2 (Shared Runners):**
- Runner can be linked to multiple projects
- UI: "Share Runner with Project" button
- Access control: Projects can execute flows on shared runners

**Implementation:**
```csharp
// Phase 2 schema
public class Runner
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    // ...existing fields
    
    // NEW: Multi-project links
    public ICollection<ProjectRunner> ProjectRunners { get; set; }
}

// Many-to-many allows sharing
public class ProjectRunner
{
    public Guid ProjectId { get; set; }
    public Guid RunnerId { get; set; }
    public DateTime AddedAt { get; set; }
    public Guid AddedBy { get; set; }
}
```

**Alternative considered:**
- Runners are global, projects just tag them
- Rejected because: Enterprises want dedicated runners per team (cost allocation, security)

---

### 8. How granular should role permissions be?

**Current proposal:** 4 roles
1. ProjectAdmin
2. Developer
3. AppUser
4. Viewer

**Question:** Is this enough? Should we have:
- AppEditor (can edit apps, not create)
- FlowEditor (can edit flows, not create)
- StepCreator (can create steps, not apps/flows)

**Decision:** Start with 4 roles. Add granular permissions in Phase 2 if needed.

**Rationale:**
- **Simplicity:** More roles = more confusion
- **80/20 rule:** 4 roles cover 90% of use cases
- **Custom roles:** Phase 3 can add "Create Custom Role" feature

**When to add granular permissions:**
- User feedback: "I need X to do Y but not Z"
- Enterprise request: "Contractors can edit but not publish"
- Security requirement: "Compliance auditor needs read-only"

**Implementation strategy (Phase 3):**
```csharp
public class ProjectMember
{
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    
    // Option A: Role enum (Phase 1)
    public string Role { get; set; }
    
    // Option B: Custom permissions (Phase 3)
    public string[] Permissions { get; set; }  // ["app.create", "app.edit", "flow.execute"]
}
```

---

### 9. Should Projects be hierarchical (parent/child)?

**Example:**
```
Organization: Acme Corp
  ├── Department: Engineering
  │   ├── Team: Frontend
  │   └── Team: Backend
  └── Department: Marketing
```

**Decision:** Phase 1 = Flat projects. Phase 2+ = Optional hierarchy.

**Rationale:**

**Flat (Phase 1):**
- ✅ **Simplicity:** Easy to implement
- ✅ **Sufficient:** Most teams have 1-5 projects
- ❌ **Scalability:** Large orgs need hierarchy

**Hierarchical (Phase 2+):**
- ✅ **Scalability:** Supports 100+ projects
- ✅ **Inheritance:** Child inherits permissions from parent
- ❌ **Complexity:** Nested queries, UX challenges

**Implementation plan:**
1. **Phase 1:** `Project` table (no parent)
2. **Phase 2:** Add `ParentProjectId` (optional)
3. **Phase 3:** Add `Organization` entity (top-level container)

**Migration path:**
- Existing projects stay flat
- Opt-in to hierarchy: "Convert to Department" action

---

### 10. How to handle subscription/billing with Projects?

**Question:** Who pays?
- Project owner?
- Per project?
- Organization level?

**Decision:** Phase 1 = Per-user subscription. Phase 2+ = Per-project or per-org.

**Phase 1 (MVP):**
- User has "Enterprise" subscription
- Can create unlimited projects
- All members must have Automaton accounts (free tier OK)

**Phase 2 (Project Billing):**
- Project owner pays for project
- Can add unlimited members
- Members use project resources without individual subscription

**Phase 3 (Organization Billing):**
- Organization has master subscription
- Projects are free within org
- Usage pooled at org level

**Implementation:**
```csharp
public class Project
{
    public Guid Id { get; set; }
    // ...
    
    // Phase 2 fields
    public string? SubscriptionTier { get; set; }      // "Team", "Enterprise"
    public DateTime? SubscriptionExpiresAt { get; set; }
    public string? BillingEmail { get; set; }
}
```

**Questions for discussion:**
- Should free-tier users be able to join enterprise projects?
  - **Yes:** Owner pays, members benefit
- Should project limit number of members?
  - **No:** Limit by subscription tier (Team=10, Enterprise=unlimited)

---

## Common Questions (FAQ)

### General

**Q: Will this break existing functionality?**
A: No. Existing users stay in "Standard Mode" with current behavior. Projects are opt-in.

**Q: Can I use Automaton without Projects?**
A: Yes. Projects are optional, even with Enterprise subscription.

**Q: Can I move my personal resources into a project?**
A: Yes. "Add to Project" button on App/Flow detail pages (Phase 2).

**Q: Can I be a member of multiple projects?**
A: Yes. Switch between projects via dropdown in header.

**Q: Can I have different roles in different projects?**
A: Yes. You might be ProjectAdmin in one, AppUser in another.

### For Admins

**Q: How do I invite users?**
A: Project Settings → Members → "Add Member" → Enter email and role → "Send Invite"

**Q: What if the invited person doesn't have an Automaton account?**
A: System creates account and sends invitation email with password setup link.

**Q: Can I remove myself from a project?**
A: Only if you're not the last ProjectAdmin. Transfer admin role first.

**Q: Can I delete a project?**
A: Yes, if you're the owner. Warning: This deletes all apps/flows/runners in the project.

**Q: How do I transfer ownership?**
A: Project Settings → General → "Transfer Ownership" → Select new owner

### For Developers

**Q: Do I need to create apps in a project?**
A: No. You can create apps personally or in projects (both work).

**Q: Can I share an app with multiple projects?**
A: Phase 1: No. Phase 2: Yes, via "Share to Project" feature.

**Q: Can I export a project?**
A: Not in Phase 1. Phase 2+ will support project export (JSON bundle).

**Q: Can I use CI/CD with projects?**
A: Yes. Create project-scoped API key, use in CI pipeline to deploy flows/apps.

### For End Users (AppUsers)

**Q: Why can't I see the Flow Designer?**
A: Your role is "AppUser" which only grants access to apps, not development tools.

**Q: How do I request Developer access?**
A: Ask your ProjectAdmin to change your role to "Developer".

**Q: Can I create my own apps?**
A: Not within the project. If you have personal account, you can create personal apps.

**Q: Why do I see different menus in different projects?**
A: Menu adapts to your role in each project (Developer vs AppUser).

### Security

**Q: Can users in Project A access resources in Project B?**
A: No. Projects are isolated unless you're a member of both.

**Q: What if I accidentally publish an app publicly?**
A: Unpublish it immediately. Check audit logs to see if it was accessed.

**Q: Can I restrict runners to specific projects?**
A: Yes. Runners are project-scoped (unless shared in Phase 2).

**Q: Are API keys project-scoped?**
A: Phase 1: User-scoped (access all your projects). Phase 2: Option to scope to specific project.

### Migration

**Q: What happens to my existing apps when I enable Projects?**
A: They remain in your personal workspace. Optionally migrate them to a project.

**Q: Can I go back to Standard Mode after enabling Projects?**
A: Yes, but your projects will be hidden (data preserved).

**Q: Will my teammates see my personal apps?**
A: No. Personal apps are only visible to you, unless you add them to a project.

---

## Industry Comparisons

### How Automaton Projects Compare

| Feature | Automaton | GitHub Orgs | Slack Workspaces | Jira Projects |
|---------|-----------|-------------|------------------|---------------|
| **Hierarchy** | Flat (P1) | Org → Repos | Workspace → Channels | Project → Issues |
| **Roles** | 4 roles | Owner/Admin/Member | 4 roles | Custom roles |
| **Multi-membership** | Yes | Yes | Yes | Yes |
| **Resource isolation** | Yes | Yes | Yes | Yes |
| **Billing** | Per user (P1) | Per org | Per workspace | Per project |
| **Public projects** | Optional | Yes (public repos) | No | Optional |

### Lessons from Industry Leaders

**GitHub:**
- ✅ **Clear ownership:** Orgs own repos, not individuals
- ✅ **Flexible permissions:** Per-repo + org-level
- ❌ **Complexity:** Learning curve for new users

**Slack:**
- ✅ **Simple roles:** Owner, Admin, Member, Guest (easy to understand)
- ✅ **Context switching:** Workspace switcher in sidebar
- ❌ **No hierarchy:** Can't nest workspaces

**Jira:**
- ✅ **Granular permissions:** Custom roles per project
- ✅ **Export/Import:** Easy to move projects
- ❌ **Overwhelming:** Too many permission options

**Automaton's approach:**
- Start simple (4 roles, flat projects)
- Iterate based on feedback
- Add complexity only when needed

---

## Performance Considerations

### Scaling Concerns

**Question:** How will Projects perform with 1000+ members?

**Answer:** Optimize queries and add pagination.

**Bottlenecks:**
1. Loading project members: Use pagination (20 per page)
2. Checking permissions: Cache user's roles (`IMemoryCache`)
3. Filtering resources: Add database indexes

**Example optimization:**
```csharp
// Before (N+1 query)
var projects = await GetUserProjects(userId);
foreach (var project in projects)
{
    project.MemberCount = await GetMemberCount(project.Id);  // Extra query!
}

// After (single query)
var projects = await _context.ProjectMembers
    .Where(pm => pm.UserId == userId)
    .Select(pm => new {
        Project = pm.Project,
        MemberCount = pm.Project.Members.Count()  // Computed in DB
    })
    .ToListAsync();
```

### Caching Strategy

**What to cache:**
- User's project list (5 min TTL)
- User's role in each project (10 min TTL)
- Project member count (15 min TTL)

**What NOT to cache:**
- Permission checks (security risk)
- Recent activity (needs to be real-time)

**Invalidation:**
```csharp
// When member is added/removed
_cache.Remove($"user_projects_{userId}");
_cache.Remove($"project_members_{projectId}");
```

---

## Open Discussion Points

### 1. Project Discovery

**Question:** Should projects be discoverable (public directory)?

**Options:**
- **A:** Projects are private by default, invite-only
- **B:** Projects can be marked "Public" (anyone can request to join)
- **C:** Internal directory (anyone in organization can see projects)

**Recommendation:** Phase 1 = A (private). Phase 2 = Add B/C as opt-in features.

### 2. Resource Limits

**Question:** Should we limit resources per project?

**Examples:**
- Max 100 apps per project?
- Max 1000 flows per project?
- Max 50 runners per project?

**Recommendation:** Phase 1 = No limits. Monitor usage, add soft limits if abuse detected.

### 3. Audit Logging

**Question:** What level of audit logging for projects?

**Track:**
- Member added/removed
- Role changed
- Resource added/removed
- Project settings changed

**Retention:** 90 days (standard), 1 year (enterprise)

**Implementation:** Use existing `RecentActivity` table + new `ProjectAuditLog` table

### 4. Project Templates

**Question:** Should we provide project templates?

**Examples:**
- "Marketing Automation" (pre-loaded with sample apps/flows)
- "Sales CRM" (customer management apps)
- "IT Operations" (monitoring dashboards)

**Recommendation:** Phase 2+. Community-contributed templates.

### 5. Cross-Project Sharing

**Question:** Should resources be shared across projects?

**Example:** "Customer Portal" app used by both Sales and Support projects.

**Options:**
- **A:** No sharing (duplicate resources)
- **B:** Share apps across projects (complex permissions)
- **C:** Create "Shared" project (all projects pull from it)

**Recommendation:** Phase 1 = A (simple). Phase 2 = Explore B or C based on demand.

---

## Success Metrics

### How to Measure Success

**Phase 1 (MVP):**
- ✅ 10+ beta users enable Projects
- ✅ Average 2-3 projects per enterprise user
- ✅ 5+ members per project (validates collaboration)
- ✅ Zero data breaches (projects isolated correctly)
- ✅ <500ms API response time (performance)

**Phase 2 (Growth):**
- ✅ 100+ active projects
- ✅ 50% of enterprise users adopt Projects
- ✅ 10+ apps per project (validates usage)
- ✅ Positive feedback (NPS score >50)

**Phase 3 (Maturity):**
- ✅ 1000+ projects
- ✅ 80% enterprise user adoption
- ✅ Advanced features used (custom roles, templates)
- ✅ Zero critical bugs in project system

**Key Performance Indicators (KPIs):**
- Project creation rate (per week)
- Average members per project
- Resource sharing frequency
- Role distribution (% admins vs developers vs app users)
- Dashboard satisfaction score

---

## Feedback & Iteration

### How to Gather Feedback

**Phase 1 (Beta):**
- Invite 10-20 enterprise customers
- Weekly check-ins
- Survey after 2 weeks
- Analytics: Track feature usage

**Phase 2 (Public Beta):**
- In-app feedback widget
- Community forum for feature requests
- Monthly feedback review meeting

**Questions to ask:**
1. Is the role model clear?
2. Do you need more/fewer roles?
3. Is project switching intuitive?
4. What features are missing?
5. Any bugs or confusing behavior?

### Iteration Process

1. **Collect:** Aggregate feedback weekly
2. **Prioritize:** Bug fixes > UX improvements > New features
3. **Plan:** Add to next sprint
4. **Build:** Implement incrementally
5. **Test:** Beta users validate
6. **Deploy:** Gradual rollout
7. **Monitor:** Track metrics

---

## Conclusion

This document addresses key design decisions and open questions for the Project/Organization feature. It should evolve as the feature is developed and feedback is received.

**Key Takeaways:**
- Start simple (4 roles, flat projects, invite-only)
- Iterate based on real usage (don't over-engineer)
- Security first (validate permissions everywhere)
- Backward compatible (existing users unaffected)
- Scalable architecture (support 1000+ projects)

**Next Steps:**
1. Review this document with stakeholders
2. Make decisions on open questions
3. Update main TAD with any changes
4. Begin Phase 1 implementation

---

**Document Version:** 1.0  
**Last Updated:** November 9, 2024  
**Contributors:** Automaton Team  
**Status:** Living Document (will be updated based on feedback)
