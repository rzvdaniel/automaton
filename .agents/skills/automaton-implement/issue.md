# Creating an issue

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
```

For a feature:

```markdown
**Goal**
As a <role>, I want <capability>, so that <benefit>.

**Example**
A concrete case: who does what, and what they see.

**Suggestion**
The simplest approach you see, and what is out of scope.

**Acceptance**
What will be checked on the test server.
```

Keep it short. Link related issues and PRs by number. No credentials, hostnames from secrets, or customer data.

## Board

Add the issue to the board and set the fields:

```sh
gh project item-add 1 --owner rzvdaniel --url <issue-url>
```

Then set Status (Todo, or In Progress if you start now), Priority (P1 next, P2 soon, P3 later) and Plan when one fits,
with `gh project item-edit`. If it belongs under a parent issue, add it as a sub-issue
(`gh api -X POST repos/rzvdaniel/automaton/issues/<parent>/sub_issues -F sub_issue_id=<issue id>`).
