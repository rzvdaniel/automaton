---
name: automaton-write-pr
description: Use whenever you open a pull request in Automaton or automaton-apps, from any flow, so every PR says what changed, how it was verified, how to revert it, what to check after release, and never closes its issue before production.
license: Apache-2.0
---

# Writing a pull request

## Title

What the change does for people, in the imperative: "Let app owners delete apps after losing Runner access (#533)".

## Body

Fill in [`.github/pull_request_template.md`](../../../.github/pull_request_template.md) and pass it with
`gh pr create --body-file`. The merge gate refuses a PR without "How verified", "Risk" and "How to revert".

- **How verified** is evidence, not a claim: the commands and what came back. For a bug, name the test that failed
  before the fix. For a Studio or website change, link a screenshot (push it to the `pr-screenshots` branch).
- **Check after release** is what the verifier will run on the test server, written so an agent that never saw your
  work can follow it: who signs in, what they do, what they see. It comes from the issue's "Done when".

Never write `Closes #N` or `Fixes #N`: an issue closes only when its change is live on production. Use
`Part of #N` when the PR delivers only part of the issue.

## Documentation

Update documentation in the same PR when behaviour that administrators or users see changes (admin-docs skill), or
when architecture changes (`AGENTS.md`). Keep it brief: say what is true now, in a few sentences, in the existing
page. Do not add new pages, change logs or plans unless the task asks for them.

## Commits

Small commits are fine; the PR is squash-merged. End commit messages and the PR body with the attribution lines
your tool asks for.
