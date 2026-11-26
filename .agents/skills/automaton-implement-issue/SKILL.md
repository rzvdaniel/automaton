---
name: automaton-implement-issue
description: Use when implementing a feature or fixing a bug in Automaton from the issue to a merged pull request - claim, branch, tests, evidence, PR, review, merge - and then handing over to (or running) the release check, with as few owner interruptions as possible.
license: Apache-2.0
---

# Automaton implement issue

One agent takes one issue from request to a merged pull request, then delivers it or hands it to the verifier. Read
the area skills that `AGENTS.md` lists for the code you touch; this skill is only the loop around them.

## Ground rules

- **Decide, do not wait.** Ask the owner only for product decisions. High-tier changes merge through the gate like any other
  ([PR review](../pr-review/SKILL.md)); the production deploy needs the owner. If running something would answer the question (behaviour, output, timing,
  layout), run it and decide from the result.
- **When you do need the owner, do not stop.** Post the question on the issue with the options and the one you would
  pick, label the issue `agent:blocked`, and continue with everything that does not depend on the answer. If nothing
  is left, take the next issue.
- **Spend tokens on the change, not on searching.** Start from the files and facts the task names; read files in
  parts; don't print large logs, whole transcripts or lock files into the conversation.
- **Never end your turn on a wait.** Wait for CI and releases in the foreground (`gh pr checks <N> --watch`,
  `gh run watch`). A turn that ends while a background job runs leaves the task idle until someone notices.
  Do the work itself in the foreground too: if you hand a part to a subagent, wait for its result before you end. A run
  that ends while a background subagent works finishes with nothing done, and the card still moves to Verify (#1625).
- **Others work beside you.** Use your own worktree and branch. Touch only the files your issue needs. Do not upgrade
  the test server or cut a release while an orchestrator is running a batch.
- **Destructive scripts run only in a throwaway place.** Test anything that deletes, uninstalls, restores or prunes in a
  throwaway container or on runner-02, never on a host with a live server (check `docker` is not the real one).
- **Stop only what you started.** Kill the PIDs you launched (mock servers, dev servers), never by image name
  (`taskkill /IM python.exe`, `pkill python`): that kills every such process on the host, including the owner's.
- **Report what you break first.** If a command changed or broke anything outside the task, say so first in the handoff;
  a fix does not replace the report.
- The sign-up, server, secrets and production rules in `AGENTS.md` apply at every step.

## Steps

Create a to-do item for each step and tick them off.

1. **Claim.** Find the issue, or create it first ([write-issue skill](../automaton-write-issue/SKILL.md)). With no
   task given, take the highest-priority open issue labelled `agent:ready` that is not `agent:in-progress`. If it has
   the `needs-shape` label, stop before claiming: do not build or add `agent:in-progress`; comment what is missing (the
   issue-check comment names it), report it in the handoff, and build only once the issue is fixed and the label is
   gone. Otherwise add
   `agent:in-progress`, set the board Status to In Progress, and comment your branch name; skip an issue another
   agent already claimed. Read its parent and blockers and restate its "Done when".
2. **Build.** Branch from `main`. For a bug, reproduce it first on the surface where it shows (API, CLI or Studio)
   and commit a failing test before the fix. Add tests for new behaviour, including who gets 200, 403 and 404.
3. **Show it works.** Run the affected test projects and, for Studio, `npx vue-tsc --noEmit -p tsconfig.app.json` in
   `src/automaton-studio-vue`. `main` is green, so every failure is yours; never skip or delete a failing test unless
   the test itself is wrong, and say so in the PR. Keep the evidence for the PR: the commands and what came back, for
   a bug the test that failed before and passes now, for a UI change a screenshot of this branch running locally.
   "Done" without output is a claim, not evidence.
   **Before you push**, run the checks CI runs that a plain build skips (#1736), picked from the changed paths:
   - C# (`src/**/*.cs`): `dotnet build Automaton.All.sln -c Debug --no-incremental -v minimal` (CA1502 complexity 15 and
     other analyzer errors fail Tests and the CLI binary builds), then
     `dotnet test src/UnitTests/Automaton.<Project>.Tests -p:WarningsAsErrors=nullable%3BNU1902%3BNU1903%3BNU1904`
     with the flags from `.github/workflows/tests.yml`.
   - Website (`src/automaton-vue/`): `npm run build` there (runs `vue-tsc -b`, the strict type check the image build uses).
     Studio: `npx vue-tsc --noEmit -p tsconfig.app.json` and `npm run lint:check -- --quiet`.
   - Guidance seeds (`src/Automaton.AiGuidance/`, its embedded seed files): run
     `dotnet test src/UnitTests/Automaton.Api.Tests --filter AiGuidanceSeedExportTests`. If
     `GuidanceOutput_IsByteForByteTheOutputOfTheDatabaseBackedRuntime` fails after an intended change, copy the new SHA-256
     values from its failure output into the `expected` list in `AiGuidanceSeedExportTests.cs`.
   - Docs (`src/automaton-vue/docs`, `docs/`): `node scripts/check-doc-links.mjs`.
4. **Docs.** Find every user page the change makes wrong: search `src/automaton-vue/docs` for the feature's words
   (commands, labels, messages). Update them in this PR and name them on the Docs line; "none" only with a real reason.
   An app's own `docs/` (apps repository) are part of that app's change too. There is no separate docs walk before a release.
5. **PR.** Open it as the [write-PR skill](../automaton-write-pr/SKILL.md) describes, including the "Check after
   release" list. Start step 6 while CI runs. An empty check list means the run has not started; it is not a pass.
6. **Review.** Follow the [PR review skill](../pr-review/SKILL.md): tier, independent review, security pass where it
   applies, at most two rounds. For a high tier, spawn a separate `automaton-reviewer` subagent for the security pass; it
   posts with a different login than the main review (pr-review, Different logins).
   Start every review and security pass as the `automaton-reviewer` agent
   ([definition](../../../.claude/agents/automaton-reviewer.md)), never a generic agent. Before posting, pick the login you will
   post with (`gh auth status`) and compare it with the logins that already reviewed the PR: the main review and the security pass must
   come from different logins, and a re-review after a rebase or fixes counts as a main review. If you would collide,
   stop and report which other login must post.
7. **Merge.** Run `python3 .agents/tools/merge-gate.py <N> --merge`; it squash-merges and deletes the branch only on
   PASS. Never run `gh pr merge` yourself. Then watch the `main` run for your commit. Also hold anything the owner said to hold.
8. **Deliver.** Follow the [verify-release skill](../automaton-verify-release/SKILL.md).
   - *Working alone:* release, upgrade the test server, run your "Check after release" list as real users, attach
     the proof and label the issue.
   - *In a batch* (the orchestrator said so): stop after the merge. The verifier runs your list on the batch release.
     Your handoff says the PR is merged and waiting for the batch check.
9. **Hand off.** Remove `agent:in-progress` when the issue is labelled `verified` or handed to the verifier. Open
   issues for gaps you found. If the issue completes a roadmap step or testing showed the docs are wrong, fix
   `docs/roadmap/README.md` or the docs (user pages in `src/automaton-vue/docs`) in a small follow-up PR.

## Handoff

A short list: what changed, the PR, the merge-gate output, tests run and their counts, the release and proof (or
"waiting for the batch check"), and anything waiting for the owner, with links. Mark what you did not check as "not
checked", with the reason. Do not leave an issue open for a check you can run yourself.
