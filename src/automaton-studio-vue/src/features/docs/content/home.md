# Automaton Documentation

Use this section as the starting point for building and deploying apps in Automaton.

## Recommended path

For most developers, the least-friction workflow is:

1. Create or link the app in Automaton.
2. Keep the project in your local repo.
3. Build with your preferred editor or AI tool.
4. Deploy with `automaton deploy`.

If your AI tool supports MCP, connect Automaton as an MCP server. That gives the AI the most accurate Automaton-specific context. If it does not, use the CLI + AI workflow, the stack guide, and the starter prompt.

## What every Automaton app has

- A frontend that produces static files.
- A Python backend in `backend/`.
- An `automaton.json` file at the project root.
- A deployment step using the Automaton CLI.

## Use this documentation menu for

- A quick stack guide for plain HTML/CSS/JS, React, and Vue.
- The in-browser App Editor workflow.
- External AI workflows with CLI and starter prompts.
- MCP setup.
- Templates.

## Core contract

- Backend controller files live in `backend/`.
- Each controller exports `handle(action_name, inputs)`.
- Each action returns `{ success, data, error }`.
- Frontend code calls `window.automatonApi.callController(controller, action, inputs)`.
- `callController()` returns `data` directly and throws on error, so use `try/catch`.