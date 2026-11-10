# Environment (example)

Skills and `AGENTS.md` say "the test server", "production" and "the board" generically. This file names them for one
contributor or team. Copy it to `.agents/environment.md` (not committed: it is in `.gitignore`), fill it in, and keep a
private copy. Never put secret values here: name where they live.

If you have no test server, test locally with the README quickstart or on your own Automaton install, and say in the PR
that the end-to-end check runs after release on a maintainer's test server.

## Test server
- Studio: `https://studio.<test-domain>`; API: `https://api.<test-domain>`.
- SSH: `ssh <alias>` (root) and, if agents run there, `ssh <alias>-agent` (the unprivileged user agents use).
- Test logins: `<path to accounts.env>` (keys in `.agents/skills/automaton-implement-issue/verify.md`). Read values
  literally; never `source` it; never print it.
- Mail catcher (Mailpit) container and how to reach its API, if any.
- Browser helpers (login and screenshot scripts for `agent-browser`), if any.

## Production
- Who may change it, and what is pre-approved (for example releases and updates of verified work).
- SSH alias and API, Studio, store and website addresses.

## Fresh-install test servers
- Server, SSH access and the DNS zone used for from-scratch install tests. Use a fresh base name per install (Let's
  Encrypt issues only 5 certificates a week for the same set of names).

## Boards
- GitHub project boards, their numbers and fields, and which parent issue each kind of work goes under. Contributors
  without board access skip this: file the issue and the maintainers place it.

## Agents
- Which agents run where (for example a headless agent on the test server), how to start them and where their logs are.
