# Pull request

## Title

What the change does for people, in the imperative: "Let app owners delete apps after losing Runner access (#533)".

## Body

```markdown
Refs #<issue>.

- **<behaviour>:** what changed and why, one line each.
- **Docs:** which Admin or architecture docs were updated, or why none were needed.
- **Tests:** what was added, and what ran (project names, pass counts).

**Not in this PR:** anything from the issue left for later, with the follow-up issue number.
```

Never write `Closes #N` or `Fixes #N`: an issue closes only when its change is live on production (step 8). Use
`Part of #N` when the PR delivers only part of the issue.

## Documentation

Update documentation in the same PR when behaviour that administrators or users see changes (admin-docs skill), or
when architecture changes (`AGENTS.md`). Keep it brief: say what is true now, in a few sentences, in the existing
page. Do not add new pages, change logs or plans unless the task asks for them.

## Commits

Small commits are fine; the PR is squash-merged. End commit messages and the PR body with the attribution lines
your tool asks for.
