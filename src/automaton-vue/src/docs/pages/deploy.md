# Deploy

`automaton deploy` uploads your app runtime to Automaton.

For most users, deploy means:

```bash
automaton deploy
```

## What deploy does

- Uploads your frontend runtime
- Uploads your backend runtime when your app has backend code
- Makes backend APIs active automatically when an execution target is already available

Backend apps need an execution target to run APIs, but deploy can still upload the backend before one exists.

## If no execution target exists yet

Deploy can still succeed.

In that case, the CLI explains that:

- the app was deployed
- backend runtime was uploaded
- backend APIs are not active yet

Then it tells you the next step.

Most users do not need to think about infrastructure details first. Just run deploy and follow the CLI output.

## Common commands

```bash
automaton deploy
automaton deploy --publish
automaton deploy --runner my-runner
```

## Recommended mental model

- `deploy` = ship the current app
- `publish` = make it public

That is the important distinction.
