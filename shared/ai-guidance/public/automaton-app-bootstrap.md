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

Prefer a Standard or Hosted Runner for ordinary applications. Do not assign a
Privileged Host Runner as an automatic fallback, and never pass
`--acknowledge-privileged-host-access` unless the user has explicitly approved
privileged host access for that app and environment. If only a Privileged Host
Runner appears usable, stop and ask the user for that approval.

For an existing remote app, use `automaton link <app-id>` instead of creating
another app, then read the generated local guidance before making changes.
