# Public launch

Updated: 3 November 2026. Status: planned. Work and status: parent issue [#560](https://github.com/rzvdaniel/automaton/issues/560) and its sub-issues; this file holds the design.

Automaton is about to get wider attention through weekly articles (LinkedIn, Medium). When someone reads one and visits, they should find a server that installs and updates cleanly by following the public docs, a clear security posture, pages that answer the obvious questions, and a way to see Automaton working without an account. This plan collects that work. It ends when the owner decides Automaton is ready to be shown.

## Rules

- Self sign-up stays disabled. Visitors see Automaton through the public store, screenshots and a video, not through accounts on our servers.
- Claims are measured, not hoped: install time, server size, costs and limitations come from the fresh-server pass, and pages say what Automaton does not do.
- Work owned by another plan stays there and blocks this plan's parent issue: .NET 10, two-user isolation (#521), restore on a clean server (#524), off-site backup (#486).

## Plan

Steps 2–5 are independent of each other. Step 1 waits for .NET 10.

1. **Fresh-server install and update pass** (#561) — *first run done* on 3 November 2026 (CPX22, release 0.2.0; measured figures in the install guide; snags filed as issues). A second run after the fixes confirms it. On a newly rented Hetzner server, follow only the public README and install guide as a newcomer: install, first administrator, Runner, first app from the CLI, a store app, server update, Runner upgrade, backup. Time each step and file every snag as an issue. *Acceptance:* a full run without undocumented steps; measured times and server size are written into the docs.
2. **Security policy** (#562) — *done* (`SECURITY.md`, [security overview](../../src/automaton-vue/docs/security.md), private vulnerability reporting enabled). `SECURITY.md` with how to report a vulnerability privately (GitHub private vulnerability reporting), supported versions and response expectations; a short security overview page (isolation model, secrets, permissions, what is out of scope). *Acceptance:* GitHub shows the policy and private reporting is enabled.
3. **Public pages** (#563) — *done* (README and website: fit, cloud comparison with measured costs, FAQ, limitations). README and website answer: what Automaton is, who it is for and not for, a quickstart, "Automaton or a cloud provider?" with honest pros and cons and real costs, an FAQ, current limitations. *Acceptance:* a newcomer can decide whether Automaton fits them without asking.
4. **Community and repository tidy** (#564) — *done* (issue templates, Discussions enabled, `CONTRIBUTING.md`, removed `QWEN.md` and `docs/prompts/`). Issue templates (bug, question, feature), GitHub Discussions for questions, `CONTRIBUTING.md`, and a review of what a visitor sees at the repository root and in `docs/` (internal prompts, agent-specific files, outdated notes). *Acceptance:* a visitor knows where to ask, report and contribute; nothing internal or stale is prominent.
5. **Demo** (#565). A public store on automaton.monster with a few good sample apps, screenshots in the README, and a short video from install to a running app. *Acceptance:* someone with no account can see Automaton working in under five minutes.
