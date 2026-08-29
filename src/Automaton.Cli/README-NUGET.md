# Automaton CLI

Automaton CLI is the .NET global tool for creating, validating, deploying, and publishing Automaton apps.

## Install

```bash
dotnet tool install --global Automaton.Cli
```

## Common commands

```bash
automaton login
automaton login --local
automaton login --device
automaton new "My App"
automaton pull
automaton pull my-remixed-app
automaton deploy --non-interactive
automaton deploy --publish --yes --non-interactive
automaton publish --yes --non-interactive
automaton --version
```

`automaton login` opens Automaton Studio and waits for you to approve a short-lived
device authorization request, then stores the credential globally for reuse across
Automaton apps. Use `automaton login --local` when a project needs an isolated
credential. If a browser cannot be opened, copy the displayed URL and code to another
device. For CI/CD and other unattended flows, set `AUTOMATON_API_KEY` and run the
required command directly; environment credentials are never persisted.

## Pull modes

```bash
# Refresh the current linked local project from Studio source files
automaton pull

# Refresh and allow overwriting matching local files without a prompt
automaton pull --force

# Create a new local folder from an existing Studio app
automaton pull <app-id-or-slug>
```

`automaton pull <app-id-or-slug>` creates a linked local folder with the generic
`automaton.json` application contract and portable source files. The source
layout is not prescribed by the CLI:

```text
my-app/
  automaton.json
  <application source chosen by the project>
```

The CLI pulls portable source files only. It does not pull disposable runtime
output or runner state.

Refresh mode safety rules:

- `automaton pull` checks whether matching local source files already exist.
- In interactive mode, pull warns before overwriting those matching files and asks for confirmation.
- In non-interactive mode, pull refuses to overwrite matching files unless you pass `--force`.
- Local-only files are kept. Pull is not a clean mirror and does not delete extra local files.

## Learn more

- Documentation: https://automaton.monster/cli
