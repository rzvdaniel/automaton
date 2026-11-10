---
name: automaton-implement-issue
description: Use when implementing a feature or fixing a bug in Automaton from start to finish - issue, branch, tests, PR, review, merge, release, test-server upgrade and end-to-end verification as real users, with as few owner interruptions as possible.
license: Apache-2.0
---

# Automaton implement issue

One agent takes a feature or bug from request to verified on the test server. Read the area skills that
`AGENTS.md` lists for the code you touch; this skill is only the delivery loop around them. Open the detail file for
a step when you reach it.

## Ground rules

- Work items live in GitHub issues on the project board (https://github.com/users/rzvdaniel/projects/1), with
  Status, Priority and Plan fields.
- Ask the owner only for product decisions and for the changes listed under step 6. Decide everything else yourself
  and report it.
- Before asking the owner, check whether running something would answer it (behaviour, output, timing, layout). If
  so, run it and decide from the result. Ask only real product or preference questions.
- Self sign-up is disabled on purpose. Never re-enable it, insert users into the database or bypass the invite flow.
- Test only on the test server. Production is changed only when the owner asked for it in the task; then follow
  `verify.md` → Production.
- Never print, commit or post credentials, tokens, API keys or generated passwords.

## Steps

Before starting, create a to-do item for each of steps 1–8, naming its detail file where it has one. Tick them off
as you go; the task is not done until step 8 is.

1. **Issue.** Find the issue. If there is none, create it first ([write-issue skill](../automaton-write-issue/SKILL.md)). If no task was given at all, take the
   open Todo item with the highest priority on the board and say which one you took. Read its parent and blockers, restate
   the done criteria, and set the board Status to In Progress.
2. **Build.** Branch from `main`. For a bug, reproduce it first on the surface where it shows (API, CLI or Studio),
   then commit a failing test before the fix; afterwards the original reproduction must pass.
   Follow `AGENTS.md`, the maintainability skill (keep it simple, guardrails) and the area skills. Add tests for new behaviour, including who gets 200, 403 and 404.
3. **Test locally.** Run the affected test projects and, for Studio changes, `npx vue-tsc --noEmit -p tsconfig.app.json`
   in `src/automaton-studio-vue`. `main` is green, so every failure is yours to fix. Never skip or delete a failing
   test to get green unless the test itself is wrong, and say so in the PR.
4. **PR.** Open it as the [write-PR skill](../automaton-write-pr/SKILL.md) describes and wait for CI. An empty check list means the run has not started; it is not
   a pass.
5. **Review.** Get an independent review from a subagent (`review.md`). At most two rounds.
6. **Merge.** Run the merge gate (below), then squash-merge and delete the branch. Ask the owner first only for:
   - database migrations that drop or rewrite data;
   - changes that let more people do something (widening access), or change how people sign in;
   - anything that touches production;
   - anything the owner said to hold.
7. **Deliver and verify.** Release, upgrade the test server and verify as real users (`verify.md`). Several small,
   independent changes may share one release; risky ones (Runner, migrations, install or update scripts) go alone.
   Real-server testing happens here, after merge. If a check fails, fix forward: a small follow-up PR (same review and
   CI), a new release and a re-check. Revert instead only when the fix is not quick or the failure blocks other work on
   the test server. Production is updated only after the proof passes, and only when the owner asks.
8. **Close out.** Done means live on production. Attach the `proof` artifact to the issue and set its label
   (`verified`, `verified-partial` or `verification-failed`, see Labels below and `verify.md`), comment the end-to-end results (pass or fail per check) and every
   administrator action on the PR, and link that comment from the issue. Leave the issue open with board Status In
   Progress: `is:open label:verified` is what the next production update ships. When the owner updates production,
   whoever updates it checks production, runs the close gate, adds `live` and closes each issue in that release with a
   comment naming the version, which sets the board Status to Done. Remove the `e2e-` items you created (`verify.md`),
   open issues for gaps you found (write-issue skill), and tell the owner what is left for them, with links. If testing showed the docs are wrong,
   or the issue completes a roadmap step, fix the docs or the status in `docs/roadmap/README.md` in a small follow-up
   PR.

## Gates

Check each item from GitHub itself (`gh pr view`, `gh pr checks`, `gh api .../issues/N/artifacts`), not from memory,
and paste the result into your handoff. A missing item stops the step; fix it or report it as a gap. The Flow check
app (#693) automates this list once it exists.

**Merge gate** (step 6):
- a formal review (`gh pr review`) from an independent reviewer, covering the final commit; if you pushed fixes after
  it, the reviewer saw them (a second round) or they only touch text the review asked for;
- the last review found nothing above low severity;
- the PR diff contains the tests the PR claims (`gh pr diff --name-only`);
- CI passed on the final commit.

**Close gate** (production update, step 8):
- the merge gate held;
- the issue has a `proof` artifact naming the release and how it was checked;
- the label is `verified` (a `verified-partial` issue closes only when the owner accepts the gap, said in a comment);
- the production version contains the merge commit.

## Labels

- `verified`: every "Done when" check passed end to end on the test server, with a proof artifact.
- `verified-partial`: only unit tests or part of the checks ran; the proof says what is missing and why.
- `verification-failed`: a check failed; fix forward.
- `live`: set by whoever updates production, just before closing.

## Handoff

A short list: what changed, tests, the release and test-server version, whether the `e2e-` items were removed, and
anything waiting for the owner. For each end-to-end check, give what was run and what came back (status code,
command output or screenshot path). Mark anything you did not check as "not checked", with the reason.
Do not keep an issue open for checks you can run yourself on the test server (browser, Studio buttons, screenshots).
Only something that truly needs the owner stays, as its own issue linked from the handoff.

Proof cost: viewing an image costs about 1,500 tokens and stays in your context. So:
- Use text proofs (status code, response body, page text) for functional checks. Use screenshots only for visual changes (website, Studio, app design).
- Capture the viewport, not the full page, unless the whole page's layout is the point.
- Look at each screenshot once to confirm it shows the right thing, then post it. Reviewers and the orchestrator do not reopen it unless something looks wrong.
