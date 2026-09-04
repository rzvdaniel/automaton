# Automaton app

This folder contains an Automaton app. The local workflow is in
`.automaton/agents/skills/automaton-app/SKILL.md`.

Read that skill before changing the app. It is the complete local workflow;
this file is only the entry point.
Use the CLI's documented commands and preserve the user's chosen technology.

`automaton.json` is the deployment contract. Automaton does not prescribe a
language, framework, package manager, source layout, or application architecture.

Keep the contract accurate when build, runtime, health, storage, or execution
behavior changes. Keep durable data in the configured data directory and keep
secrets out of source-controlled files.

If `automaton.json` is missing and the user wants a new remote app, run
`automaton new "<name>"`. If the user has an existing remote app, run
`automaton link <app-id>`.

After implementing the app, run `automaton validate` and fix every error. Run
`automaton deploy` and wait for the asynchronous operation to reach
`Succeeded` before claiming completion. Verify the deployed URL.

If deployment fails, inspect the reported operation and correct the source or
contract before retrying. Do not claim a successful deploy from a local build.

Never run `automaton publish` without the user's explicit approval.
