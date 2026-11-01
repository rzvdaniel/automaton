---
name: full-delivery
description: Use when taking a GitHub issue, bug or PR from start to finish without a human in the loop for each step - develop, review, merge, release, deploy to the test server and verify end to end as real users.
license: Apache-2.0
---

# Full delivery

Use this skill when the task is to deliver an issue or PR completely: code, review, merge, release, test-server
upgrade and an end-to-end check. Read the area skills from `AGENTS.md` as well; this skill only adds the delivery
loop around them.

## Ground rules

- Work items live in GitHub issues and the project board. Labels: `agent:ready` (may start), `agent:in-progress`,
  `agent:blocked` (say why in a comment), `agent:human-review` (needs the owner).
- Self sign-up is disabled on purpose. Never re-enable it, insert users into the database or bypass the invite
  flow. Test accounts are created by an administrator in **Admin → Users → Add user**, or through
  `POST api/admin/users`.
- Permission approval is a system administrator decision. An agent may prepare it and hand over the approval link,
  but does not approve on the owner's behalf unless the owner asks for that specific approval.
- Test only on the test server. Production changes follow the production skills, with a backup and a check of the
  public URLs afterwards.
- Never print, commit or post credentials, tokens or API keys. Keep them in files only the current user can read.

## Test credentials

Credentials sit outside the repository in `~/.automaton-test/accounts.env` (on the test server:
`~/.config/automaton-test/accounts.env`, mode 600):

| Key | Meaning |
| --- | --- |
| `AUTOMATON_TEST_STUDIO_URL`, `AUTOMATON_TEST_API_URL` | Test server addresses |
| `AUTOMATON_TEST_ADMIN_EMAIL` / `_PASSWORD` | System administrator |
| `AUTOMATON_TEST_USER_EMAIL` / `_PASSWORD` | Regular user 1 |
| `AUTOMATON_TEST_USER2_EMAIL` / `_PASSWORD` | Regular user 2 |

Log in with `POST api/Account/LoginUser` (`{email, password}` → `accessToken`). Logins are rate limited: on 429, wait
about 30 seconds and retry, and reuse tokens instead of logging in for every call. For CLI work, create an API key
with `POST api/ApiKeys` and pass it with `AUTOMATON_SERVER` and `AUTOMATON_API_KEY`. Write it to a file, never to the
terminal. Delete keys you no longer need.

New regular users have no Runner. Apps run only on a Runner the deploying user can see: their personal Runners, or
the Runners attached to the app's project. To test as a regular user, put the app in a project that has a Runner, or
deploy as the administrator and use the regular user for the access checks.

## Loop

1. **Understand.** Read the issue, its parent and its blockers. Restate the done criteria. If a product decision is
   open, ask the owner before building.
2. **Branch and build.** Branch from `main`. Follow the area skills. Add tests for new behaviour, authorization
   included (who gets 200, 403, 404).
3. **Test locally.** Run the affected suites and the Studio type check (`npx vue-tsc --noEmit -p tsconfig.app.json`).
   Some API and Runner tests already fail on `main`, so compare the failures with `main` instead of expecting zero.
   Report any new failure.
4. **Open the PR.** Close the issue from the PR body. Wait for CI. A check list that is still empty means the run has
   not started yet; it is not a pass.
5. **Review.** Ask a fresh reviewer agent for a read-only review. Fix confirmed findings, take product questions to
   the owner, then review again until nothing above low severity is left.
6. **Merge.** Only with the owner's go-ahead. Wait for the checks of the final commit, including a conflict-resolution
   commit, before merging. Squash-merge and delete the branch.
7. **Release.** `gh workflow run server-release.yml -f version=<next>`. The CLI is published with the release when it
   changed. NuGet indexing can take a while; until then, run the CLI from `src/Automaton.Cli`.
8. **Upgrade the test server.** Run its `update.sh` and check the version file. Upgrade its Runners with the command
   Studio shows under **Runners → Upgrade runner** when the Runner image changed.
9. **Verify end to end as real users.** Use the administrator and regular test accounts against the test server API,
   the CLI and Studio:
   - the happy path of the feature;
   - each role boundary (a regular user and a stranger are refused, a plain Admin versus a SysAdmin);
   - error messages a person will see.
   Use the browser for pages. Record what passed and what you could not check.
10. **Close out.** Comment the results on the issue or PR, open issues for gaps found while testing, update the board,
    and tell the owner what is left for them, with direct links.

## Handoff

Report as a list: what changed, test results compared with `main`, the release and the test-server version, the
end-to-end checks with pass or fail, and anything waiting for the owner.
