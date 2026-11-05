# Deliver and verify

## Where to test

All end-to-end checks run on the test server: the machine Automaton-Runner-01, Studio at
`studio.test.automaton.monster`. The exact URLs are in `accounts.env` below. An agent running on that machine is
already on the test server. Never test on production, and never use a local stack in place of the end-to-end check.

## Release

After the merge, wait for the `Runner image` and `Build production images` workflows on `main` to succeed; a release
pins the Runner image that exists at that moment. Then run `gh workflow run server-release.yml -f version=<next>` and
wait for it and for `Pack CLI and Runner`. The CLI is published when it changed; until NuGet indexes it, run the CLI
from `src/Automaton.Cli`.

## Upgrade the test server

Run `sudo bash /opt/automaton-server/update.sh --version server-v<next>` (without `--version` it may install the
previous release) and check `/opt/automaton-server/version`. When the Runner image changed, upgrade the Runners with
the command Studio shows under **Runners → Upgrade runner**.

## Test accounts

Credentials sit outside the repository in `~/.automaton-test/accounts.env` (on the test server
`~/.config/automaton-test/accounts.env`, mode 600):

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

New accounts are created by the system administrator in **Admin → Users → Add user** or `POST api/admin/users`, with
`"mustChangePassword": false`; add `"administrator": true` for a plain Admin and store its password in the
credentials file. On the test server agents act fully as the administrator (approve permissions, add users, change
settings) and report what they did.

New regular users have no Runner. To test as a regular user, put the app in a project that has a Runner, or deploy as
the administrator and use the regular user for the access checks.

## Verify as real users

Against the test server API, the CLI and Studio, check:

- the happy path of the change;
- each role boundary: a regular user and a stranger are refused, a plain Admin versus a SysAdmin;
- the error messages a person will see.

Check pages in a browser yourself with `agent-browser` (see `AGENTS.md`; on the test server `/home/agent/ab-login.sh`
and `ab-shot.sh` log in and take screenshots), following the studio-design visual checklist. When the change is a Studio
action (a button, a form), do it by clicking in Studio with `agent-browser`, not through the API. Record what passed
and what you could not check.

Proof on the issue: GitHub shows artifacts on issues, not on pull requests. Write the proof as one HTML page and
publish it on the **issue** the PR closes with the helper, then comment its link on the PR:

```bash
python3 .agents/skills/automaton-implement-issue/proof.py <issue> proof.html
```

The page lists each done criterion from the issue, the evidence for it (the command and what came back, or a link
to a screenshot) and pass, fail or not checked with the reason. Keep it plain: one `<table>` (criterion, result,
evidence), inline `<style>` only, no scripts, no embedded images. Running `proof.py` again replaces the issue's `proof`.

Then label the issue: `verified` when every criterion passes (or is "not checked" with a reason the owner accepted),
`verification-failed` when a check fails. Swap the label when the fix-forward release passes its re-check.

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

Name everything you create `e2e-...` (apps, projects, API keys). Remove it when done:
`python .agents/tools/test-server-cleanup.py` lists it, `--apply` removes it. Test accounts stay. A nightly timer
removes `e2e-` items older than a day.

## Production

Only when the task asks for it. Say in the issue or conversation what will change before doing it (wait for an answer
only if it deletes data, grants administrator rights or locks people out), take a backup before anything disruptive,
follow the production skills, check the public URLs afterwards and report what was done.
