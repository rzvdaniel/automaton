# Automaton Actions

Use an Action for finite work such as an import, export, report, notification,
maintenance task, or job. An Action is not an HTTP application runtime.

The manifest lives at:

```text
actions/{action-key}/action.yaml
```

The manifest selects the Action entrypoint and its declared settings. Keep the
entrypoint inside its Action directory.

An Action receives one UTF-8 JSON value on standard input, writes one UTF-8
JSON result to standard output, writes operational logs to standard error, and
uses a zero exit code for success. It must not write secrets to output or logs.

Read credentials such as API keys from environment variables, never from
source files. Declare each one the app needs in `automaton.json` under
`application.secrets`, for example
`"secrets": [{ "name": "STRIPE_KEY", "description": "Stripe secret key" }]`.
Each Action run and the running app receive them as environment variables, and
their values are removed from Action logs and results.

Run `automaton secrets status` to see which declared secrets are missing. Never
ask for a secret value in chat and never put one in a command argument. Ask the
user to run `automaton secrets set NAME` themselves (it prompts without echo),
or pipe a value that is already in an environment variable:
`printf '%s' "$VALUE" | automaton secrets set NAME`. Add `--project` to share it
with every app in the project, or `--user` for all of the user's personal apps.

Platform access such as reading server backups is requested in
`application.permissions` (for example `["backups:read"]`) and needs a system
administrator's approval in Studio; no agent or API key can approve it. After
deploying, run `automaton permissions status` and give the user the approval
link it prints. The app receives its key as `APP_AUTOMATON_API_KEY` on its next
start after approval.

Deploy Actions with `automaton deploy`. Validate with
`automaton action validate <action-key>`. Invoke a deployed Action with
`automaton action run <action-key> --input '{...}'`.
