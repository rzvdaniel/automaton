# Project/Organization Feature Documentation

## Overview

This directory contains comprehensive documentation for the **Project/Organization Multi-Tenancy** feature in Automaton Studio. This feature enables enterprise customers to create isolated workspaces where they can collaborate with team members, share resources, and control access through role-based permissions.

---

## Documents in This Directory

### 1. [Technical Approach Document (TAD)](./project-organization-tad.md) 📋
**Primary technical specification document**

**Contents:**
- Executive summary and business context
- Current state analysis
- Proposed solution architecture
- Database schema design
- Access control & permission model
- API design and endpoints
- UI/UX changes
- Migration strategy
- Implementation phases (8-week timeline)
- Security considerations
- Testing strategy
- Open questions & discussion points

**Audience:** Architects, Tech Leads, Developers, Product Managers

**Use this document when:**
- Planning the implementation
- Understanding overall architecture
- Making technical decisions
- Reviewing system design

---

### 2. [Implementation Guide](./implementation-guide.md) 🔧
**Detailed step-by-step implementation instructions**

**Contents:**
- Directory structure (server & client)
- Phase-by-phase implementation steps
- Complete code examples for:
  - Entity models
  - Controllers
  - Services
  - UI components
- Testing checklists
- Common pitfalls & solutions
- Performance optimization techniques
- Security best practices
- Deployment checklist

**Audience:** Developers, QA Engineers

**Use this document when:**
- Writing code
- Setting up database migrations
- Creating UI components
- Debugging issues
- Deploying to production

---

### 3. [Discussion & FAQ](./discussion-and-faq.md) 💬
**Design decisions, alternatives, and frequently asked questions**

**Contents:**
- Design decision rationale (why "Project"? why 4 roles?)
- Alternative approaches considered
- Common questions from users, admins, and developers
- Industry comparisons (GitHub, Slack, Jira)
- Performance considerations
- Open discussion points
- Success metrics & KPIs
- Feedback iteration process

**Audience:** Product Managers, Designers, Stakeholders, Support Team

**Use this document when:**
- Understanding "why" behind decisions
- Answering user questions
- Planning future phases
- Discussing trade-offs
- Training support team

---

## Quick Reference

### Key Concepts

**Project**
- Isolated workspace for teams/departments
- Contains Apps, Flows, Runners, Custom Steps
- Members have specific roles with defined permissions

**Project Roles**
1. **ProjectAdmin** - Full control (manage members, settings)
2. **Developer** - Create/edit apps, flows, steps (full dev tools)
3. **AppUser** - Execute apps only (consumer view)
4. **Viewer** - Read-only access

**Access Scopes**
- **Personal** - User-owned resources (current behavior)
- **Project** - Project-owned, members can access
- **Public** - Published publicly, anyone can access

### Architecture at a Glance

```
┌────────────────────────────────────────────────────┐
│                  Automaton Studio                   │
├────────────────────────────────────────────────────┤
│                                                     │
│  Standard User                Enterprise User      │
│  (Solo Creator)               (Project Owner)      │
│       ↓                              ↓             │
│  ┌──────────────┐          ┌──────────────────┐  │
│  │   Personal   │          │    PROJECT(S)    │  │
│  │  Workspace   │          │  - Members       │  │
│  │  - Apps      │          │  - Apps          │  │
│  │  - Flows     │          │  - Flows         │  │
│  │  - Runners   │          │  - Runners       │  │
│  └──────────────┘          └──────────────────┘  │
│                                                     │
└────────────────────────────────────────────────────┘
```

### Database Schema (Core Tables)

```sql
Projects
  - Id (PK)
  - Name (Unique)
  - DisplayName
  - OwnerId (FK → ApplicationUser)

ProjectMembers
  - ProjectId (PK, FK)
  - UserId (PK, FK)
  - Role (ProjectAdmin/Developer/AppUser/Viewer)

ProjectApps
  - ProjectId (PK, FK)
  - WebAppId (PK, FK)

(Similar: ProjectFlows, ProjectRunners, ProjectCustomSteps)
```

### Implementation Timeline

| Phase | Duration | Focus | Deliverables |
|-------|----------|-------|--------------|
| **Phase 1** | 2 weeks | Core Infrastructure | Database, APIs, Services |
| **Phase 2** | 2 weeks | Project Management UI | Projects page, Settings, Members |
| **Phase 3** | 2 weeks | Resource Sharing | Add Apps/Flows to projects |
| **Phase 4** | 2 weeks | Dashboard Personalization | Role-based UIs |
| **Total** | **8 weeks** | **MVP** | Fully functional multi-tenancy |

