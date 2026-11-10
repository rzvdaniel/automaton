---
name: automaton-verify-release
description: Use after pull requests are merged, to release, upgrade the test server, check each change as real users, attach the proof to each issue and label it - for one change or a whole batch. Also holds the labels, the close gate and the test accounts.
license: Apache-2.0
---

# Automaton verify release

Merged is not done. This skill takes merged work to "proved on the test server": one agent's own change, or a batch
of several agents' changes checked by one verifier.

## Where to test

All end-to-end checks run on the test server named in `.agents/environment.md`; the exact URLs are in `accounts.env` below.
An agent running on that machine is already on the test server. Never test on production, and never use a local stack in place of the end-to-end check.

The test server is shared. Only one agent at a time releases and upgrades it: the verifier of a batch, or an agent
working alone. Everyone else waits for that release instead of cutting another.

## One change or a batch

- **One change:** release, upgrade, run the PR's "Check after release" list, proof, label.
- **A batch:** list the PRs merged since the last release (`gh pr list --state merged --search "merged:>=<date>"`),
  release once, upgrade once, then run each PR's "Check after release" list and write one proof per issue. A list
  that is missing or too vague to follow is a finding on that PR; write the checks from the issue's "Done when".
- Risky changes (Runner, migrations, install or update scripts) get a release of their own, so a failure points at
  one change.
- **When a check fails:** label that issue `verification-failed` and fix forward with a small follow-up PR (same
  review and CI), a new release and a re-check; the other issues in the batch keep their results. Revert instead only
  when the fix is not quick or the failure blocks other work on the test server. The follow-up PR also fixes the step
  that should have caught it ([PR review](../pr-review/SKILL.md), last section).

## Release

After the merge, wait for the `Runner image` and `Build production images` workflows on `main` to succeed; a release
pins the Runner image that exists at that moment. Then run `gh workflow run server-release.yml -f version=<next>` and
wait for it; its `cli` job also waits for `Pack CLI and Runner` when the CLI changed. Until NuGet indexes it, run the CLI
from `src/Automaton.Cli`.

## Upgrade the test server

Run `sudo bash /opt/automaton-server/update.sh --version server-v<next>` (without `--version` it may install the
previous release) and check `/opt/automaton-server/version`. When the Runner image changed, upgrade the Runners with
the command Studio shows under **Runners → Upgrade runner**.

## Test accounts

Credentials sit outside the repository in an `accounts.env` file (mode 600) at the path `.agents/environment.md` names:

| Key | Meaning |
| --- | --- |
| `AUTOMATON_TEST_STUDIO_URL`, `AUTOMATON_TEST_API_URL` | Test server addresses |
| `AUTOMATON_TEST_ADMIN_EMAIL` / `_PASSWORD` | System administrator |
| `AUTOMATON_TEST_USER_EMAIL` / `_PASSWORD` | Regular user 1 |
| `AUTOMATON_TEST_USER2_EMAIL` / `_PASSWORD` | Regular user 2 |
| `AUTOMATON_TEST_PLAINADMIN_EMAIL` / `_PASSWORD` | Plain Admin (not SysAdmin), when one was created |

Log in with `POST api/Account/LoginUser` (`{email, password}` → `accessToken`). Logins are rate limited (5 a minute
per IP, 10 per 15 minutes per account): reuse tokens, and on 429 wait for `Retry-After`. For CLI work create an API
key with `POST api/ApiKeys`, pass it with `AUTOMATON_SERVER` and `AUTOMATON_API_KEY`, keep it in a file, and delete it
afterwards.

New accounts are created by the system administrator in **People → Invite people** (or **Create with password**) or `POST api/admin/users`, with
`"mustChangePassword": false`; add `"administrator": true` for a plain Admin and store its password in the
credentials file. On the test server agents act fully as the administrator (approve permissions, add users, change
settings) and report what they did.

New regular users have no Runner. To test as a regular user, put the app in a project that has a Runner, or deploy as
the administrator and use the regular user for the access checks.

## Test inbox (emails)

