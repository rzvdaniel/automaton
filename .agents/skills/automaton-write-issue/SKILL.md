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
```

For a feature:

```markdown
**Goal**
As a <role>, I want <capability>, so that <benefit>.

**Example**
A concrete case: who does what, and what they see.

**Suggestion**
The simplest approach you see, and what is out of scope.

**Done when**
What will be checked on the test server, as a real user would do it.
```

"Done when" names an end-to-end check, not "tests pass". If only a unit test can cover it (for example it needs a
fresh server), say so; the issue then gets `verified-partial` until someone runs the real check.

Keep it short. Link related issues and PRs by number. No credentials, hostnames from secrets, or customer data.

## Board

If you maintain the project boards, put the issue on the right board with its fields and parent issue, as
`.agents/environment.md` describes (boards, fields, parents per kind of work). Contributors without board access skip this: the
maintainers place the issue. Add a sub-issue with
`gh api -X POST repos/<owner>/<repo>/issues/<parent>/sub_issues -F sub_issue_id=<issue id>`.
