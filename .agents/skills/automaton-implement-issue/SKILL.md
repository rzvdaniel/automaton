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

1. **Issue.** Find the issue. If there is none, create it first (`issue.md`). If no task was given at all, take the
   open Todo item with the highest priority on the board and say which one you took. Read its parent and blockers, restate
   the done criteria, and set the board Status to In Progress.
2. **Build.** Branch from `main`. For a bug, reproduce it first on the surface where it shows (API, CLI or Studio),
   then commit a failing test before the fix; afterwards the original reproduction must pass.
   Follow `AGENTS.md` (keep it simple, maintainability guardrails) and the area skills. Add tests for new behaviour, including who gets 200, 403 and 404.
3. **Test locally.** Run the affected test projects and, for Studio changes, `npx vue-tsc --noEmit -p tsconfig.app.json`
   in `src/automaton-studio-vue`. `main` is green, so every failure is yours to fix. Never skip or delete a failing
   test to get green unless the test itself is wrong, and say so in the PR.
4. **PR.** Open it as `pr.md` describes and wait for CI. An empty check list means the run has not started; it is not
   a pass.
5. **Review.** Get an independent review from a subagent (`review.md`). At most two rounds.
6. **Merge.** Squash-merge and delete the branch when the final commit's checks pass, the last review found nothing
   above low severity, and you are satisfied. Ask the owner first only for:
   - database migrations that drop or rewrite data;
   - changes that let more people do something (widening access), or change how people sign in;
   - anything that touches production;
   - anything the owner said to hold.
7. **Deliver and verify.** Release, upgrade the test server and verify as real users (`verify.md`). Several small,
   independent changes may share one release; risky ones (Runner, migrations, install or update scripts) go alone.
8. **Close out.** The PR's `Closes #N` closes the issue on merge. Attach the `proof` artifact (`verify.md`), comment
   the end-to-end results (pass or fail per check) and every administrator action on the PR, and link that comment from
   the issue. Remove the `e2e-` items you created (`verify.md`), set the board Status to Done, open issues for gaps you
   found (`issue.md`), and tell the owner what is left for them, with links. If testing showed the docs are wrong, or
   the issue completes a roadmap step, fix the docs or the status in `docs/roadmap/README.md` in a small follow-up PR.

## Handoff

A short list: what changed, tests, the release and test-server version, whether the `e2e-` items were removed, and
anything waiting for the owner. For each end-to-end check, give what was run and what came back (status code,
command output or screenshot path). Mark anything you did not check as "not checked", with the reason.
Do not keep an issue open for checks you can run yourself on the test server (browser, Studio buttons, screenshots).
Only something that truly needs the owner stays, as its own issue linked from the handoff.
