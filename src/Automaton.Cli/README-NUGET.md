# Automaton CLI

Automaton CLI is the .NET global tool for creating, validating, deploying, and publishing Automaton apps.

## Install

```bash
dotnet tool install --global Automaton.Cli
```

## Common commands

```bash
automaton login
automaton new "My App"
automaton pull
automaton pull my-remixed-app
automaton deploy --non-interactive
automaton deploy --publish --yes --non-interactive
automaton publish --yes --non-interactive
automaton --version
```

## Pull modes

```bash
# Refresh the current linked local project from Studio source files
automaton pull

# Create a new local folder from an existing Studio app
automaton pull <app-id-or-slug>
```

`automaton pull <app-id-or-slug>` creates a linked local folder with:

```text
my-app/
  automaton.json
  frontend/
  backend/   # only when source/backend exists
```

The CLI pulls portable source files only. It does not pull `live/backend`, and it does not use `live/frontend` fallback unless that behavior is added explicitly later.

## Learn more

- Documentation: https://automaton.monster/cli
