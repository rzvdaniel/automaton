---
name: pr-review
description: Use when reviewing a pull request, your own or another agent's or a person's, and before merging any PR. Covers the risk tier, the review and security pass, how findings are posted and answered, and the merge gate.
---

# PR review

The question is not "did someone read it" but "what could go wrong with this change, and which check catches it".
The change sets how much review it gets, not its author.

## Risk tier

| Tier | The change | Before merge |
| --- | --- | --- |
| Low | Only documentation for people: Markdown under `docs/`, the Admin docs, the root `README.md`. Skills, `AGENTS.md`, templates and guidance shipped to users' agents steer agents and are medium. | Green CI. The author re-reads the diff against the code it describes. |
| Medium | Everything else | Green CI and one independent review. Paths below the "security pass" marker in [`.github/CODEOWNERS`](../../../.github/CODEOWNERS) (workflows, install and update scripts) also need a security pass. |
| High | Touches a path above that marker (sign-in, tokens, authorization, secrets, the review rules), or the reviewer finds that it drops or rewrites data, lets more people do something, changes how people sign in, or touches production | Green CI, the review, a separate security pass, and the owner's approval (PR label `owner-approved`). |

The reviewer may raise the tier, never lower it. An agent's approval never stands in for the owner's: only the owner
adds `owner-approved`, or tells an agent in the session to add it for that PR. `agent:human-review` on the issue means
"waiting for the owner"; it is not the approval.

## Review

The reviewer is a fresh agent that did not write the change and starts with none of the author's context. Give it the
issue number and its "Done when", the PR number, [`REVIEW.md`](../../../REVIEW.md) and the area skills that apply. It
reads the diff against the code around it, not the diff alone, and asks whether the change does what the issue asks,
fits the system and breaks anything.

**Security pass.** Looks only for security problems (in Claude Code: `/security-review`). For the high tier it is a
second fresh agent. For a medium change it is part of the same review (one agent, one more section), required on
the security-pass paths and whenever the change adds or changes an endpoint, a permission check, secret handling, a
file path or shell command built from input, or anything an app or anonymous visitor can reach.

**Confirm before posting.** For each Important finding, reproduce it when that is cheap (a failing test, a command and
its output); otherwise say what would confirm it and how sure you are. Drop guesses. A wrong finding costs a commit and
another round.

**Keep it cheap.** The reviewer reads the diff and the files it touches, not the whole repository. Reviews and
security passes run on Opus 5.5; a Nit-only re-check or a docs-only diff can use Sonnet 5.5. A re-review after Nit-only fixes is not needed; the gate still wants the last review to cover
the final commit, so post a short review on it with the same first lines as before (verdict, `Tier:`, and `Security:`
when the change needed one) plus "Nits fixed, no new code". A tier a reviewer raised stays raised.

## Post the result

Post a formal GitHub review, not a conversation comment; the merge gate reads the review:

```bash
gh pr review <N> --request-changes --body-file review.md   # any Important finding
gh pr review <N> --approve --body-file review.md           # none
gh pr review <N> --comment --body-file review.md           # your own PR: GitHub refuses the two above
```

The body starts like this, then lists the findings in the `REVIEW.md` format:

```markdown
APPROVE            (or CHANGES NEEDED)
Tier: medium - ordinary Studio change
Important: 0, Nit: 2, Pre-existing: 1
Security: checked who can call the new endpoint / not needed, no reachable surface
Not checked: the update script was not run
```

## Answer and re-review

- The author answers every Important finding: a fix, or a reply saying why it is wrong or intended.
- A re-review covers only the commits pushed since the last one and does not repeat earlier findings. Any new commit
  needs it: the review must cover the final commit.
- After two rounds, stop. More rounds mostly find old bugs or mean the approach is wrong. Write what is left on the
  PR, label the issue `agent:blocked`, and take other work.

## Straight to `main`

Low-tier changes (documentation for people) may be pushed to `main` without a PR. So may agent guidance (`AGENTS.md`,
skills, templates) when the owner asks for it in the session, except paths in the high-tier list of
[`.github/CODEOWNERS`](../../../.github/CODEOWNERS) (the review rules themselves): those always go through a PR. Before pushing, run `node scripts/check-doc-links.mjs`
and, for agent guidance, `dotnet test src/UnitTests/Automaton.Api.Tests --filter RepositoryAgentGuidanceTests`;
afterwards watch the `main` run for the commit. Everything else goes through a PR and the gate.

## Merge gate

```bash
python3 .agents/tools/merge-gate.py <N>
```

It checks from GitHub that CI is green on the final commit, the last review (from an account with write access;
a later review replaces an earlier one) approves that commit, a high-tier change
has its security pass and the owner's approval, and the description says how the change was verified and how to
revert it. Merge only on `MERGE GATE: PASS`, and paste the output in a PR comment. Read its warnings (no test in the
diff, comments newer than the last commit) and act on them.

Squash-merge and delete the branch. Then watch the `main` run for your merge commit: two PRs that pass alone can
break together. A red `main` is fixed before anything else merges.

If a PR was merged with open findings, say so on the PR, fix them in a follow-up PR with its own review, and hold any
release that would ship the problem.

## When something gets past review

When a check after release fails, or production shows a bug, ask which step should have caught it and fix that step
in the same follow-up PR: a regression test first, else a line in `REVIEW.md`, an analyzer rule, or a sentence in the
skill that was unclear.
