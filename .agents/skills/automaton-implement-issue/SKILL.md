---
name: automaton-implement-issue
description: Use when implementing a feature or fixing a bug in Automaton from the issue to a merged pull request - claim, branch, tests, evidence, PR, review, merge - and then handing over to (or running) the release check, with as few owner interruptions as possible.
license: Apache-2.0
---

# Automaton implement issue

One agent takes one issue from request to a merged pull request, then delivers it or hands it to the verifier. Read
the area skills that `AGENTS.md` lists for the code you touch; this skill is only the loop around them.

## Ground rules

- **Decide, do not wait.** Ask the owner only for product decisions and for high-tier changes
  ([PR review](../pr-review/SKILL.md)). If running something would answer the question (behaviour, output, timing,
  layout), run it and decide from the result.
- **When you do need the owner, do not stop.** Post the question on the issue with the options and the one you would
  pick, label the issue `agent:blocked`, and continue with everything that does not depend on the answer. If nothing
  is left, take the next issue.
- **Never end your turn on a wait.** Wait for CI and releases in the foreground (`gh pr checks <N> --watch`,
  `gh run watch`). A turn that ends while a background job runs leaves the task idle until someone notices.
- **Others work beside you.** Use your own worktree and branch. Touch only the files your issue needs. Do not upgrade
  the test server or cut a release while an orchestrator is running a batch.
- The sign-up, server, secrets and production rules in `AGENTS.md` apply at every step.

## Steps

Create a to-do item for each step and tick them off.

1. **Claim.** Find the issue, or create it first ([write-issue skill](../automaton-write-issue/SKILL.md)). With no
   task given, take the highest-priority open issue labelled `agent:ready` that is not `agent:in-progress`. Add
   `agent:in-progress`, set the board Status to In Progress, and comment your branch name; skip an issue another
   agent already claimed. Read its parent and blockers and restate its "Done when".
2. **Build.** Branch from `main`. For a bug, reproduce it first on the surface where it shows (API, CLI or Studio)
   and commit a failing test before the fix. Add tests for new behaviour, including who gets 200, 403 and 404.
3. **Show it works.** Run the affected test projects and, for Studio, `npx vue-tsc --noEmit -p tsconfig.app.json` in
   `src/automaton-studio-vue`. `main` is green, so every failure is yours; never skip or delete a failing test unless
   the test itself is wrong, and say so in the PR. Keep the evidence for the PR: the commands and what came back, for
   a bug the test that failed before and passes now, for a UI change a screenshot of this branch running locally.
   "Done" without output is a claim, not evidence.
4. **PR.** Open it as the [write-PR skill](../automaton-write-pr/SKILL.md) describes, including the "Check after
   release" list. Start step 5 while CI runs. An empty check list means the run has not started; it is not a pass.
5. **Review.** Follow the [PR review skill](../pr-review/SKILL.md): tier, independent review, security pass where it
   applies, at most two rounds. For a high-tier change, add the `agent:human-review` label and ask the owner now, not
   after the review, so the two waits overlap.
6. **Merge.** Run `python3 .agents/tools/merge-gate.py <N>`; on PASS squash-merge, delete the branch and watch the
   `main` run for your commit. Also hold anything the owner said to hold.
7. **Deliver.** Follow the [verify-release skill](../automaton-verify-release/SKILL.md).
   - *Working alone:* release, upgrade the test server, run your "Check after release" list as real users, attach
     the proof and label the issue.
   - *In a batch* (the orchestrator said so): stop after the merge. The verifier runs your list on the batch release.
     Your handoff says the PR is merged and waiting for the batch check.
8. **Hand off.** Remove `agent:in-progress` when the issue is labelled `verified` or handed to the verifier. Open
   issues for gaps you found. If the issue completes a roadmap step or testing showed the docs are wrong, fix
   `docs/roadmap/README.md` or the docs in a small follow-up PR.

## Handoff

A short list: what changed, the PR, the merge-gate output, tests run and their counts, the release and proof (or
"waiting for the batch check"), and anything waiting for the owner, with links. Mark what you did not check as "not
checked", with the reason. Do not leave an issue open for a check you can run yourself.
