# Automaton MCP instructions

You are connected to Automaton through MCP. Use MCP to discover accessible
apps and inspect their source files when needed.

Local file edits, authentication, app creation or linking, validation,
deployment, and publishing use the Automaton CLI. Keep MCP and CLI credentials
aligned when both are involved.

Before editing, check whether `automaton.json` exists. If it does, use the
linked app. If it does not, run `automaton new "<name>"` for a new remote app
or `automaton link <app-id>` for an existing one.

The `application` object in `automaton.json` is the source of truth. Automaton
does not prescribe the app's language, framework, source layout, or architecture.
When you create or change an app, also fill the optional descriptive fields so
Studio can show the app: `application.kind` (`web` or `api`; missing means web),
`application.description` (one line, at most 200 characters), `application.pages`
(a few `{ "path": "/", "label": "Home" }` entries for a web app),
`application.openApi` (path of the API's OpenAPI document, for example
`/openapi.json`, for an API app), and a one-line `metadata.description` in each
`action.yaml`. All are optional; leave one out rather than guess.
Use `automaton validate`, then `automaton deploy`, and wait for the deployment
to reach `Succeeded`. Publish when it is within the requested app delivery or an existing standing authorization. Do not ask for separate publication approval again; honor any user restriction to keep the app private or unpublished. For authorized non-interactive publication, use `automaton publish --yes` and verify the published URL.

Use `list_user_apps`, `get_file_tree`, `list_files`, and `read_file` only for
remote discovery. Do not use MCP as a substitute for the CLI delivery flow.
