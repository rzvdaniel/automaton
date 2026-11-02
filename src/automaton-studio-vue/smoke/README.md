# Studio smoke checks

Read-only browser checks of a deployed Studio, run against the test server after each update (full-delivery
skill, step 9). They sign in once as the system administrator and once as a regular user, open the main pages,
and save a screenshot of each page in `smoke-results/`. They never create or change anything.

What they cover: dashboard; Admin → Users and the Add user dialog; Admin → Apps and, when an app exists, its
Permissions tab; Admin → Installation scrolling; dark theme; a direct load of `/apikeys` (#540); a regular user
refused from Admin; project members when the user is in a project; no horizontal scroll at phone width.

## Run

On the test server, as `agent`:

```bash
cd src/automaton-studio-vue
npm ci
AUTOMATON_TEST_STUDIO_URL=https://studio.<test domain> \
PLAYWRIGHT_CHROMIUM_EXECUTABLE=$(ls -d ~/.agent-browser/browsers/chrome-*/chrome | tail -1) \
npm run smoke
```

- `AUTOMATON_TEST_STUDIO_URL` is the server's real Studio domain (on the test server
  `https://studio.runner-01.automaton.monster`), not the hand-added `studio.test` alias, which does not get the
  routes added by updates.
- Credentials come from `~/.config/automaton-test/accounts.env` (`AUTOMATON_TEST_ACCOUNTS` overrides the path);
  they are never printed.
- `PLAYWRIGHT_CHROMIUM_EXECUTABLE` reuses agent-browser's Chromium; without it, Playwright needs its own browser
  (`npx playwright install chromium`).
- Run one browser at a time; the test server has little memory.
