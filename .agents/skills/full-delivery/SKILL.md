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
- On the test server, agents operate fully as the test system administrator: approve and revoke app permissions,
  add users and change settings themselves, then report what they did (what, for which app or user, and why).
- Production is changed only when the owner asked for it in the task. Then agents do the administrator work
  themselves too: post in the issue or conversation what will change before doing it (no need to wait for an answer
  unless the step deletes data, grants administrator rights or locks people out), take a backup before anything
  disruptive, follow the production skills, check the public URLs afterwards and report what was done.
- Test only on the test server.
- Never print, commit or post credentials, tokens, API keys or generated passwords. Write them straight to files
  only the current user can read (passwords for test accounts go into the credentials file below).

## Test credentials

Credentials sit outside the repository in `~/.automaton-test/accounts.env` (on the test server:
`~/.config/automaton-test/accounts.env`, mode 600):

| Key | Meaning |
| --- | --- |
| `AUTOMATON_TEST_STUDIO_URL`, `AUTOMATON_TEST_API_URL` | Test server addresses |
| `AUTOMATON_TEST_ADMIN_EMAIL` / `_PASSWORD` | System administrator |
| `AUTOMATON_TEST_USER_EMAIL` / `_PASSWORD` | Regular user 1 |
| `AUTOMATON_TEST_USER2_EMAIL` / `_PASSWORD` | Regular user 2 |

Log in with `POST api/Account/LoginUser` (`{email, password}` → `accessToken`). Logins are rate limited per IP address
(5 a minute) and per account (10 per 15 minutes). Reuse tokens instead of logging in for every call, and on 429 wait
for the `Retry-After` header before trying again. For CLI work, create an API key
with `POST api/ApiKeys` and pass it with `AUTOMATON_SERVER` and `AUTOMATON_API_KEY`. Write it to a file, never to the
terminal. Delete keys you no longer need.

New regular users have no Runner. Apps run only on a Runner the deploying user can see: their personal Runners, or
the Runners attached to the app's project. To test as a regular user, put the app in a project that has a Runner, or
deploy as the administrator and use the regular user for the access checks.

For checks that need a plain Admin (not SysAdmin), create one as the system administrator with `POST api/admin/users`
and `"administrator": true`, store its email and generated password in the credentials file as
`AUTOMATON_TEST_PLAINADMIN_EMAIL` / `_PASSWORD`, and keep it for later runs.

## Steps

1. **Understand.** Read the issue, its parent and its blockers. Restate the done criteria. If a product decision is
   open, ask the owner before building.
2. **Branch and build.** Branch from `main`. Follow the area skills. Add tests for new behaviour, authorization
   included (who gets 200, 403, 404).
3. **Test locally.** Run the affected suites and, from `src/automaton-studio-vue`, the Studio type check
   (`npx vue-tsc --noEmit -p tsconfig.app.json`). Some tests already fail on `main` (#498), so run the same suites on
   `main`, compare the names of the failing tests, and report any new failure.
4. **Open the PR.** Close the issue from the PR body. Wait for CI. A check list that is still empty means the run has
   not started yet; it is not a pass.
5. **Review.** Get an independent read-only review (a fresh agent session with no stake in the change). Fix confirmed findings, take product questions to
   the owner, then review again until nothing above low severity is left.
6. **Merge.** Only with the owner's go-ahead. Asking for a delivery to the test server includes releasing and
   upgrading the test server; a production update needs its own request. Wait for the checks of the final commit, including a conflict-resolution
   commit, before merging. Squash-merge and delete the branch.
7. **Release.** After the merge, wait for the `Runner image` and `Build production images` workflows on `main` to
   succeed; a release pins the Runner image that exists at that moment. Then run
   `gh workflow run server-release.yml -f version=<next>` and wait for it and for `Pack CLI and Runner` to succeed.
   The CLI is published when it changed; NuGet indexing can take a while, so until then run the CLI from
   `src/Automaton.Cli`.
8. **Upgrade the test server.** Run `sudo bash /opt/automaton-server/update.sh --version server-v<next>` (without
   `--version` it installs the newest release it finds, which may be the previous one) and check that
   `/opt/automaton-server/version` shows the new version. When the Runner image changed, upgrade the Runners with the
   command Studio shows under **Runners → Upgrade runner**.
9. **Verify end to end as real users.** Use the administrator and regular test accounts against the test server API,
   the CLI and Studio:
   - the happy path of the feature;
   - each role boundary (a regular user and a stranger are refused, a plain Admin versus a SysAdmin);
   - error messages a person will see.
   Check pages in a browser: `agent-browser` (see `AGENTS.md`) on a server without a screen, or Claude in Chrome on
   the owner's PC, following the studio-design visual checklist. Record what passed and what you could not check.
10. **Close out.** Comment the results on the issue or PR, including every administrator action you took, open issues for gaps found while testing, update the board,
    and tell the owner what is left for them, with direct links.

## Handoff

Report as a list: what changed, test results compared with `main`, the release and the test-server version, the
end-to-end checks with pass or fail, and anything waiting for the owner.
