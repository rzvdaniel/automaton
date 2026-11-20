---
name: automaton-write-issue
description: Use whenever you create a GitHub issue in Automaton - a bug, feature, gap found while testing, or follow-up - from any flow, so every issue has the same short shape, a clear "Done when" and its place on the project board.
license: Apache-2.0
---

# Writing an issue

Create an issue before building when the request came from chat, and for every gap found while testing.

## Title

What should be true, in plain words: "An app owner can delete their app after losing Runner access", not
"Fix delete bug".

## Body

The section names are those of the issue forms in `.github/ISSUE_TEMPLATE/` (`feature.yml`, `bug.yml`), the one source;
`.agents/tools/issue-check.py` labels an issue `needs-shape` when a required one is missing. Related is optional.

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

**Done when**
What will be checked on the test server (and, if it shows there, on production).

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

**Done when**
What will be checked on the test server, as a real user would do it.

**Related**
Optional: issues, PRs and files by path.
```

"Done when" names an end-to-end check, not "tests pass". If only a unit test can cover it (for example it needs a
fresh server), say so; the issue then gets `verified-partial` until someone runs the real check.

Keep it short. Link related issues and PRs by number. No credentials, hostnames from secrets, or customer data.

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
