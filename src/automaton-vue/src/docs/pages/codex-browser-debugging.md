# Codex Browser Debugging

Use this workflow when building an Automaton Monster app locally with Codex and you want the agent to inspect the running UI, click through flows, read console errors, and capture screenshots.

## 1. Let Codex run the app

Ask Codex to start the app's local dev or preview server. It can usually detect the package manager and script from the project files.

If the server is already running, give Codex the URL instead, for example `http://127.0.0.1:4173`.

## 2. Initialize Codex Browser

In a fresh Codex session, run:

```text
/browser
```

The Browser plugin can be installed before this step, but the Codex In-app Browser runtime is not attached until `/browser` initializes it for the current Codex window/session.

Before initialization, browser targets may be empty:

```js
await agent.browsers.list()
// []
```

After initialization, the in-app browser target should be available:

```js
await agent.browsers.list()
// ["iab"]
```

`iab` means Codex can attach to the shared in-app browser and work with local pages.

## 3. Ask Codex to test the app

Use a direct prompt:

```text
Use the Browser tool to open http://127.0.0.1:4173.
Inspect console errors, test the main flows, capture desktop and mobile screenshots, and patch any issues you find.
```

For an Automaton app, include the deploy loop:

```text
Run the local app, use Browser QA to inspect it, fix visible/runtime issues, then run automaton deploy. Ask me before publish.
```

## What Codex Browser is good for

- interactive browser automation
- DOM inspection
- visible-state checks
- screenshots
- click, type, and navigation flows
- console/runtime debugging
- localhost app testing

This gives Automaton a useful local loop: build the app, open it, verify it in a real browser, fix issues, then deploy.

## Known limitations

- Run `/browser` once per Codex session before expecting the `iab` target.
- OAuth and auth popups may not behave like your normal browser.
- Cookies and session sharing may differ from your daily browser profile.
- `file://` and `data:` URLs may be blocked.
- Remote containers, WSL, and unusual port forwarding setups can need extra network configuration.
- If `agent.browsers.list()` still returns `[]`, restart the Codex session, run `/browser` again, and reopen the local app in the shared browser.

## Codex Browser vs. Playwright

Use Codex Browser for interactive UI QA while the agent is still building and fixing the app.

Use Playwright for deterministic automated checks that should run the same way every time, such as CI smoke tests, regression tests, screenshot baselines, and form-flow assertions.

They complement each other: Codex Browser is the exploratory local debugging loop; Playwright is the repeatable test suite.
