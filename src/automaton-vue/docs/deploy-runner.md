---
title: Deploy a Runner
description: Set up a self-hosted Automaton Runner to execute backend apps locally or on your own infrastructure using Docker.
---

# Deploy a Runner

A Runner executes backend code for your Automaton apps.

Frontend apps are hosted by Automaton.

Backend apps run on a Runner that you control.

## Create an API key

Create an API key in Automaton Studio before installing a Runner.

Open the API Keys menu:

[https://studio.automaton.monster/apikeys](https://studio.automaton.monster/apikeys)

Then create a new API key and copy it before starting the Runner install.

## Install a Runner

The easiest way is through the CLI:

```bash
automaton runner install
```

## Requirements

- Docker installed
- Automaton account
- Automaton API key

## Verify the connection

Open the Runners page in Automaton Studio.

Your Runner should appear as connected.

You can also run:

```bash
automaton runners
```

## When do you need a Runner?

You need a Runner when your app contains a backend folder.

```txt
backend/
```

Frontend-only apps do not require a Runner.

## Typical workflow

```bash
automaton login
automaton new "My App"
automaton runner install
automaton deploy
automaton publish
```
