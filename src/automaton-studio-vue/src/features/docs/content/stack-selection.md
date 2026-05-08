# Choose a Stack

Most users building Automaton apps today will use AI tools plus the CLI, not hand-follow a long human quickstart.

Use this page only to decide which frontend stack to ask the AI for.

## Recommended default

If you do not have a strong reason to choose a framework, start with plain HTML, CSS, and JavaScript.

That is usually the best fit for:

- forms
- dashboards
- internal tools
- calculators
- planning apps
- small or medium single-page apps

## Choose plain HTML/CSS/JS when

- you want the simplest possible project shape
- you want root-level frontend files by default
- you do not need a component-heavy SPA
- you want the least build tooling

## Choose React when

- the user explicitly asks for React
- the app clearly benefits from reusable component structure
- you expect richer SPA state and routing
- the team already prefers a React workflow

## Choose Vue when

- the user explicitly asks for Vue
- the team already uses Vue
- you want a component-based SPA with Vue tooling and conventions

## What to tell the AI

Start with the canonical instructions:

- [llms.txt](https://automaton.monster/llms.txt)
- [Starter prompts](https://automaton.monster/llms/automaton-starter-prompts.md)
- [Frontend structure](https://automaton.monster/llms/automaton-frontend-structure.md)

For the normal local workflow, the CLI happy path is:

```bash
automaton setup
automaton new "My App"
# or
automaton link <app-id>
automaton validate
automaton deploy
```

Use `automaton sync-ai` only if the generated local guidance files are missing or stale.

## Bottom line

- Default to plain HTML/CSS/JS.
- Use React or Vue when the user asks for them or the app clearly needs a heavier SPA structure.
- Treat `llms.txt` as the source of truth, not this page.