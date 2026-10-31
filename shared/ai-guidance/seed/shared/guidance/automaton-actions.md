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

Read credentials such as API keys from environment variables named in the
Action's documentation, never from source files. The app owner sets them as app
secrets in Studio (**App → Settings → Secrets**); each Action run receives them
as environment variables, and their values are removed from logs and results.

Deploy Actions with `automaton deploy`. Validate with
`automaton action validate <action-key>`. Invoke a deployed Action with
`automaton action run <action-key> --input '{...}'`.
