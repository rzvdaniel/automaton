---
title: Getting started
description: Install the CLI, create an Automaton app, and deploy it.
---

# Quickstart

Install and authenticate the CLI:

```bash
dotnet tool install -g Automaton.Cli
automaton login
```

Create a project and link it to a remote app:

```bash
automaton new "My App"
```

The command writes `automaton.json`, `AGENTS.md`, and the local Automaton app
skill. Read those files before implementing the app.

Choose any technology and source layout. Configure the generic application
contract, then validate and deploy:

```bash
automaton validate
automaton deploy
```

Wait for the asynchronous deployment to reach `Succeeded`, then verify the URL.
Publish when it is within the requested app delivery or an existing standing authorization. Do not ask for separate publication approval again; honor any user restriction to keep the app private or unpublished. For authorized non-interactive publication, use `automaton publish --yes` and verify the published URL.

For an existing remote app, use `automaton link <app-id>`. Use
`automaton sync-ai` to restore the two local guidance files if they are missing.
