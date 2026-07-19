# API Key Types and Permissions

Automaton uses API keys for the CLI, workflow integrations, web apps, and managed runner processes.

## Developer / CLI

The recommended user-created key type is **Developer / CLI** (internal scope value `7`, prefix `ak_developer_`). Use it with the Automaton CLI to:

- read and update the user's apps;
- deploy frontend and backend source;
- publish and unpublish apps;
- read deployment status and logs; and
- download AI guidance and run ordinary user workflows.

Developer keys do not grant API-key management, billing, administration, infrastructure-runner enrollment, or access to another user's resources. Studio account and administrator policies still apply separately.

## Advanced key types

Advanced types are available when an integration needs a narrower credential:

### Read Only

Read account and execution data for monitoring, reporting, and dashboards. It cannot mutate apps or execute workflows.

### Workflow Execution

The internal scope is `WorkflowExecution` (value `2`, prefix `ak_workflow_`). It can run workflows and read their execution status, but does not provide general app-management access. Existing `ak_execute_` credentials remain valid as legacy credentials.

### Web App (Experimental)

Web App keys remain experimental. They should not be embedded in browser code as permanent account-wide secrets. A future implementation should bind them to one app and use a restricted runtime identity or short-lived token exchange.

## System-generated credentials

Users cannot manually create **Runner** or **Infrastructure Runner enrollment** keys. Automaton generates those credentials for managed runner and host-registration workflows.

**Full Access** is retained only for legacy credentials and compatibility. It is not offered as a new user-facing key type.

## CLI authentication

Use a Developer / CLI key with the CLI:

```bash
automaton login --key ak_developer_xxx
automaton deploy
automaton publish
```

For unattended environments, prefer an environment variable or a masked prompt rather than placing a secret directly in shell history:

```powershell
$env:AUTOMATON_API_KEY = "ak_developer_xxx"
automaton login
```

Browser and device authorization for `automaton login` is planned. It should create a Developer / CLI key only after explicit approval in Studio.
