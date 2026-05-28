# Automaton MCP (Optional)

Use this only when the AI environment already supports MCP tools or the user explicitly wants to work through Automaton MCP.

## Role of MCP vs CLI

Keep the boundary clear:

- **MCP** = inspect, understand, discover existing Automaton apps and source files
- **CLI** = create/link local projects, build, deploy, publish

Do not treat MCP as the main execution path for app delivery.

## When to use MCP

If Automaton MCP tools are already available in the AI environment, use them as an optional enhancement for:

- listing accessible Automaton apps
- inspecting the source file tree of an existing app
- reading source files from an existing app

Use MCP when it helps the AI understand an existing app before making local changes.

## When not to use MCP

Do not use MCP as the primary path for:

- deployment
- publishing
- replacing the normal local CLI workflow
- secrets handling
- runtime state inspection beyond the documented read/discovery tools

Do not ask the user for an MCP server URL or MCP setup by default during normal app generation. That adds friction to the common workflow.

## Default behavior

The default user flow should remain:

```bash
automaton setup
automaton new "My App"
automaton deploy
```

or, for an existing app:

```bash
automaton setup
automaton link <app-id>
automaton deploy
```

## MCP tools to use when available

Use Automaton MCP tools when available to inspect existing Automaton apps:

- `list_user_apps`
- `get_file_tree`
- `read_file`
- `list_files`

Recommended sequence:

1. `list_user_apps`
2. choose an `appId`
3. `get_file_tree(appId)` or `list_files(appId)`
4. `read_file(appId, path)`

## CLI tools to use for delivery

Use the Automaton CLI for:

- `automaton setup`
- `automaton new`
- `automaton link`
- `automaton deploy`
- `automaton publish`

Recommended workflow boundary:

1. Use MCP to understand the app/source.
2. Modify files locally.
3. Use the Automaton CLI to deploy/publish.

## If the user asks about MCP setup

Only if the user explicitly wants MCP-based inspection or asks how to connect it, explain that:

- Automaton exposes an MCP server for compatible AI tools
- MCP is optional
- it is useful for inspecting existing Automaton apps
- the CLI remains the primary path for deployment and publishing

If MCP tools are not already available in the AI environment, continue with local files and the Automaton CLI instead of blocking on MCP setup.
