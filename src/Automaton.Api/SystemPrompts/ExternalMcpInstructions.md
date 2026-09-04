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
Use `automaton validate`, then `automaton deploy`, and wait for the deployment
to reach `Succeeded`. Never publish without explicit user approval.

Use `list_user_apps`, `get_file_tree`, `list_files`, and `read_file` only for
remote discovery. Do not use MCP as a substitute for the CLI delivery flow.
