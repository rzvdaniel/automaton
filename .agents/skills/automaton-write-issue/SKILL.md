---
name: automaton-write-issue
description: Use whenever you create a GitHub issue in Automaton - a bug, feature, gap found while testing, or follow-up - from any flow, so every issue has the same short shape, a clear "Done when" and its place on the project board.
license: Apache-2.0
---

# Writing an issue

Create an issue before building when the request came from chat, and for every gap found while testing.

## One issue or several

Combine small fixes into one issue so one agent run, review and gate cover them: up to 3-4 fixes in the same area
(Studio UI, even on different pages; one app; docs and text), each with its own line under **Done when**. Write them
as one issue from the start. Keep separate: security, auth, installer or backup changes; anything large or uncertain;
changes in different repositories or layers (Studio and Runner). If review blocks one part of a combined PR, split
that part out instead of holding the rest.

## Title

What should be true, in plain words: "An app owner can delete their app after losing Runner access", not
"Fix delete bug".

## Body

The section names are those of the issue forms in `.github/ISSUE_TEMPLATE/` (`feature.yml`, `bug.yml`), the one source;
`.agents/tools/issue-check.py` labels an issue `needs-shape` when a required section is missing, a **Done when** item
names no check, or a **Before work** box is unticked. `Before work` and `Related` are optional.

For a bug:

```markdown
**Problem**
What goes wrong and who it affects, in one or two sentences.

**Steps**
1. …
2. …

**Result**
What happens, with the exact error or status code.

**Expected**
What should happen instead.

**Before work**
Optional: owner-only prerequisites (tokens, accounts, console clicks), one checkbox each (`- [ ] ...`).

**Done when**
Each item says how an agent checks it and where (test server, install check, CI run).

**Related**
Optional: issues, PRs and files by path.
```

For a feature:

```markdown
**Goal**
As a <role>, I want <capability>, so that <benefit>.

**Example**
A concrete case: who does what, and what they see.

**Suggestion**
- The simplest approach you see, one idea per bullet.
- A table when there are options (see Formatting):

  | Mode | What happens |
  |---|---|
  | Off | nothing |
  | **Notify** (default) | "0.2.174 available", with the release notes |

- Out of scope: what this issue will not do.

**Before work**
Optional: owner-only prerequisites (tokens, accounts, console clicks), one checkbox each (`- [ ] ...`).

**Done when**
Each item says how an agent checks it and where (test server, install check, CI run).

**Related**
Optional: issues, PRs and files by path.
```

**Done when** names an end-to-end check, not "tests pass": each item must say how an agent checks it and where (test
server, install check, CI run). If only a unit test can cover it (for example it needs a fresh server), say so; the
issue then gets `verified-partial` until someone runs the real check.

**Before work** holds owner-only prerequisites (tokens, accounts, console clicks) with one checkbox (`- [ ]`) each.
`.agents/tools/issue-check.py` adds `needs-shape` while any **Before work** box is unticked (or when a **Done when**
item names no check), so Agent Keeper's Pick up sends the card to Inbox until the owner ticks the box and fixes the
item.

Keep it short. Link related issues and PRs by number. No credentials, hostnames from secrets, or customer data.

## UI changes: attach the mock

Agents cannot open private claude.ai artifacts. For any issue that changes a screen, commit the mock to the repo and
link it from **Example**: the HTML under `docs/design/mocks/<slug>.html` and a screenshot of each main state next to it
(`<slug>-<state>.png`). The issue text still describes every state, so it stands without the mock.

## Formatting

So issues read well for people and agents alike:

- A **table** for options, modes, settings, permissions or a before/after: one row per item, a short "What happens" column.
- A **Mermaid** diagram (`flowchart` or `sequenceDiagram` in a `mermaid` code block) only when a flow has 3+ steps or 3+
  parts talking to each other, under about 10 nodes. GitHub renders it; agents read the text.
- Numbered lists for steps, bullets for everything else; one idea per bullet.
- Code blocks for commands, errors, config and file paths. Quote error text exactly.
- Bold only the key term at the start of a bullet, or the default choice.
- No ASCII-art boxes, images of text or emoji. Plain words; name things the way the UI names them.

## Board

If you maintain the project boards, put the issue on the right board with its fields and parent issue, as
`.agents/environment.md` describes (boards, fields, parents per kind of work). Contributors without board access skip this: the
maintainers place the issue. Add a sub-issue with
`gh api -X POST repos/<owner>/<repo>/issues/<parent>/sub_issues -F sub_issue_id=<issue id>`.
