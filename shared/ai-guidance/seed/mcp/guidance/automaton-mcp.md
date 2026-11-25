# Automaton MCP

An Automaton server has an MCP endpoint at `/mcp` (for example
`https://api.your-domain.com/mcp`). Claude can use it to create, deploy, share
and pause apps and to read how they are doing, as the signed-in user.

## Connect

- **Claude Code, with an API key:**
  `claude mcp add --transport http automaton https://api.your-domain.com/mcp --header "Authorization: Bearer <your API key>"`.
  Without a key, or with a wrong one, the endpoint answers 401.
- **Claude on the web or phone, as a custom connector:** in Claude open
  Settings, Connectors, add a custom connector and enter the `/mcp` address.
  Claude sends the user to the server's sign-in page; they sign in, check the
  connector's name and where it returns to, and press Approve. Access lasts an
  hour and renews while Claude uses it. Each connection shows under Settings,
  API Keys as "Claude connector"; deleting it ends access. The server must be
  reachable from the internet over https.

## What the tools do

- List the user's apps and read one app's status, activity, logs (the last 500
  lines at most) and README.
- `create_app` creates an app. `deploy_app` deploys it from files you write (up
  to 50 MB), waits until it is Running, or returns the failure with the latest
  log lines.
- `share_app` chooses who can open it: only the user, people, team, server or
  internet.
- Pause and resume an app.
- Search and install from the catalog.
- `app_guidance` returns the guidance for building Automaton apps.
- `missing_secrets` names the secrets an app still needs and links to Studio to
  fill them.

## Limits

- You can change only the apps the user's key or sign-in can change.
- You cannot delete apps and cannot administer the server.
- Secret values never pass through MCP. Tell the user which secrets to set in
  Studio or the CLI.

## MCP or the CLI

Use MCP when it is connected: it needs no local install and works from Claude
on the web or phone. Use the CLI for local work in a project folder: `automaton
login`, `new`, validation and deployment from the files on disk. Publishing to
the app store needs the user's explicit approval either way.
