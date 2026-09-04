---
title: Deploy a Runner
description: Set up a self-hosted Automaton Runner for configured runtimes and Actions.
---

# Deploy a Runner

A Runner executes configured long-running runtimes and finite Actions. Choose a
Standard Runner for ordinary workloads. Use a Privileged Host Runner only for
trusted apps that need approved host capabilities.

## Install

In Automaton Studio, open **Runners**, select **Install Self-Hosted Runner**,
choose a profile, and copy the generated one-time command to the target host.
Do not share the enrollment credential. Return to **Runners** and confirm the
Runner connects.

The CLI also supports:

```bash
automaton runner install
automaton runners
```

The host needs Docker and access to the Automaton account. Keep privileged apps
private and follow the least-capability rule.

## Deploy an app

```bash
automaton validate
automaton deploy
```

The Runner performs the configured build and starts the configured runtime. Wait
for the deployment to reach `Succeeded` before publishing or reporting success.
