---
title: Deploy an App
description: Deploy your Automaton app with the CLI using automaton deploy.
---

# Deploy

`automaton deploy` uploads your app runtime to Automaton.

For most users, deploy means:

```bash
automaton deploy
```

## What deploy does

- Uploads the configured public runtime
- Uploads optional server runtime code when configured
- Starts server execution automatically when an execution target is available

Any app with an HTTP/public endpoint needs an execution target for its runtime,
including static apps. Deploy can still upload that runtime before a target
exists.

## If no execution target exists yet

Deploy can still succeed.

In that case, the CLI explains that:

- the app was deployed
- server runtime was uploaded
- server execution is not active yet

The public URL becomes available only after the application-owned runtime starts
on a Runner. A build output directory alone does not create a public web app.

Then it tells you the next step.

Most users do not need to think about infrastructure details first. Just run deploy and follow the CLI output.

## Common commands

```bash
automaton deploy
automaton deploy --publish
automaton deploy --publish --non-interactive
automaton deploy --runner my-runner
```

If the Live environment has no Runner yet, `automaton deploy` assigns the only
online Machine Management Runner and reports it. With several online Runners,
choose one with `--runner`.

Use `automaton deploy <target-slug>` to select a remote deployment target. The
`--env <name>` option selects the local project profile that contains the app
link and credentials; it is not a remote target selector. Use
`automaton deploy --publish` to publish after deployment, or standalone
`automaton publish --yes` as the explicit non-interactive publish confirmation.

## Recommended mental model

- `deploy` = ship the current app
- `publish` = make it public
- AI/CI/scripts should use explicit publish flags instead of relying on follow-up prompts

That is the important distinction.
