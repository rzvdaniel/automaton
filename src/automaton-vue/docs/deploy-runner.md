---
title: Deploy a Runner
description: Install a Machine Management Runner to deploy Automaton apps and Actions.
---

# Deploy a Runner

A Machine Management Runner hosts app runtimes and isolated Actions on a server you control. The server needs Ubuntu and a running Docker Engine. The Runner itself has access to the machine's Docker socket, so install it only on a trusted server.

## Install

In Automaton Studio, open **Runners** and select **Install Machine Management Runner**. Enter a name, acknowledge the machine access warning, and copy the generated one-time command to the server. Do not share the enrollment credential. Return to **Runners** and confirm that the Runner connects.

You can list connected Runners from the CLI:

```bash
automaton runners
```

When it is the only online Runner available to the app, `automaton deploy` assigns it to the app's Live environment automatically. With several online Runners, choose one with `automaton set-runner <runner>` or `automaton deploy --runner <runner>`. A Runner can host multiple app environments on the same machine.

## Deploy an app

```bash
automaton validate
automaton deploy
```

Wait for deployment to reach `Succeeded` before publishing or reporting success.