### API Endpoints (Summary)

```
# Projects
GET    /api/projects
POST   /api/projects
GET    /api/projects/{id}
PUT    /api/projects/{id}
DELETE /api/projects/{id}

# Members
POST   /api/projects/{id}/members
GET    /api/projects/{id}/members
PUT    /api/projects/{id}/members/{userId}
DELETE /api/projects/{id}/members/{userId}

# Resources
POST   /api/projects/{id}/apps/{appId}
DELETE /api/projects/{id}/apps/{appId}
GET    /api/projects/{id}/apps
(Similar for /flows, /runners, /customsteps)
```

### Permission Examples

| Action | ProjectAdmin | Developer | AppUser | Viewer |
|--------|--------------|-----------|---------|--------|
| Create App | ✅ | ✅ | ❌ | ❌ |
| Edit App | ✅ | ✅ | ❌ | ❌ |
| Execute App | ✅ | ✅ | ✅ | ❌ |
| Create Flow | ✅ | ✅ | ❌ | ❌ |
| Execute Flow | ✅ | ✅ | ❌ | ❌ |
| View Logs | ✅ | ✅ | ❌ | ✅ |
| Add Members | ✅ | ❌ | ❌ | ❌ |

---

## Getting Started

### For Product Managers
1. Read [TAD](./project-organization-tad.md) - Executive Summary & Business Context
2. Review [FAQ](./discussion-and-faq.md) - Understand user personas
3. Plan beta rollout strategy

### For Architects
1. Read [TAD](./project-organization-tad.md) - Full document
2. Review database schema design
3. Validate against system constraints
4. Identify integration points

### For Developers
1. Skim [TAD](./project-organization-tad.md) - Architecture Overview
2. Deep dive [Implementation Guide](./implementation-guide.md)
3. Set up local dev environment
4. Start with Phase 1 (Database)

### For Designers
1. Read [TAD](./project-organization-tad.md) - UI/UX Changes section
2. Review [FAQ](./discussion-and-faq.md) - User personas
3. Design mockups for:
   - Projects list page
   - Project settings (members panel)
   - Role-based dashboards

### For QA/Testers
1. Read [Implementation Guide](./implementation-guide.md) - Testing Strategy
2. Create test plans for each phase
3. Set up test environments
4. Prepare test data (users, projects, resources)

### For Support Team
1. Read [FAQ](./discussion-and-faq.md) - All FAQs
2. Practice common scenarios:
   - Inviting users
   - Changing roles
   - Troubleshooting access issues
3. Create internal support docs

---

## Contributing

### How to Update Documentation

**When to update:**
- Design decision changes
- New feature added
- User feedback requires clarification
- Implementation differs from spec

**Update process:**
1. Create feature branch: `docs/update-project-tad`
2. Edit relevant document(s)
3. Increment version number in document footer
4. Add "Last Updated" date
5. Submit PR with clear summary of changes

### Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2024-11-09 | Initial documentation |

---

## Related Documentation

### Within Automaton Docs

- [Web Apps Feature TAD](../webapps/apps-feature-technical-approach.md)
- [API Key Scopes Guide](../apikeys/API_KEY_SCOPES_GUIDE.md)
- [Flow Architecture](../flows/FLOW_ARCHITECTURE_HOW_IT_WORKS.md)
- [Runner Registration Flow](../runners/runner-registration-flow.md)

### External Resources

- [ASP.NET Core Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity)
- [Policy-Based Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies)
- [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/)
- [Multi-Tenancy Patterns](https://docs.microsoft.com/en-us/azure/architecture/guide/multitenant/overview)

---

## Contact & Support

**For questions about this feature:**
- Technical questions → Engineering team
- Product questions → Product Manager
- User experience → UX/Design team

**Document maintainers:**
- Primary: Automaton Development Team
- Last reviewed: November 9, 2024

---

## Glossary

**AppUser**
A project member with permission to execute apps but not create/edit them. Consumer-focused role.

**Developer**
A project member with full access to development tools (create/edit apps, flows, steps).

**Personal Workspace**
User's private area for creating resources not shared with projects.

**Project**
An isolated workspace containing apps, flows, runners, and custom steps, shared among members.

**ProjectAdmin**
A project member with full control over project settings, members, and resources.

**Resource**
Any entity in Automaton: App, Flow, Runner, Custom Step, Schedule, etc.

**Scope**
Access level of a resource: Personal (user-owned), Project (team-owned), or Public (published).

**Viewer**
A project member with read-only access. Can view resources but not execute or modify them.

---

**Document Version:** 1.0  
**Last Updated:** November 9, 2024  
**Maintained by:** Automaton Development Team
