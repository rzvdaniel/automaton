# Automaton app

This folder contains an Automaton app. The local workflow is in
`.automaton/agents/skills/automaton-app/SKILL.md`.

Read that skill before changing the app. It is the complete local workflow;
this file is only the entry point.
Use the CLI's documented commands and preserve the user's chosen technology.

`automaton.json` is the deployment contract. Automaton does not prescribe a
language, framework, package manager, source layout, or application architecture.

Prefer small, maintained images compatible with the app. Separate build tools
from production runtimes, and use the shared static-site contract for static
output. Follow the image-selection guidance in the generated skill; preserve the
user's chosen technology and verify dependencies before choosing a minimal image.

Keep the contract accurate when build, runtime, health, storage, or execution
behavior changes. Keep durable data in the configured data directory and keep
secrets out of source-controlled files.

If `automaton.json` is missing and the user wants a new remote app, run
`automaton new "<name>"`. If the user has an existing remote app, run
`automaton link <app-id>`.

After implementing the app, run `automaton validate` and fix every error. Run
`automaton deploy` and wait for the asynchronous operation to reach
`Succeeded` before claiming completion. Verify the app's main behavior through
its real entry point, then report the observed result as proof. A successful
deployment status or reachable URL alone is not proof that the app works.

If deployment fails, inspect the reported operation and correct the source or
contract before retrying. Do not claim a successful deploy from a local build.

Publish when it is within the requested app delivery or an existing standing authorization. Do not ask for separate publication approval again; honor any user restriction to keep the app private or unpublished. For authorized non-interactive publication, use `automaton publish --yes` and verify the published URL.
