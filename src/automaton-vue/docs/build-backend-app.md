---
title: Run server code
description: Configure an Automaton runtime for server-side logic, integrations, and durable application data.
---

# Run server code

Use an Automaton runtime when the app needs server-side logic, private
credentials, integrations, or durable data. The runtime technology and source
layout are application choices.

## Configure the runtime

Declare `application.runtime.command` in `automaton.json`. Set its executable,
arguments, working directory, environment, and requested port. Automaton gives
the process the effective port through `AUTOMATON_PORT`.

Add an optional `application.health.path` when the process exposes an HTTP
readiness endpoint. Store durable files below `AUTOMATON_APP_DATA_DIR`.

## Live updates

Automaton forwards each request to your app and returns the complete response
when the app has finished it. WebSockets and streamed responses, such as
Server-Sent Events, are not supported. For multiplayer games, dashboards, or
other live views, poll a small JSON endpoint, for example once a second.

## Deploy

```bash
automaton validate
automaton deploy
```

The deployment is asynchronous. Wait for `Succeeded` and verify the deployed
URL. A Runner must be available for a long-running runtime to start.

See the [automaton.json reference](/automaton-json) for the complete contract
and [Deploy a Runner](/deploy-runner) when you need a Runner.
