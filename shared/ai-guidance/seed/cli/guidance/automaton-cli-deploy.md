# Automaton CLI deploy workflow

Use this when an Automaton app already exists locally.

## Required state

- `automaton login` has completed.
- `automaton.json` exists in the project root.
- The `application` object declares structured build/runtime commands, port,
  optional health settings, environment, and persistent storage as needed.
- New apps should be created with `automaton new "My App"`; existing apps can be
  linked with `automaton link <app-id>`.

## Deploy

```bash
automaton deploy
```

The CLI validates the generic contract, packages the project, and uploads it to
the selected deployment target. A normal remote deployment does not execute the
application build locally. The Runner executes the declared build once, selects
the configured output (or post-build workspace), stages the artifact, and then
starts the declared runtime when one exists.

Useful options:

```bash
automaton deploy --json
automaton deploy --clean
automaton deploy --runner <runner-id-or-name>
automaton deploy --env <name>
```

Do not say deployment is complete until `automaton deploy` succeeds. On failure,
report the exact command, error, and next required step. Use `automaton status`
to inspect the selected target and its generic runtime state.

Publishing is separate and requires explicit approval:

```bash
automaton publish --yes
```

The application source layout is not part of the platform contract. Generated
files, dependency directories, caches, secrets, and persistent data are not
uploaded as disposable runtime artifacts according to the CLI archive rules.
Technology-specific templates are optional examples and must emit the same
generic `application` contract.
