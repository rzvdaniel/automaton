# Experimental MCP Endpoint

This note is intentionally internal-only.

## Status

The Automaton MCP endpoint is still implemented, but it is currently:

- not documented in public docs
- not shown in Studio docs navigation
- not recommended in starter prompts
- not part of the default app-building workflow

The product-facing recommendation is CLI-only:

1. `automaton login`
2. `automaton new` or `automaton link`
3. `automaton validate`
4. `automaton deploy`

## Endpoint

- Hosted/local path: `/api/mcp`
- Related internal prompt file: `src/Automaton.Studio.Server/SystemPrompts/ExternalMcpInstructions.md`

## Why It Is Hidden For Now

MCP created unnecessary workflow ambiguity alongside the CLI:

- two possible app-creation/link paths
- two possible auth contexts
- app lookup mismatches when MCP and CLI used different accounts or servers

That made the AI experience less reliable than the CLI-only path.

## Future Reintroduction

If we bring MCP back to user-facing docs, it should return only after we can guarantee one of these:

1. MCP cannot create or relink app identity independently of the CLI.
2. MCP and CLI share the exact same app/account/server context automatically.
3. The MCP surface is explicitly limited to read-only guidance and remote inspection.
