---
title: Deploy a Runner
description: Set up a self-hosted Automaton Runner to execute backend apps locally or on your own infrastructure using Docker.
---

# Deploy a Runner

A Runner executes backend code for your Automaton apps.

Frontend apps are hosted by Automaton.

Backend apps run on a Runner that you control.

## Install a Runner

In Automaton Studio, open **Runners** and select **Install Self-Hosted Runner**. Give the Runner a name, choose a profile, then copy and run the one-time Docker command on the machine that will run your backend apps.

Choose **Standard** for ordinary application workloads. Choose **Privileged Host** only for trusted apps that need administrator-level access to the machine. That profile can inspect and affect host files, processes, Docker containers, services, system state, and secrets, so Studio requires an acknowledgement before it creates the installation command.

The command contains a short-lived, one-time enrollment credential. Copy it directly to the target machine, do not share it, and return to **Runners** to confirm that the Runner has connected.

The CLI is also available:

```bash
automaton runner install
```

## Requirements

- Docker installed
- Automaton account
- Access to your Automaton Studio project

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
