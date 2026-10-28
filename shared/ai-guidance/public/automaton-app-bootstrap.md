# Starting an Automaton app

If you are in an empty directory and the user asks you to build an Automaton app:

1. Check whether the `automaton` CLI is available with `automaton --help`.
2. If it is missing, follow the official [CLI installation instructions](/docs/automaton-cli).
3. Authenticate with `automaton login` if necessary, then run `automaton new "My App"`
   using a name appropriate to the user's request.
4. Before implementing the app, read the generated `AGENTS.md`,
   `.automaton/agents/skills/automaton-app/SKILL.md`, and `automaton.json`.
5. Follow those local instructions and the deployment contract as the authoritative
   workflow for the project. Public guidance provides discovery and bootstrap;
   the generated files guide app development.
6. Choose the language, framework, and build tools after inspecting the user's
   requirements and available runtime environment. Do not assume that tools
   installed locally are available on the assigned Runner.

Assign a Machine Management Runner to the app's deployment target in Studio or
with `automaton target add`. Do not assume an unassigned or unsupported Runner
can host the app.

For an existing remote app, use `automaton link <app-id>` instead of creating
another app, then read the generated local guidance before making changes.
