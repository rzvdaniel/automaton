# Choose a Stack

Most users building Automaton apps today will use AI tools plus the CLI, not hand-follow a long human quickstart.

Use this page only to decide how much build tooling your app needs.

## Recommended default

If you do not have a strong reason to choose a framework, start with the smallest project shape that meets the need.

That is usually the best fit for:

- forms and dashboards
- internal tools and calculators
- planning apps
- small or medium single-page apps

## Choose a minimal app when

- you want the simplest possible project shape
- you do not need a component-heavy client
- you want the least build tooling

## Choose a framework when

- the user explicitly asks for one
- the app clearly benefits from reusable components, richer client state, or routing
- the team already has a preferred workflow

## What to tell the AI

After `automaton new` or `automaton link`, prefer the local generated guidance first. Use these files as the canonical fallback instructions:

- [llms.txt](https://automaton.monster/llms.txt)
- [App contract](https://automaton.monster/llms/automaton-app-contract.md)

Manual fallback if you need to drive the CLI yourself:

```bash
automaton login
automaton new "My App"
# or
automaton link <app-id>
automaton deploy
```

Use `automaton sync-ai` only if the generated local guidance files are missing or stale.

## Bottom line

- Default to the smallest project shape that meets the requirement.
- Add a build step when the app needs one, and describe its output in `automaton.json`.
- Choose a framework when the user asks for it or the app clearly needs richer client-side structure.
- After `automaton new` or `automaton link`, prefer the local generated guidance. Treat `llms.txt` as the canonical fallback, not the default.
