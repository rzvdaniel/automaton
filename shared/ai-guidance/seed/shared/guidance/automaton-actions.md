# Automaton App Actions

Actions are executable modules owned by an Automaton App. They are never standalone resources. Use an Action for finite background work such as imports, exports, reports, notifications, maintenance, and long-running jobs that should not run inside the App backend process.

## Layout

```text
actions/{action-key}/
  action.yaml
  main.py          # or main.sh
  requirements.txt # optional for Python
```

Keys use lowercase letters, digits, and internal hyphens. The manifest key must match the folder name when specified. Entrypoints must stay inside the Action folder.

## Process contract

- Read one UTF-8 JSON value from stdin.
- Write exactly one UTF-8 JSON result to stdout.
- Write operational logs to stderr.
- Exit zero for success and non-zero for failure.
- Read declared secrets from environment variables and never echo them.

Python Actions are ordinary scripts; Bash Actions run through `/bin/bash`. Do not create an HTTP listener or frontend scaffold inside an Action. Validate with `automaton action validate <action-key>`, deploy through `automaton deploy`, and run with `automaton action run <action-key> --input '{...}'`.