Studio on the test server sends all mail to a catch-all [Mailpit](https://mailpit.axllent.com) container
(`automaton-test-mailpit`, SMTP `automaton-test-mailpit:1025` on the `automaton-server_default` network, Studio's SMTP
settings point at it). No mail leaves the server, so invite, reset and alert emails for `e2e-` users never reach a real
inbox. Use `@example.test` addresses. The API is bound to the server's localhost only; on the test server:

```bash
curl -s localhost:8025/api/v1/messages                     # list: .messages[] {ID, Subject, To, Created}
curl -s localhost:8025/api/v1/message/<ID>                 # one mail: .Text, .HTML
curl -s "localhost:8025/api/v1/search?query=to:e2e-x@example.test"
curl -s -X DELETE localhost:8025/api/v1/messages           # empty the inbox
```

From another machine use `ssh -L 8025:localhost:8025 <test-server>`; the UI is then at `http://localhost:8025`.
Quote the mail (subject, recipient, link) in the proof, but not one-time links or passwords. If the container is gone, recreate it:
`docker run -d --name automaton-test-mailpit --restart unless-stopped --network automaton-server_default -p 127.0.0.1:8025:8025 -e MP_SMTP_AUTH_ACCEPT_ANY=1 -e MP_SMTP_AUTH_ALLOW_INSECURE=1 axllent/mailpit`,
then in Admin → Settings → SMTP (or `PUT api/admin/installation/smtp`) set host `automaton-test-mailpit`, port 1025, SSL off,
any account and password. Test server only; never point production at it.

## Verify as real users

Against the test server API, the CLI and Studio, check:

- the happy path of the change;
- each role boundary: a regular user and a stranger are refused, a plain Admin versus a SysAdmin;
- the error messages a person will see.

Check pages in a browser yourself with `agent-browser` (see the studio-design skill, "Browser automation"; login and screenshot helpers, if your test server has them, are listed in `.agents/environment.md`), following the studio-design visual checklist. When the change is a Studio
action (a button, a form), do it by clicking in Studio with `agent-browser`, not through the API. Record what passed
and what you could not check.

Proof on the issue: GitHub shows artifacts on issues, not on pull requests. Write the proof as one HTML page and
publish it on the **issue** the PR closes with the helper, then comment its link on the PR:

```bash
python3 .agents/skills/automaton-verify-release/proof.py <issue> proof.html
```

The page lists each done criterion from the issue, the evidence for it (the command and what came back, or a link
to a screenshot) and pass, fail or not checked with the reason. Keep it plain: one `<table>` (criterion, result,
evidence), inline `<style>` only, no scripts, no embedded images. Running `proof.py` again replaces the issue's `proof`.

Then label the issue (see Labels below) and swap the label when a fix-forward release passes its re-check.

Proof cost: viewing an image costs about 1,500 tokens and stays in your context. Use text proofs (status code,
response body, page text) for functional checks and screenshots only for visual changes. Capture the viewport, not
the full page, unless the whole layout is the point. Look at each screenshot once to confirm it shows the right
thing, then post it.

Screenshots: push them to the orphan branch `pr-screenshots` under `pr-<N>/` (create it with
`git switch --orphan pr-screenshots` if it does not exist; never merge or delete it). Link them, do not embed them:
`https://raw.githubusercontent.com/rzvdaniel/automaton/pr-screenshots/pr-<N>/name.png`. The same links work in the
proof, PR comments and issue comments (in Markdown, `![name](link)` also shows the image there). The artifact preview
does not load images from other sites, so in the proof they are plain links (it also blocks a plain click: right-click,
Open in new tab; `proof.py` adds that hint). Also list the screenshot links in the close-out comment on the issue,
where a normal click opens them.
The repository is public: use only `e2e-` test data and no passwords, tokens or keys in the shot.

After every test-server upgrade, also run the Studio smoke checks (`src/automaton-studio-vue/smoke/README.md`:
`npm run smoke` with `AUTOMATON_TEST_STUDIO_URL` set to the server's real Studio domain). They are read-only,
sign in as the administrator and a regular user, and leave a screenshot of each page in `smoke-results/`; attach the
ones your change affects to the PR. A failure is a finding to fix or report, not something to skip.

Name everything you create `e2e-<issue>-...` (apps, projects, API keys), so agents checking different issues do not
collide. Remove it when done: `python .agents/tools/test-server-cleanup.py` lists it, `--apply` removes it. Test
accounts stay. A nightly timer removes `e2e-` items older than a day.

## Labels and closing

- `verified`: every "Done when" check passed end to end on the test server, with a proof artifact.
- `verified-partial`: only unit tests or part of the checks ran; the proof says what is missing and why. If an agent
  can run the missing check, it is not partial yet: run it.
- `verification-failed`: a check failed; fix forward.
- `live`: set by whoever updates production, just before closing.

After labelling, comment the results (pass or fail per check) and every administrator action on the PR and link that
comment from the issue. Leave the issue open with board Status In Progress: `is:open label:verified` is what the next
production update ships.

**Close gate** (at the production update). Done means live on production. Whoever updates production checks it, then
closes each issue in that release with a comment naming the version (`.agents/tools/close-released-issues.py`), when:
- the merge gate held (`python3 .agents/tools/merge-gate.py <N>` output is on the PR);
- the issue has a `proof` artifact naming the release and how it was checked;
- the label is `verified` (a `verified-partial` issue closes only when the owner accepts the gap, said in a comment);
- the production version contains the merge commit.

## Production

Only when the task asks for it. Say in the issue or conversation what will change before doing it (wait for an answer
only if it deletes data, grants administrator rights or locks people out), take a backup before anything disruptive,
follow the production skills, check the public URLs afterwards and report what was done.
