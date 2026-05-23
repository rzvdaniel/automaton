# Publish

`automaton publish` makes the latest deployed version publicly accessible.

```bash
automaton publish
```

## Recommended flow

1. Build the app locally
2. Run `automaton deploy`
3. Ask whether you want it public yet
4. Run `automaton publish`

## Why publish is separate

Deploy is the technical sync step.

Publish is the visibility step.

That means you can deploy safely without immediately exposing the app.

## Typical AI prompt

```text
Build a poker planning app for Automaton Monster.
Deploy with automaton deploy.
Ask me before publish.
```

## Useful options

```bash
automaton publish --slug poker-planning
automaton publish --yes --json
```

Use `--yes` for non-interactive automation after approval has already been given.
